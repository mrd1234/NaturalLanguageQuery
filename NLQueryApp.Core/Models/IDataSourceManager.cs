namespace NLQueryApp.Core.Models;

public interface IDataSourceManager
{
    Task<List<DataSourceDefinition>> GetDataSourcesAsync();
    Task<DataSourceDefinition> GetDataSourceAsync(string id);
    Task<DataSourceDefinition> CreateDataSourceAsync(DataSourceDefinition dataSource);
    Task<DataSourceDefinition> UpdateDataSourceAsync(string id, DataSourceDefinition dataSource);
    Task<bool> DeleteDataSourceAsync(string id);
    Task<string> GetSchemaAsync(string dataSourceId);
    Task<string> GetSchemaContextAsync(string dataSourceId);
    Task<bool> SetSchemaContextAsync(string dataSourceId, string context);
    Task<QueryResult> ExecuteQueryAsync(string dataSourceId, string query);
    Task<bool> TestConnectionAsync(string dataSourceId);
}