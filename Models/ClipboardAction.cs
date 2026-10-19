namespace TestProject.Models
{
    public class ClipboardAction
    {
        public string Path { get; set; } = string.Empty;
        public List<ServerObject> Copy { get; set; } = new List<ServerObject>();
        public List<ServerObject> Cut { get; set; } = new List<ServerObject>();
    }
}
