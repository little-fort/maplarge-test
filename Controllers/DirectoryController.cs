using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestProject.Helpers;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers 
{
    [ApiController]
    [Route("[controller]")]
    public class DirectoryController : ControllerBase 
    {

        // Lists a folder's own files and subfolders, including hidden and system ones, matching what
        // Directory.GetDirectories and Directory.GetFiles return for the listing in Get.
        private static readonly EnumerationOptions ContentsOptions = new() { AttributesToSkip = 0 };

        private readonly ILogger<DirectoryController> _logger;
        private readonly DirectorySizeService _directorySizes;

        public DirectoryController(ILogger<DirectoryController> logger, DirectorySizeService directorySizes)
        {
            _logger = logger;
            _directorySizes = directorySizes;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string path) 
        {
            if (!TryResolveDirectory(path, out var directory))
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
                    Type = ServerObjectType.Directory
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
                    Extension = info.Name.Contains('.') ? info.Name.Split('.').ToList().Last().ToUpper() : "FILE",
                    Type = ServerObjectType.File
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

            return Ok(new { directory = current, contents = dto });
        }

        /// <summary>
        /// Returns the size of each subfolder in a directory, plus the directory's total size.
        /// This is separate from <see cref="Get"/> because it walks every subfolder, which can take
        /// seconds for a large tree; the page shows the listing first and fills these in after.
        /// </summary>
        [HttpGet("sizes")]
        public IActionResult GetSizes([FromQuery] string path, CancellationToken cancellationToken)
        {
            if (!TryResolveDirectory(path, out var directory))
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
        /// Maps a path from the browser (for example "/Controllers") to a folder under the app root.
        /// A path to a file resolves to the folder that contains it. Returns false when the path is
        /// outside the root or doesn't exist.
        /// </summary>
        private static bool TryResolveDirectory(string path, out string directory)
        {
            var app = Environment.CurrentDirectory;
            var fullPath = Path.GetFullPath(Path.Combine(app, path.TrimStart('/').TrimStart('\\')));
            directory = fullPath;

            // Validate path
            if (!MiscHelpers.IsPathUnderAppRoot(fullPath))
                return false;

            if (Directory.Exists(fullPath))
                return true;

            // Get data to serve direct file request
            if (System.IO.File.Exists(fullPath))
            {
                // TODO: Infer path for directory browser and render file preview
                directory = Path.GetDirectoryName(fullPath)!; // only null for a drive root, which isn't a file
                return true;
            }

            return false;
        }
    }
}