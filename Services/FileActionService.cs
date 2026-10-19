using TestProject.Helpers;
using TestProject.Models;

namespace TestProject.Services
{
    /// <summary>
    /// Copies, moves, renames and deletes items under the browse root
    /// </summary>
    public class FileActionService
    {
        // Lists one folder, including hidden and system items
        private static readonly EnumerationOptions ContentsOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

        // File actions run one at a time to avoid race conditions
        private readonly object _actionLock = new();

        private readonly ILogger<FileActionService> _logger;
        private readonly DirectorySizeService _directorySizes;

        public FileActionService(ILogger<FileActionService> logger, DirectorySizeService directorySizes)
        {
            _logger = logger;
            _directorySizes = directorySizes;
        }

        /// <summary>
        /// Copies and moves items, given as browser paths
        /// </summary>
        public ItemActionResult Paste(string destination, IEnumerable<string> copy, IEnumerable<string> cut, bool overwrite) =>
            RunExclusively(() => PasteItems(destination, copy, cut, overwrite));

        /// <summary>
        /// Renames items in <paramref name="directory"/>
        /// </summary>
        public ItemActionResult Rename(string directory, IReadOnlyList<string> names, string newName) =>
            RunExclusively(() => RenameItems(directory, names, newName));

        /// <summary>
        /// Recursively deletes items in <paramref name="directory"/>
        /// </summary>
        public ItemActionResult Delete(string directory, IReadOnlyList<string> names) =>
            RunExclusively(() => DeleteItems(directory, names));

        // Runs one action at a time and drops cached folder sizes
        private ItemActionResult RunExclusively(Func<ItemActionResult> action)
        {
            lock (_actionLock)
            {
                try
                {
                    return action();
                }
                finally
                {
                    _directorySizes.Clear();
                }
            }
        }

        private ItemActionResult PasteItems(string destination, IEnumerable<string> copy, IEnumerable<string> cut, bool overwrite)
        {
            var result = new ItemActionResult();
            int copied = 0, moved = 0, linksSkipped = 0;

            var items = copy.Select(path => (Path: path, Move: false)).Concat(cut.Select(path => (Path: path, Move: true)));
            foreach (var (sourcePath, move) in items)
            {
                var name = NameOf(sourcePath);
                try
                {
                    var source = ResolvePasteSource(sourcePath, destination, move);
                    name = Path.GetFileName(source);

                    // A copy into its own folder becomes a duplicate
                    var isDuplicate = string.Equals(Path.GetDirectoryName(source), destination, PathResolver.Comparison);
                    var target = Path.Combine(destination,
                        isDuplicate ? GetFreeCopyName(destination, name, Directory.Exists(source)) : name);
                    if (!isDuplicate)
                        CheckPasteTarget(source, target, overwrite);

                    if (move)
                    {
                        Move(source, target, overwrite);
                        moved++;
                    }
                    else
                    {
                        linksSkipped += Copy(source, target, overwrite && !isDuplicate);
                        copied++;
                    }
                }
                catch (ItemActionException ex)
                {
                    result.Failed.Add(new ItemFailure { Name = name, Reason = ex.Message, Path = sourcePath });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not {Action} {Source} into {Destination}", move ? "move" : "copy", sourcePath, destination);
                    result.Failed.Add(new ItemFailure { Name = name, Reason = Describe(ex), Path = sourcePath });
                }
            }

            result.Message = JoinCounts((copied, "copied"), (moved, "moved"));
            if (linksSkipped > 0)
                result.Message += $" ({Count(linksSkipped, "link", "links")} inside {(linksSkipped == 1 ? "was" : "were")} skipped)";
            return result;
        }

        private ItemActionResult RenameItems(string directory, IReadOnlyList<string> names, string newName)
        {
            var result = new ItemActionResult();
            var renamed = 0;

            var items = names.Select(name => (Name: name ?? string.Empty, IsFolder: IsFolder(directory, name))).ToList();
            var newNames = GetNewNames(items, newName);

            for (var i = 0; i < items.Count; i++)
            {
                var (name, _) = items[i];
                var targetName = newNames[i];
                try
                {
                    var source = ResolveChild(directory, name);
                    if (!MiscHelpers.IsValidFileName(targetName))
                        throw new ItemActionException($"\"{targetName}\" isn't a valid name.");

                    if (name == targetName)
                    {
                        renamed++; // already has that name
                        continue;
                    }

                    // A change of letter case only is the same item on Windows, not a conflict
                    var target = Path.Combine(directory, targetName);
                    var caseOnly = string.Equals(name, targetName, PathResolver.Comparison);
                    if (!caseOnly && (Exists(target) || PathResolver.IsLink(target)))
                        throw new ItemActionException($"An item named \"{targetName}\" already exists.");

                    if (File.GetAttributes(source).HasFlag(FileAttributes.Directory))
                        Directory.Move(source, target);
                    else
                        File.Move(source, target);
                    renamed++;
                }
                catch (ItemActionException ex)
                {
                    result.Failed.Add(new ItemFailure { Name = name, Reason = ex.Message });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not rename {Name} in {Directory} to {NewName}", name, directory, targetName);
                    result.Failed.Add(new ItemFailure { Name = name, Reason = Describe(ex) });
                }
            }

            result.Message = JoinCounts((renamed, "renamed"));
            return result;
        }

