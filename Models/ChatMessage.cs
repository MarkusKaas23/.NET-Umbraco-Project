using NPoco;

namespace MyCustomUmbracoProject.Models
{
    [TableName("ChatMessages")]
    [PrimaryKey("Id", AutoIncrement = true)]
    public class ChatMessage
    {
        public int Id { get; set; }
        public string SessionId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserPrompt { get; set; } = "";
        public string ResponseMarkdown { get; set; } = "";
        public string AiModel { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
