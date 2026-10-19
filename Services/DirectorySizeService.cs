using System.IO.Enumeration;
using Microsoft.Extensions.Caching.Memory;

namespace TestProject.Services
{
    public class DirectorySizeService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        private static readonly EnumerationOptions WalkOptions = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,       // the default skips Hidden and System files
            IgnoreInaccessible = true,  // skip folders the app can't read instead of failing
        };

        private readonly IMemoryCache _cache;

        public DirectorySizeService(IMemoryCache cache)
        {
            _cache = cache;
        }

        /// <summary>
        /// Returns the total size of the files under <paramref name="folder"/>, or null when the
        /// folder is a link, which isn't followed (see <see cref="IsLink"/>).
        /// </summary>
        public long? GetSize(DirectoryInfo folder, CancellationToken cancellationToken = default)
        {
            if (IsLink(folder.Attributes))
                return null;

            var cacheKey = "directory-size:" + folder.FullName;
            if (_cache.TryGetValue(cacheKey, out long size))
                return size;

            size = CalculateSize(folder.FullName, cancellationToken);
            _cache.Set(cacheKey, size, CacheDuration);
            return size;
        }

        private static long CalculateSize(string path, CancellationToken cancellationToken)
        {
            // Each file's size arrives in the same system call that lists its folder. Reading it from
            // the raw entry avoids creating a FileInfo or a path string for every file.
            var fileSizes = new FileSystemEnumerable<long>(path, (ref FileSystemEntry entry) => entry.Length, WalkOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
                ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsLink(entry.Attributes),
            };

            long total = 0;
            foreach (long size in fileSizes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += size;
            }
            return total;
        }

        /// <summary>
        /// Junctions and symbolic links point at content stored somewhere else. Should be ignored
        /// to avoid inflated sums.
        /// </summary>
        private static bool IsLink(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) != 0;
    }
}
