namespace TestProject.Models
{
    public class ClipboardAction
    {
        public string Path { get; set; } = string.Empty;
        public List<string> Copy { get; set; } = new List<string>();
        public List<string> Cut { get; set; } = new List<string>();
    }
}
