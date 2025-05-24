namespace NLQueryApp.Core.Models;

public class DataSourceTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, string> DefaultConnectionParameters { get; set; } = new();
    public string DefaultSchemaContext { get; set; } = string.Empty;
    public string? SchemaContextFile { get; set; }
    public bool Optional { get; set; } = true;
}
