namespace TestProject.Models
{
    public class ServerObject
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime LastWriteTimeUtc { get; set; }
        public long Size { get; set; }
        public string DisplaySize {  get; set; } = string.Empty;
        public ServerObjectType Type { get; set; }
        public string Extension { get; set; } = string.Empty;
        public string CountDisplay { get; set; } = string.Empty;

        /// <summary>True for a junction or symbolic link, which can't be opened or copied.</summary>
        public bool IsLink { get; set; }
    }

    public enum ServerObjectType
    {
        Directory = 0,
        File = 1
    }
}
