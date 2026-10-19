using System.Text.Json.Serialization;

namespace TestProject.Models
{
    public class ItemActionResult
    {
        public string Message { get; set; } = string.Empty;
        public List<ItemFailure> Failed { get; set; } = new List<ItemFailure>();
    }

    public class ItemFailure
    {
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }
    }
}
