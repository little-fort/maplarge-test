using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
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
        /// Implemented as a separate endpoint to support lazy loading.
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

        /// <summary>
        /// Pastes the contents of the clipboard into the target directory.
        /// </summary>
        [HttpPost("paste")]
        public async Task<IActionResult> Paste([FromBody] ClipboardAction action, bool overwrite = false)
        {
            if (!TryResolveDirectory(action.Path, out var directory))
                return NotFound();

            int copyCount = 0, cutCount = 0;
            List<string> failed = new List<string>();
            foreach (var item in action.Copy)
            {
                try
                {
                    var target = Path.Combine(directory, item.Name);
                    if (!MiscHelpers.IsPathUnderAppRoot(item.FullPath) || !MiscHelpers.IsPathUnderAppRoot(target))
                        throw new UnauthorizedAccessException("The source and target paths must be within the application directory.");

                    if (System.IO.File.Exists(item.FullPath) && System.IO.File.Exists(target))
                        System.IO.File.Copy(item.FullPath, target, overwrite);
                    else if (Directory.Exists(item.FullPath) && Directory.Exists(target))
                    {
                        // Create all target subdirectories upfront
                        foreach (string dirPath in Directory.GetDirectories(action.Path, "*", SearchOption.AllDirectories))
                            Directory.CreateDirectory(dirPath.Replace(action.Path, target));

                        // Copy all files, overwriting existing ones
                        foreach (string newPath in Directory.GetFiles(action.Path, "*.*", SearchOption.AllDirectories))
                            System.IO.File.Copy(newPath, newPath.Replace(action.Path, target), overwrite);
                    }
                    else
                        throw new InvalidOperationException("The operation's source and destination are mismatched.");
                    
                    copyCount++;
                }
                catch (Exception ex)
                {
                    failed.Add(item.Name);
                    _logger.LogError(ex.Message);
                }
            }

            foreach (var item in action.Cut)
            {
                try
                {
                    var target = Path.Combine(directory, item.Name);
                    if (!MiscHelpers.IsPathUnderAppRoot(action.Path) || !MiscHelpers.IsPathUnderAppRoot(target))
                        throw new UnauthorizedAccessException("The source and target paths must be within the application directory.");

                    if (System.IO.File.Exists(item.FullPath) && System.IO.File.Exists(target))
                        System.IO.File.Move(item.FullPath, target, overwrite);
                    else if (Directory.Exists(item.FullPath) && Directory.Exists(target))
                    {
                        // Create all target subdirectories upfront
                        foreach (string dirPath in Directory.GetDirectories(action.Path, "*", SearchOption.AllDirectories))
                            Directory.CreateDirectory(dirPath.Replace(action.Path, target));

                        // Move all files, overwriting existing ones
                        foreach (string newPath in Directory.GetFiles(action.Path, "*.*", SearchOption.AllDirectories))
                            System.IO.File.Move(newPath, newPath.Replace(action.Path, target), overwrite);

                        // Delete the old directory
                        Directory.Delete(action.Path, true);
                    }
                    else
                        throw new InvalidOperationException("The operation's source and destination are mismatched.");

                    cutCount++;
                }
                catch (Exception ex)
                {
                    failed.Add(item.Name);
                    _logger.LogError(ex.Message);
                }
            }

            var counts = new List<string>();
            if (copyCount > 0)
                counts.Add(copyCount + (copyCount == 1 ? " item" : " items") + " copied");
            if (cutCount > 0)
                counts.Add(cutCount + (cutCount == 1 ? " item" : " items") + " cut");

            var message = string.Join(", ", counts);

            if (failed.Count > 0)
                return StatusCode(StatusCodes.Status500InternalServerError, new { status = "FAILED", message, failed });

            return Ok(new { status = "OK", message });
        }

        /// <summary>
        /// Renames the specified file
        /// </summary>
        [HttpPost("rename")]
        public async Task<IActionResult> Rename([FromBody] RenameAction action)
        {
            if (!TryResolveDirectory(action.Path, out var directory))
                return NotFound();

            try
            {
                var target = Path.Combine(directory, action.Name);
                if (!MiscHelpers.IsPathUnderAppRoot(action.Path) || !MiscHelpers.IsPathUnderAppRoot(target))
                    return Forbid("The source and target paths must be within the application directory.");

                if (System.IO.File.Exists(action.Path) && System.IO.File.Exists(target))
                    System.IO.File.Move(action.Path, target);
                else if (Directory.Exists(action.Path) && Directory.Exists(target))
                {
                    // Create all target subdirectories upfront
                    foreach (string dirPath in Directory.GetDirectories(action.Path, "*", SearchOption.AllDirectories))
                        Directory.CreateDirectory(dirPath.Replace(action.Path, target));

                    // Move all files, overwriting existing ones
                    foreach (string newPath in Directory.GetFiles(action.Path, "*.*", SearchOption.AllDirectories))
                        System.IO.File.Move(newPath, newPath.Replace(action.Path, target));

                    // Delete the old directory
                    Directory.Delete(action.Path, true);
                }
                else
                    throw new InvalidOperationException("The operation's source and destination are mismatched.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return Problem(ex.Message);
            }

            return NoContent();
        }

        /// <summary>
        /// Deletes the specified file
        /// </summary>
        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] string path)
        {
            if (!TryResolveDirectory(path, out var directory))
                return NotFound();

            try
            {
                if (!MiscHelpers.IsPathUnderAppRoot(path))
                    return Forbid("The target path must be within the application directory.");

                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    return NotFound("The supplied path does not exist.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return Problem(ex.Message);
            }

            return Ok();
        }
    }
}