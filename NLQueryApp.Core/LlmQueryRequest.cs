namespace NLQueryApp.Core;

public class LlmQueryRequest
{
    public string UserQuestion { get; set; } = string.Empty;
    public string DatabaseSchema { get; set; } = string.Empty;
    public string SchemaContext { get; set; } = string.Empty;
    public string DialectNotes { get; set; } = string.Empty;
    public string QueryLanguage { get; set; } = "SQL";
    
    // Deprecated - remove after migration
    [Obsolete("Use QueryLanguage instead")]
    public string DataSourceType 
    { 
        get => QueryLanguage;
        set => QueryLanguage = value;
    }
    
    public string? PreviousSqlQuery { get; set; }
    public string? PreviousError { get; set; }
    public string? ConversationContext { get; set; }
    public ModelType ModelType { get; set; } = ModelType.Query;
}