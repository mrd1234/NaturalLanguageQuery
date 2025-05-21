namespace NLQueryApp.Core.Models;

public class DataSourceContext
{
    public string DataSourceId { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}