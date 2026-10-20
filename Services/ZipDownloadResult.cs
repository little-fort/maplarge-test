using System.IO.Compression;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TestProject.Helpers;

namespace TestProject.Services
{
    /// <summary>
    /// Streams files and folders to the browser as one zip. Built while it downloads so nothing is
    /// held in memory or written to disk first
    /// </summary>
    public sealed class ZipDownloadResult : IActionResult
    {
        // Lists one folder, including hidden and system items
        private static readonly EnumerationOptions ContentsOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

        private readonly IReadOnlyList<string> _items;
        private readonly string _fileName;

        /// <param name="items">Full paths of already-checked files and folders in one folder</param>
        public ZipDownloadResult(IReadOnlyList<string> items, string fileName)
        {
            _items = items;
            _fileName = fileName;
        }

        public Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            var logger = http.RequestServices.GetRequiredService<ILogger<ZipDownloadResult>>();

            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(_fileName);
            http.Response.ContentType = "application/zip";
            http.Response.Headers.ContentDisposition = disposition.ToString();
            http.Response.Headers.XContentTypeOptions = "nosniff";

            // ZipArchive in .NET 8 only writes synchronously, which Kestrel refuses by default. Allowing
            // it for this response keeps the zip streaming, at the cost of a blocked thread per download.
            http.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;

            var writer = new Writer(logger, http.RequestAborted);
            try
            {
                using var zip = new ZipArchive(http.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var item in _items)
                {
                    var name = Path.GetFileName(item);
                    if (Directory.Exists(item))
                        writer.AddFolder(zip, item, name + "/");
                    else
                        writer.AddFile(zip, item, name);
                }
                writer.AddSkippedList(zip, _items.Select(item => Path.GetFileName(item)).ToHashSet(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (http.RequestAborted.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Zip download {FileName} was cancelled", _fileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Zip download {FileName} failed partway", _fileName);
                http.Abort();
            }

            return Task.CompletedTask;
        }

        // Writes one zip and keeps track of what was left out of it
        private sealed class Writer
        {
            private readonly ILogger _logger;
            private readonly CancellationToken _cancellationToken;
            private readonly List<string> _skipped = new();

            public Writer(ILogger logger, CancellationToken cancellationToken)
            {
                _logger = logger;
                _cancellationToken = cancellationToken;
            }

            public void AddFolder(ZipArchive zip, string folder, string entryPath)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // Checked again here rather than trusting the listing
                if (!PathResolver.IsReachable(folder))
                {
                    Skip(entryPath, "It was moved or replaced by a link during the download.");
                    return;
                }

                List<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ContentsOptions).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Left the folder {Path} out of a zip download", folder);
                    Skip(entryPath, Describe(ex));
                    return;
                }

                zip.CreateEntry(entryPath); // keeps empty folders
                foreach (var entry in entries)
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        Skip(entryPath + entry.Name, "It's a link, and links aren't followed.");
                    else if (entry is DirectoryInfo)
                        AddFolder(zip, entry.FullName, entryPath + entry.Name + "/");
                    else
                        AddFile(zip, entry.FullName, entryPath + entry.Name);
                }
            }

            public void AddFile(ZipArchive zip, string path, string entryPath)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // Checked again here rather than trusting the listing
                if (!PathResolver.IsReachable(path))
                {
                    Skip(entryPath, "It was moved or replaced by a link during the download.");
                    return;
                }

                // Verify file can be read by opening it first
                FileStream? source = null;
                DateTime lastWrite;
                try
                {
                    source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    lastWrite = File.GetLastWriteTime(source.SafeFileHandle);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    source?.Dispose();
                    _logger.LogWarning(ex, "Left {Path} out of a zip download", path);
                    Skip(entryPath, Describe(ex));
                    return;
                }

                using (source)
                {
                    var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
                    entry.LastWriteTime = lastWrite.Year is >= 1980 and <= 2107 ? lastWrite : new DateTime(1980, 1, 1); // the range zip dates can hold
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
            }

            // Lists what was left out, in a file at the top of the zip whose name no item there uses
            public void AddSkippedList(ZipArchive zip, ISet<string> topLevelNames)
            {
                if (_skipped.Count == 0)
                    return;

                var name = "Skipped items.txt";
                for (var number = 2; topLevelNames.Contains(name); number++)
                    name = $"Skipped items ({number}).txt";

                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.WriteLine("These items were left out of this download:");
                writer.WriteLine();
                foreach (var line in _skipped)
                    writer.WriteLine(line);
            }

            private void Skip(string entryPath, string reason) => _skipped.Add($"{entryPath.TrimEnd('/')}: {reason}");

            private static string Describe(Exception ex) => ex switch
            {
                FileNotFoundException or DirectoryNotFoundException => "It was moved, deleted or replaced by a link during the download.",
                UnauthorizedAccessException => "Access to it was denied.",
                _ => "It couldn't be read. It may be open in another program.",
            };
        }
    }
}
