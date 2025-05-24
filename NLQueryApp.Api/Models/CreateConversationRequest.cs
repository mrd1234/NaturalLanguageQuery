namespace NLQueryApp.Api.Models;

public class CreateConversationRequest
{
    public string Title { get; set; } = "New Conversation";
    public string? DataSourceId { get; set; }
}
