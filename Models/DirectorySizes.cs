namespace TestProject.Models
{
    public class DirectorySizes
    {
        /// <summary>Total size of the directory: its own files plus every subfolder.</summary>
        public long Size { get; set; }
        public string DisplaySize { get; set; } = string.Empty;
        public List<FolderSize> Folders { get; set; } = new();
    }

    public class FolderSize
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Null when the size isn't available: the folder is a link such as a junction or symbolic
        /// link (links aren't followed), or it couldn't be read.
        /// </summary>
        public long? Size { get; set; }
        public string DisplaySize { get; set; } = string.Empty;
    }
}
