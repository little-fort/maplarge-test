using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Enumeration;
using TestProject.Helpers;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers 
{
    [ApiController]
    [Route("[controller]")]
    public class DirectoryController : ControllerBase 
    {

        // Explicitly include hidden/system files and folders
        private static readonly EnumerationOptions ContentsOptions = new() { AttributesToSkip = 0 };

        private static readonly EnumerationOptions SearchOptions = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };

        public const int MaxSearchResults = 500;

        private readonly ILogger<DirectoryController> _logger;
        private readonly DirectorySizeService _directorySizes;
        private readonly FileActionService _fileActions;

        public DirectoryController(ILogger<DirectoryController> logger, DirectorySizeService directorySizes, FileActionService fileActions)
        {
            _logger = logger;
            _directorySizes = directorySizes;
            _fileActions = fileActions;
        }

        [HttpGet]
        public IActionResult Get([FromQuery] string path)
        {
            if (!PathResolver.TryResolveDirectory(path, out var directory))
                return NotFound();

            // Compile folder content data
            var dto = new List<ServerObject>();
            var subdirs = Directory.GetDirectories(directory);
            foreach (var dir in subdirs)
            {
                var info = new DirectoryInfo(dir);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName.Replace("\\", "/"),
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Type = ServerObjectType.Directory,
                    IsLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                });
            }

            var files = Directory.GetFiles(directory);
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName.Replace("\\", "/"),
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Size = info.Length,
                    DisplaySize = MiscHelpers.FormatDisplaySize(info.Length),
                    Extension = MiscHelpers.GetExtensionLabel(info.Name),
                    Type = ServerObjectType.File,
                    IsLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                });
            }

            // Add metadata
            var currDirectory = new DirectoryInfo(directory);
            var current = new ServerObject
            {
                Name = currDirectory.Name,
                FullPath = currDirectory.FullName
            };
            var counts = new List<string>();
            if (subdirs.Length > 0)
                counts.Add(subdirs.Length + (subdirs.Length == 1 ? " folder" : " folders"));
            if (files.Length > 0)
                counts.Add(files.Length + (files.Length == 1 ? " file" : " files"));

            current.CountDisplay = string.Join(", ", counts);

            return Ok(new { directory = current, contents = dto, path = PathResolver.ToBrowserPath(directory) });
        }

        /// <summary>
        /// Returns the size of each subfolder in a directory, plus the directory's total size.
        /// Implemented as a separate endpoint to support lazy loading.
        /// </summary>
        [HttpGet("sizes")]
        public IActionResult GetSizes([FromQuery] string path, CancellationToken cancellationToken)
        {
            if (!PathResolver.TryResolveDirectory(path, out var directory))
                return NotFound();

            var stopwatch = Stopwatch.StartNew();
            var result = new DirectorySizes();

            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", ContentsOptions))
            {
                if (entry is FileInfo file)
                {
                    result.Size += file.Length;
                    continue;
                }

                var folder = new FolderSize { Name = entry.Name };
                try
                {
                    folder.Size = _directorySizes.GetSize((DirectoryInfo)entry, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // For example, the folder was deleted after the listing was read. Report the rest.
                    _logger.LogWarning(ex, "Could not calculate the size of {Folder}", entry.FullName);
                }

                if (folder.Size is long size)
                {
                    result.Size += size;
                    folder.DisplaySize = MiscHelpers.FormatDisplaySize(size);
                }
                result.Folders.Add(folder);
            }

            result.DisplaySize = MiscHelpers.FormatDisplaySize(result.Size);
            _logger.LogDebug("Calculated folder sizes for {Directory} in {ElapsedMs} ms", directory, stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }

        /// <summary>
        /// Pastes the clipboard into a folder. Returns 200 with a per-item result, even when some or all items fail.
        /// </summary>
        [HttpPost("paste")]
        public IActionResult Paste([FromBody] ClipboardAction action, [FromQuery] bool overwrite = false)
        {
            if (!PathResolver.TryResolveDirectory(action.Path, out var destination))
                return NotFound();
            if (action.Copy.Count + action.Cut.Count == 0)
                return Problem("The clipboard is empty.", statusCode: StatusCodes.Status400BadRequest);

            return RunAction(() => _fileActions.Paste(destination, action.Copy, action.Cut, overwrite));
        }

        /// <summary>
        /// Renames items in a folder. Batch renaming gives items numbered names.
        /// </summary>
        [HttpPost("rename")]
        public IActionResult Rename([FromBody] RenameAction action)
        {
            if (!PathResolver.TryResolveDirectory(action.Path, out var directory))
                return NotFound();
            if (action.Names.Count == 0)
                return Problem("No items were selected.", statusCode: StatusCodes.Status400BadRequest);
            if (!MiscHelpers.IsValidFileName(action.NewName))
                return Problem($"\"{action.NewName}\" isn't a valid name. Names can't be \".\" or \"..\", end with a dot or space, or contain any of these characters: \\ / : * ? \" < > |",
                    statusCode: StatusCodes.Status400BadRequest);

            return RunAction(() => _fileActions.Rename(directory, action.Names, action.NewName));
        }

        /// <summary>
        /// Deletes items in a folder, including everything inside folders.
        /// </summary>
        [HttpPost("delete")]
        public IActionResult Delete([FromBody] DeleteAction action)
        {
            if (!PathResolver.TryResolveDirectory(action.Path, out var directory))
                return NotFound();
            if (action.Names.Count == 0)
                return Problem("No items were selected.", statusCode: StatusCodes.Status400BadRequest);

            return RunAction(() => _fileActions.Delete(directory, action.Names));
        }

        /// <summary>
        /// Downloads items from a folder. One file = direct download, folder or multi-item = .zip
        /// </summary>
        [HttpGet("download")]
        public IActionResult Download([FromQuery] string path, [FromQuery] List<string> names)
        {
            if (!PathResolver.TryResolveDirectory(path, out var directory))
                return NotFound();
            if (names.Count == 0)
                return Problem("No items were selected.", statusCode: StatusCodes.Status400BadRequest);

            var items = new List<string>();
            foreach (var name in names.Distinct(StringComparer.FromComparison(PathResolver.Comparison)))
            {
                var item = Path.Combine(directory, name ?? string.Empty);
                if (!MiscHelpers.IsValidFileName(name ?? string.Empty) || PathResolver.IsLink(item) || !(System.IO.File.Exists(item) || Directory.Exists(item)))
                    return Problem($"\"{name}\" can't be downloaded. It may have been moved or deleted, or it's a link.", statusCode: StatusCodes.Status404NotFound);
                items.Add(item);
            }

            if (items.Count == 1 && System.IO.File.Exists(items[0]))
            {
                // Always an attachment of no particular type, so the browser saves it rather than displaying it
                Response.Headers.XContentTypeOptions = "nosniff";
                return PhysicalFile(items[0], "application/octet-stream", Path.GetFileName(items[0]), enableRangeProcessing: true);
            }

            var zipName = items.Count == 1 ? Path.GetFileName(items[0])
                : PathResolver.IsRoot(directory) ? "Download"
                : Path.GetFileName(directory);
            return new ZipDownloadResult(items, zipName + ".zip");
        }

        /// <summary>
        /// Uploads files into a folder
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(FileActionService.MaxUploadRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileActionService.MaxUploadRequestBytes, ValueCountLimit = FileActionService.MaxUploadFiles)]
        public IActionResult Upload([FromQuery] string path, [FromForm] List<IFormFile> files, [FromQuery] bool overwrite = false)
        {
            if (!PathResolver.TryResolveDirectory(path, out var directory))
                return NotFound();
            if (files.Count == 0)
                return Problem("No files were chosen.", statusCode: StatusCodes.Status400BadRequest);
            if (files.Sum(file => file.Length) > FileActionService.MaxUploadBytes)
                return Problem($"Uploads are limited to {FileActionService.MaxUploadBytes / 1024 / 1024} MB at a time.", statusCode: StatusCodes.Status413PayloadTooLarge);

            return RunAction(() => _fileActions.Upload(directory, files, overwrite));
        }

        // Runs a file action. Its folder is checked again once it's the action's turn to run, and
        // a folder that changed in the meantime is reported rather than acted on.
        private IActionResult RunAction(Func<ItemActionResult> action)
        {
            try
            {
                return Ok(action());
            }
            catch (FolderChangedException ex)
            {
                return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        }

        /// <summary>
        /// Recursively finds files and folders whose names contain the term
        /// </summary>
        [HttpGet("search")]
        public IActionResult Search([FromQuery] string path, [FromQuery] string? term, CancellationToken cancellationToken)
        {
            if (!PathResolver.TryResolveDirectory(path, out var directory))
                return NotFound();

            var text = term?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return Problem("Enter something to search for.", statusCode: StatusCodes.Status400BadRequest);

            var matches = new FileSystemEnumerable<SearchResult>(directory, (ref FileSystemEntry entry) => ToSearchResult(ref entry), SearchOptions)
            {
                // Checks the name in place, without allocating anything for the names that don't match
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    cancellationToken.ThrowIfCancellationRequested(); // the user searched again or left
                    return entry.FileName.Contains(text, StringComparison.OrdinalIgnoreCase);
                },
                // Ignore symlinks & junctions
                ShouldRecursePredicate = (ref FileSystemEntry entry) => !entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
            };

            // One more than the limit, to tell whether there were more
            var results = matches.Take(MaxSearchResults + 1).ToList();
            var truncated = results.Count > MaxSearchResults;
            if (truncated)
                results.RemoveAt(MaxSearchResults);

            return Ok(new SearchResults { Results = results, Truncated = truncated });
        }

        private static SearchResult ToSearchResult(ref FileSystemEntry entry)
        {
            var name = entry.FileName.ToString();
            return new SearchResult
            {
                Name = name,
                Path = PathResolver.ToBrowserPath(entry.ToFullPath()),
                Type = entry.IsDirectory ? ServerObjectType.Directory : ServerObjectType.File,
                Extension = entry.IsDirectory ? string.Empty : MiscHelpers.GetExtensionLabel(name),
                DisplaySize = entry.IsDirectory ? string.Empty : MiscHelpers.FormatDisplaySize(entry.Length),
                LastWriteTimeUtc = entry.LastWriteTimeUtc.UtcDateTime,
                IsLink = entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
            };
        }
    }
}