        private ItemActionResult DeleteItems(string directory, IReadOnlyList<string> names)
        {
            var result = new ItemActionResult();
            var deleted = 0;

            foreach (var name in names.Select(name => name ?? string.Empty))
            {
                try
                {
                    var path = ResolveChild(directory, name);
                    var attributes = File.GetAttributes(path);
                    if (!attributes.HasFlag(FileAttributes.Directory))
                        File.Delete(path);
                    else if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        Directory.Delete(path); // removes the link itself
                    else
                        DeleteFolder(path);
                    deleted++;
                }
                catch (ItemActionException ex)
                {
                    result.Failed.Add(new ItemFailure { Name = name, Reason = ex.Message });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not delete {Name} in {Directory}", name, directory);
                    result.Failed.Add(new ItemFailure { Name = name, Reason = Describe(ex) });
                }
            }

            result.Message = JoinCounts((deleted, "deleted"));
            return result;
        }

        /// <summary>
        /// One item gets the new name exactly as typed, batched items get a numbered prefix
        /// </summary>
        public static List<string> GetNewNames(IReadOnlyList<(string Name, bool IsFolder)> items, string newName)
        {
            if (items.Count == 1)
                return new List<string> { newName };

            return items.Select((item, index) =>
            {
                var extension = item.IsFolder ? string.Empty : Path.GetExtension(item.Name);
                var suffix = newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? string.Empty : extension;
                return $"{index + 1} - {newName}{suffix}";
            }).ToList();
        }

        /// <summary>
        /// The name of the <paramref name="number"/>th duplicate of an item copied into its own
        /// folder
        /// </summary>
        public static string GetCopyName(string name, bool isFolder, int number)
        {
            var extension = isFolder ? string.Empty : Path.GetExtension(name);
            var baseName = name[..^extension.Length];
            if (baseName.Length == 0)
            {
                baseName = name;
                extension = string.Empty;
            }
            return $"{baseName} - Copy {number}{extension}";
        }

        // The first "- Copy N" name, counting from 1, that's free in the folder
        private static string GetFreeCopyName(string folder, string name, bool isFolder)
        {
            const int maxCopies = 1000;
            for (var number = 1; number <= maxCopies; number++)
            {
                var copyName = GetCopyName(name, isFolder, number);
                var path = Path.Combine(folder, copyName);
                if (!Exists(path) && !PathResolver.IsLink(path))
                    return copyName;
            }
            throw new ItemActionException($"There are already {maxCopies} copies of it in this folder.");
        }

        private static string ResolvePasteSource(string sourcePath, string destination, bool move)
        {
            if (!PathResolver.TryResolve(sourcePath, out var source))
                throw new ItemActionException("It isn't inside the folder this browser can reach.");
            if (PathResolver.IsRoot(source))
                throw new ItemActionException("The top-level folder can't be copied or moved.");

            var isLink = PathResolver.IsLink(source);
            if (!isLink && !Exists(source))
                throw new ItemActionException("It no longer exists.");
            if (move && string.Equals(Path.GetDirectoryName(source), destination, PathResolver.Comparison))
                throw new ItemActionException("It's already in this folder.");
            if (isLink && !move)
                throw new ItemActionException("Links can't be copied, because what they point to could be anywhere.");
            if (!isLink && Directory.Exists(source) && PathResolver.IsSameOrInside(destination, source))
                throw new ItemActionException("A folder can't be pasted into itself.");

            return source;
        }

