namespace TestProject.Models
{
    public class RenameAction
    {
        public string Path { get; set; } = string.Empty;
        public List<string> Names { get; set; } = new List<string>();
        public string NewName { get; set; } = string.Empty;
    }
}
