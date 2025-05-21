namespace NLQueryApp.Core;

public class ChatMessage
{
    public int Id { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ConversationId { get; set; }
}