namespace TestProject.Helpers
{
    /// <summary>
    /// Maps relative paths from the browser to full paths under the browse root
    /// </summary>
    public static class PathResolver
    {
        public static string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory));

        public static StringComparison Comparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Resolves a browser path to a full path under the root, fails when the path is outside the
        /// root or goes through a link
        /// </summary>
        public static bool TryResolve(string? path, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string resolved;
            try
            {
                resolved = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.Combine(Root, path.TrimStart('/').TrimStart('\\'))));
            }
            catch (ArgumentException)
            {
                return false; // Catches unusable paths
            }

            if (!MiscHelpers.IsPathUnderAppRoot(resolved) || GoesThroughLink(resolved))
                return false;

            fullPath = resolved;
            return true;
        }

        public static string ToBrowserPath(string fullPath)
        {
            var relative = Path.GetRelativePath(Root, fullPath);
            return relative == "." ? "/" : "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Resolves a browser path to a folder that can be listed
        /// </summary>
        public static bool TryResolveDirectory(string? path, out string directory)
        {
            directory = string.Empty;
            if (!TryResolve(path, out var fullPath))
                return false;

            // Get data to serve direct file request
            if (File.Exists(fullPath))
                fullPath = Path.GetDirectoryName(fullPath)!;
            else if (!Directory.Exists(fullPath) || IsLink(fullPath))
                return false;

            directory = fullPath;
            return true;
        }

        /// <summary>
        /// True for a junction or symbolic link. Links can point anywhere, so they are never followed
        /// </summary>
        public static bool IsLink(string fullPath)
        {
            if (IsRoot(fullPath))
                return false;

            try
            {
                return File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return false; // doesn't exist
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true; // can't tell
            }
        }

        public static bool IsRoot(string fullPath) =>
            string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Root, Comparison);

        public static bool IsSameOrInside(string path, string folder) =>
            string.Equals(path, folder, Comparison)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, Comparison);

        // Checks each folder between the root and the path, not the path itself
        private static bool GoesThroughLink(string fullPath)
        {
            for (var folder = Path.GetDirectoryName(fullPath);
                 folder is not null && !IsRoot(folder) && MiscHelpers.IsPathUnderAppRoot(folder);
                 folder = Path.GetDirectoryName(folder))
            {
                if (IsLink(folder))
                    return true;
            }
            return false;
        }
    }
}
