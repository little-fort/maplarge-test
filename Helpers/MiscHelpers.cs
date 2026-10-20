using System.Text.RegularExpressions;

namespace TestProject.Helpers
{
    public class MiscHelpers
    {
        // Restricted Windows device names (CON, NUL, COM1...)
        private static readonly Regex ReservedNames = new(
            @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])\s*(\..*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const decimal _KB = 1024;
        private const decimal _MB = _KB * 1024;
        private const decimal _GB = _MB * 1024;
        private const decimal _TB = _GB * 1024;
        private const decimal _PB = _TB * 1024;

        public static string FormatDisplaySize(long size)
        {
            if (size < 100)
                return "0 KB";
            if (size < 1000)
                return "1 KB";
            if (size < 1_000_000)
                return $"{Math.Round(size / _KB)} KB";
            if (size < 1_000_000_000)
                return $"{Math.Round(size / _MB)} MB";
            if (size < 1_000_000_000_000)
                return $"{Math.Round(size / _GB)} GB";
            if (size < 1_000_000_000_000_000)
                return $"{Math.Round(size / _TB)} TB";
            
            return $"{Math.Round(size / _PB)} PB";
        }
        
        public static bool IsPathUnderAppRoot(string path)
        {
            var appDir = PathResolver.Root;
            var rootDir = Path.EndsInDirectorySeparator(appDir) ? appDir : appDir + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!path.Equals(appDir, comparison) && !path.StartsWith(rootDir, comparison))
                return false;

            return true;
        }

        /// <summary>
        /// True for a single item name (not a path)
        /// </summary>
        public static bool IsValidFileName(string name) =>
            !string.IsNullOrWhiteSpace(name)
            && name == Path.GetFileName(name)
            && name is not "." and not ".."
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !(OperatingSystem.IsWindows() && (name.EndsWith('.') || name.EndsWith(' '))) // Windows silently drops a trailing dot or space, so "a." would become "a"
            && !(OperatingSystem.IsWindows() && ReservedNames.IsMatch(name));
    }
}
