namespace TestProject.Models
{
    /// <summary>
    /// A file or folder found by a search. Its path is a browser path, like the one in the address bar.
    /// </summary>
    public class SearchResult
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public ServerObjectType Type { get; set; }
        public string Extension { get; set; } = string.Empty;
        public string DisplaySize { get; set; } = string.Empty;
        public DateTime LastWriteTimeUtc { get; set; }

        /// <summary>True for a junction or symbolic link, which is listed but never searched inside.</summary>
        public bool IsLink { get; set; }
    }

    /// <summary>
    /// The matches for one search, up to a limit
    /// </summary>
    public class SearchResults
    {
        public List<SearchResult> Results { get; set; } = new();

        /// <summary>True when there were more matches than were returned.</summary>
        public bool Truncated { get; set; }
    }
}
