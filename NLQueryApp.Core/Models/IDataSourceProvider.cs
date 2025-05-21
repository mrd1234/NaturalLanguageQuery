namespace NLQueryApp.Core.Models;

public interface IDataSourceProvider
{
    string ProviderType { get; }
    Task<string> GetSchemaAsync(DataSourceDefinition dataSource);
    Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query);
    Task<bool> TestConnectionAsync(DataSourceDefinition dataSource);
}