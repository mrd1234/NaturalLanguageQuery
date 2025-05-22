namespace NLQueryApp.Core.Models;

public interface IDataSourceManager
{
    /// <summary>
    /// Get all configured data sources
    /// </summary>
    Task<List<DataSourceDefinition>> GetDataSourcesAsync();
    
    /// <summary>
    /// Get a specific data source by ID
    /// </summary>
    Task<DataSourceDefinition> GetDataSourceAsync(string id);
    
    /// <summary>
    /// Create a new data source
    /// </summary>
    Task<DataSourceDefinition> CreateDataSourceAsync(DataSourceDefinition dataSource);
    
    /// <summary>
    /// Update an existing data source
    /// </summary>
    Task<DataSourceDefinition> UpdateDataSourceAsync(string id, DataSourceDefinition dataSource);
    
    /// <summary>
    /// Delete a data source
    /// </summary>
    Task<bool> DeleteDataSourceAsync(string id);
    
    /// <summary>
    /// Get the schema for a data source
    /// </summary>
    Task<string> GetSchemaAsync(string dataSourceId);
    
    /// <summary>
    /// Get database-specific dialect notes and query guidance for a data source
    /// </summary>
    Task<string> GetDialectNotesAsync(string dataSourceId);
    
    /// <summary>
    /// Get the schema context for a data source
    /// </summary>
    Task<string> GetSchemaContextAsync(string dataSourceId);
    
    /// <summary>
    /// Set the schema context for a data source
    /// </summary>
    Task<bool> SetSchemaContextAsync(string dataSourceId, string context);
    
    /// <summary>
    /// Execute a query against a data source
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(string dataSourceId, string query);
    
    /// <summary>
    /// Validate a query against a data source
    /// </summary>
    Task<ValidationResult> ValidateQueryAsync(string dataSourceId, string query);
    
    /// <summary>
    /// Test connection to a data source
    /// </summary>
    Task<bool> TestConnectionAsync(string dataSourceId);
    
    /// <summary>
    /// Get query execution plan for a data source
    /// </summary>
    Task<string?> GetQueryPlanAsync(string dataSourceId, string query);
    
    /// <summary>
    /// Get metadata for a data source
    /// </summary>
    Task<DataSourceMetadata> GetMetadataAsync(string dataSourceId);
    
    /// <summary>
    /// Get version for a data source, if available
    /// </summary>
    Task<DatabaseInfo?> GetDatabaseInfoAsync(string dataSourceId);
}