        // Fails the item when its name is taken at the destination and can't be replaced
        private static void CheckPasteTarget(string source, string target, bool overwrite)
        {
            var name = Path.GetFileName(target);
            var targetIsLink = PathResolver.IsLink(target);
            var targetIsFolder = !targetIsLink && Directory.Exists(target);
            if (!targetIsLink && !targetIsFolder && !File.Exists(target))
                return; // the name is free

            if (!overwrite)
                throw new ItemActionException($"An item named \"{name}\" already exists here.");
            if (PathResolver.IsSameOrInside(source, target))
                throw new ItemActionException($"\"{name}\" here contains it, so it can't be merged into it.");
            if (targetIsLink)
                throw new ItemActionException($"\"{name}\" here is a link, which can't be replaced.");
            if (PathResolver.IsLink(source))
                throw new ItemActionException("A link can't replace an existing item.");

            var sourceIsFolder = Directory.Exists(source);
            if (sourceIsFolder != targetIsFolder)
                throw new ItemActionException(sourceIsFolder
                    ? $"A file named \"{name}\" already exists here."
                    : $"A folder named \"{name}\" already exists here.");
        }

        // Returns how many links inside a copied folder were skipped
        private static int Copy(string source, string target, bool overwrite)
        {
            if (!Directory.Exists(source))
            {
                File.Copy(source, target, overwrite);
                return 0;
            }

            Directory.CreateDirectory(target);
            var linksSkipped = 0;
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", ContentsOptions))
            {
                var entryTarget = Path.Combine(target, entry.Name);
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    linksSkipped++; // never follow a link: what it points to could be anywhere
                else if (PathResolver.IsLink(entryTarget))
                    throw new ItemActionException($"\"{entry.Name}\" in the existing folder is a link, which can't be replaced.");
                else
                    linksSkipped += Copy(entry.FullName, entryTarget, overwrite);
            }
            return linksSkipped;
        }

        private static void Move(string source, string target, bool overwrite)
        {
            if (!File.GetAttributes(source).HasFlag(FileAttributes.Directory))
                File.Move(source, target, overwrite);
            else if (overwrite && Directory.Exists(target))
                MergeFolder(source, target);
            else
                Directory.Move(source, target);
        }

        // Moves everything, then removes the emptied source folder
        private static void MergeFolder(string source, string target)
        {
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", ContentsOptions).ToList())
            {
                var entryTarget = Path.Combine(target, entry.Name);
                if (PathResolver.IsLink(entryTarget))
                    throw new ItemActionException($"\"{entry.Name}\" in the existing folder is a link, which can't be replaced.");

                var isFolder = entry is DirectoryInfo && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isFolder && Directory.Exists(entryTarget))
                    MergeFolder(entry.FullName, entryTarget);
                else if (entry is DirectoryInfo)
                    Directory.Move(entry.FullName, entryTarget);
                else
                    File.Move(entry.FullName, entryTarget, overwrite: true);
            }
            Directory.Delete(source);
        }

        // Deletes a folder and everything in it
        private static void DeleteFolder(string path)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", ContentsOptions).ToList())
            {
                if (entry is not DirectoryInfo)
                    File.Delete(entry.FullName);
                else if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(entry.FullName);
                else
                    DeleteFolder(entry.FullName);
            }
            Directory.Delete(path);
        }

        // Resolves a name inside an already-resolved folder
        private static string ResolveChild(string directory, string name)
        {
            if (!MiscHelpers.IsValidFileName(name))
                throw new ItemActionException("It isn't a valid name.");

            var path = Path.Combine(directory, name);
            if (!Exists(path) && !PathResolver.IsLink(path))
                throw new ItemActionException("It no longer exists.");
            return path;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static bool IsFolder(string directory, string? name) =>
            MiscHelpers.IsValidFileName(name ?? string.Empty) && Directory.Exists(Path.Combine(directory, name!));

        // The last segment of a path as the request gave it, for reporting an item that didn't resolve
        private static string NameOf(string? path)
        {
            var trimmed = (path ?? string.Empty).TrimEnd('/', '\\');
            return trimmed[(trimmed.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
        }

        private static string Describe(Exception ex) => ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => "It no longer exists.",
            PathTooLongException => "The resulting path would be too long.",
            UnauthorizedAccessException => "Access was denied. It, or something inside it, may be read-only.",
            _ => "It's in use or couldn't be accessed.",
        };

        private static string Count(int count, string singular, string plural) =>
            $"{count} {(count == 1 ? singular : plural)}";

        private static string JoinCounts(params (int Count, string Verb)[] parts) =>
            string.Join(", ", parts.Where(part => part.Count > 0).Select(part => $"{Count(part.Count, "item", "items")} {part.Verb}"));

        /// <summary>A failure the user can act on. Its message is shown as the item's reason.</summary>
        private sealed class ItemActionException : Exception
        {
            public ItemActionException(string reason) : base(reason) { }
        }
    }
}
