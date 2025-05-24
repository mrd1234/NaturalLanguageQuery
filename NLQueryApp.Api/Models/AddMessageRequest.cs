namespace NLQueryApp.Api.Models;

public class AddMessageRequest
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? DataSourceId { get; set; }
    public string? SqlQuery { get; set; }
    public bool? QuerySuccess { get; set; }
}
