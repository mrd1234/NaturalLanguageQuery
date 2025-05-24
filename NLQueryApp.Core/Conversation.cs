namespace NLQueryApp.Core;

public class Conversation
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DataSourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = new();
}
