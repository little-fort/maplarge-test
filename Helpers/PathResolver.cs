namespace TestProject.Helpers
{
    /// <summary>
    /// Maps relative paths from the browser to full paths under the browse root
    /// </summary>
    public static class PathResolver
    {
        private static string? _root;

        /// <summary>
        /// The folder the browser can reach, set once at startup by <see cref="Configure"/>
        /// </summary>
        public static string Root => _root ?? throw new InvalidOperationException("PathResolver.Configure hasn't been called.");

        /// <summary>
        /// The browse root used when RootFolder isn't set
        /// </summary>
        public const string DefaultRootFolder = "Files";

        /// <summary>
        /// Sets the browse root to the RootFolder setting, a folder inside the app's binaries folder,
        /// and creates it if it doesn't exist yet
        /// </summary>
        public static void Configure(string? rootFolder, string? webRoot)
        {
            var appFolder = FullPath(AppContext.BaseDirectory);
            var root = FullPath(Path.Combine(appFolder, string.IsNullOrWhiteSpace(rootFolder) ? DefaultRootFolder : rootFolder));

            if (!IsSameOrInside(root, appFolder) || string.Equals(root, appFolder, Comparison))
                throw new InvalidOperationException($"RootFolder \"{rootFolder}\" would be \"{root}\", which isn't a folder inside the app's folder \"{appFolder}\".");

            // The binaries folder has its own wwwroot once published, so refuse that name in development too
            foreach (var siteFolder in new[] { webRoot, Path.Combine(appFolder, "wwwroot") })
            {
                if (siteFolder is null)
                    continue;

                var site = FullPath(siteFolder);
                if (IsSameOrInside(root, site) || IsSameOrInside(site, root))
                    throw new InvalidOperationException($"RootFolder \"{rootFolder}\" would be \"{root}\", which overlaps \"{site}\", the folder this site is served from. Uploads there would become part of the site.");
            }

            // A link between the binaries folder and the root could point the root anywhere, so the
            // root can't be one or sit under one. Folders that don't exist yet are created as plain folders.
            for (var folder = root; !string.Equals(folder, appFolder, Comparison); folder = Path.GetDirectoryName(folder)!)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(folder);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    continue; // doesn't exist yet
                }

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidOperationException($"RootFolder \"{rootFolder}\" would be \"{root}\", which goes through the link \"{folder}\". A link can point anywhere, so the root can't be one or sit under one.");
            }

            Directory.CreateDirectory(root);
            _root = root;
        }

        private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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
        /// True when a full path is under the root, doesn't go through a link and isn't a link itself.
        /// For checking a path that was resolved earlier again, right before it's used.
        /// </summary>
        public static bool IsReachable(string fullPath) =>
            MiscHelpers.IsPathUnderAppRoot(fullPath) && !GoesThroughLink(fullPath) && !IsLink(fullPath);

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
