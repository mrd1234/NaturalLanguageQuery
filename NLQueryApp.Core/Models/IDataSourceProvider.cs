using NLQueryApp.Core;

namespace NLQueryApp.Core.Models;

public interface IDataSourceProvider
{
    string ProviderType { get; }
    
    /// <summary>
    /// Get the database schema for this data source
    /// </summary>
    Task<string> GetSchemaAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get the database version, if possible, for this data source
    /// </summary>
    Task<DatabaseInfo?> GetDatabaseInfoAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get database-specific dialect notes and query guidance
    /// </summary>
    Task<string> GetDialectNotesAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Execute a query against this data source
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query);
    
    /// <summary>
    /// Validate a query without executing it
    /// </summary>
    Task<ValidationResult> ValidateQueryAsync(DataSourceDefinition dataSource, string query);
    
    /// <summary>
    /// Test the connection to this data source
    /// </summary>
    Task<bool> TestConnectionAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get query execution plan (optional, for performance analysis)
    /// </summary>
    Task<string?> GetQueryPlanAsync(DataSourceDefinition dataSource, string query);
    
    /// <summary>
    /// Get database-specific metadata
    /// </summary>
    Task<DataSourceMetadata> GetMetadataAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Generate a contextual title for a query using schema awareness
    /// </summary>
    Task<string> GenerateTitleAsync(DataSourceDefinition dataSource, string userQuestion, ILlmService? llmService = null);
    
    /// <summary>
    /// Get example queries specific to this data source
    /// </summary>
    Task<List<QueryExample>> GetQueryExamplesAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get entity descriptions for better context understanding
    /// </summary>
    Task<Dictionary<string, string>> GetEntityDescriptionsAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Setup or migrate the schema for this data source (if applicable)
    /// </summary>
    Task<bool> SetupSchemaAsync(DataSourceDefinition dataSource, bool dropIfExists = false);
    
    /// <summary>
    /// Get datasource-specific prompt enhancements for LLM queries
    /// </summary>
    Task<string> GetPromptEnhancementsAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get the query language name used by this data source (e.g., "PostgreSQL", "MySQL", "T-SQL")
    /// </summary>
    Task<string> GetQueryLanguageAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Get the display name for the query language (for UI purposes)
    /// </summary>
    Task<string> GetQueryLanguageDisplayAsync(DataSourceDefinition dataSource);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class DataSourceMetadata
{
    public string DatabaseType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> AvailableSchemas { get; set; } = new();
    public Dictionary<string, object> AdditionalInfo { get; set; } = new();
}

public class QueryExample
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NaturalLanguageQuery { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}