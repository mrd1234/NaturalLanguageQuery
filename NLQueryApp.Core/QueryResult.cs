namespace NLQueryApp.Core;

public class QueryResult
{
    public string SqlQuery { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<Dictionary<string, object>>? Data { get; set; }
}