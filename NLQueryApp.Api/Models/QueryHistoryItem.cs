namespace NLQueryApp.Api.Models;

public class QueryHistoryItem
{
    public int Index { get; set; }
    public string Question { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? DataSourceId { get; set; }
    public string? SqlQuery { get; set; }
    public bool? Success { get; set; }
}
