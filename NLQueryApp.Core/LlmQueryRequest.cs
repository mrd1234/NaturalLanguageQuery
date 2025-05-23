namespace NLQueryApp.Core;

public class LlmQueryRequest
{
    public string UserQuestion { get; set; } = string.Empty;
    public string DatabaseSchema { get; set; } = string.Empty;
    public string SchemaContext { get; set; } = string.Empty;
    public string DialectNotes { get; set; } = string.Empty;
    public string DataSourceType { get; set; } = "postgres";
    public string? PreviousSqlQuery { get; set; }
    public string? PreviousError { get; set; }
    public ModelType ModelType { get; set; } = ModelType.Query;
}