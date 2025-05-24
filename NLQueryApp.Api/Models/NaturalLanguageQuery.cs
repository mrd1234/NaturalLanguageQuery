namespace NLQueryApp.Api.Models;

public class NaturalLanguageQuery
{
    public string Question { get; set; } = string.Empty;
    public string? LlmService { get; set; }
    public string DataSourceId { get; set; } = string.Empty;
    public int? ConversationId { get; set; }
}
