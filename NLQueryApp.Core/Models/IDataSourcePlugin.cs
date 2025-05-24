namespace NLQueryApp.Core.Models;

/// <summary>
/// Interface for data source plugins that provide specialized functionality
/// for specific types of data sources
/// </summary>
public interface IDataSourcePlugin
{
    /// <summary>
    /// Name of the plugin
    /// </summary>
    string PluginName { get; }
    
    /// <summary>
    /// Data source type this plugin handles
    /// </summary>
    string DataSourceType { get; }
    
    /// <summary>
    /// Initialize schema for this data source type
    /// </summary>
    Task InitializeSchemaAsync(string connectionString);
    
    /// <summary>
    /// Import data specific to this data source type
    /// </summary>
    Task ImportDataAsync(string connectionString, string dataPath);
    
    /// <summary>
    /// Get default schema context for this data source type
    /// </summary>
    string GetDefaultSchemaContext();
    
    /// <summary>
    /// Get example queries for this data source type
    /// </summary>
    Task<List<QueryExample>> GetQueryExamplesAsync();
}
