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