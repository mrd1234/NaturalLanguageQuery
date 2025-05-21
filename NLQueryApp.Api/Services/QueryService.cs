using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.LlmServices;

namespace NLQueryApp.Api.Services;

public class QueryService
{
    private readonly IDataSourceManager _dataSourceManager;
    private readonly LlmServiceFactory _llmServiceFactory;
    private readonly IConfiguration _configuration;

    public QueryService(
        IDataSourceManager dataSourceManager,
        LlmServiceFactory llmServiceFactory,
        IConfiguration configuration)
    {
        _dataSourceManager = dataSourceManager;
        _llmServiceFactory = llmServiceFactory;
        _configuration = configuration;
    }

    public async Task<QueryResult> ProcessNaturalLanguageQueryAsync(
    string dataSourceId, 
    string question, 
    string? llmServiceName = null)
{
    llmServiceName ??= _configuration["LlmSettings:DefaultService"] ?? "anthropic";
    var llmService = _llmServiceFactory.GetService(llmServiceName);
    
    // Get database schema and context for this data source
    var dataSource = await _dataSourceManager.GetDataSourceAsync(dataSourceId);
    var schema = await _dataSourceManager.GetSchemaAsync(dataSourceId);
    var schemaContext = await _dataSourceManager.GetSchemaContextAsync(dataSourceId);
    
    // Initial request
    var request = new LlmQueryRequest
    {
        UserQuestion = question,
        DatabaseSchema = schema,
        SchemaContext = schemaContext,
        DataSourceType = dataSource.Type
    };
    
    // Max retry attempts
    const int maxRetries = 3;
    for (var attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            // Generate SQL query
            var llmResponse = await llmService.GenerateSqlQueryAsync(request);
            
            if (string.IsNullOrWhiteSpace(llmResponse.SqlQuery))
            {
                return new QueryResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to generate SQL query: {llmResponse.Explanation}"
                };
            }
            
            // Execute SQL query
            var queryResult = await _dataSourceManager.ExecuteQueryAsync(dataSourceId, llmResponse.SqlQuery);
            queryResult.SqlQuery = llmResponse.SqlQuery;
            
            // If successful, return results
            if (queryResult.Success)
            {
                return queryResult;
            }
            
            // Check for relation doesn't exist error
            if (queryResult.ErrorMessage?.Contains("relation") == true && 
                queryResult.ErrorMessage.Contains("does not exist"))
            {
                // Try a fallback query to list available schemas and tables
                var fallbackQuery = @"
                    SELECT 
                        table_schema, 
                        table_name 
                    FROM 
                        information_schema.tables 
                    WHERE 
                        table_schema NOT IN ('pg_catalog', 'information_schema') 
                    ORDER BY 
                        table_schema, 
                        table_name";
                
                var fallbackResult = await _dataSourceManager.ExecuteQueryAsync(dataSourceId, fallbackQuery);
                if (fallbackResult.Success)
                {
                    fallbackResult.SqlQuery = fallbackQuery;
                    fallbackResult.ErrorMessage = "The requested tables don't exist. Here are the available tables:";
                    return fallbackResult;
                }
            }
            
            // If failed and we have more attempts, try again
            if (attempt < maxRetries - 1)
            {
                request.PreviousSqlQuery = llmResponse.SqlQuery;
                request.PreviousError = queryResult.ErrorMessage;
            }
            else
            {
                return queryResult;
            }
        }
        catch (Exception ex)
        {
            if (attempt >= maxRetries - 1)
            {
                return new QueryResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing query: {ex.Message}"
                };
            }
        }
    }
    
    // Should never reach here, but just in case
    return new QueryResult
    {
        Success = false,
        ErrorMessage = "Maximum retry attempts reached."
    };
}
}