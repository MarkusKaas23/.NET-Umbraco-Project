namespace MyCustomUmbracoProject.Models
{
    public class ChatSessionSummary
    {
        public string SessionId { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime LastMessageAt { get; set; }
    }
}
