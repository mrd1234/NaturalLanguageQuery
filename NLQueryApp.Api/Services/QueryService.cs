using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.LlmServices;

namespace NLQueryApp.Api.Services;

public class QueryService
{
    private readonly IDataSourceManager _dataSourceManager;
    private readonly LlmServiceFactory _llmServiceFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        IDataSourceManager dataSourceManager,
        LlmServiceFactory llmServiceFactory,
        IConfiguration configuration,
        ILogger<QueryService> logger)
    {
        _logger = logger;
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
    
        // Get database schema, context, and dialect notes
        var dataSource = await _dataSourceManager.GetDataSourceAsync(dataSourceId);
        var schema = await _dataSourceManager.GetSchemaAsync(dataSourceId);
        var schemaContext = await _dataSourceManager.GetSchemaContextAsync(dataSourceId);
        var dialectNotes = await _dataSourceManager.GetDialectNotesAsync(dataSourceId);
    
        // Create request with dialect notes
        var request = new LlmQueryRequest
        {
            UserQuestion = question,
            DatabaseSchema = schema,
            SchemaContext = schemaContext,
            DialectNotes = dialectNotes,
            DataSourceType = dataSource.Type
        };
    
    // Max retry attempts
    const int maxRetries = 3;
    var baseDelayMs = 1000; // Start with 1 second delay
    
    for (var attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            // Add exponential backoff delay for retries (except first attempt)
            if (attempt > 0)
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1); // 1s, 2s, 4s
                _logger.LogInformation($"Retry attempt {attempt + 1}, waiting {delay}ms before calling LLM service");
                await Task.Delay(delay);
            }
            
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
                _logger.LogInformation($"Query succeeded on attempt {attempt + 1}");
                return queryResult;
            }
            
            // Log the failure for debugging
            _logger.LogWarning($"Query failed on attempt {attempt + 1}: {queryResult.ErrorMessage}");
            
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
                _logger.LogInformation($"Preparing retry {attempt + 2} with previous error: {queryResult.ErrorMessage}");
            }
            else
            {
                _logger.LogError($"All {maxRetries} attempts failed. Final error: {queryResult.ErrorMessage}");
                return queryResult;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            // Handle rate limiting specifically
            _logger.LogWarning($"Rate limited on attempt {attempt + 1}: {ex.Message}");
            
            if (attempt < maxRetries - 1)
            {
                // Longer delay for rate limiting
                var rateLimitDelay = baseDelayMs * (int)Math.Pow(3, attempt + 1); // 3s, 9s, 27s
                _logger.LogInformation($"Rate limited, waiting {rateLimitDelay}ms before retry");
                await Task.Delay(rateLimitDelay);
                continue; // Don't update request with previous query/error for rate limit retries
            }
            
            return new QueryResult
            {
                Success = false,
                ErrorMessage = $"Rate limited after {maxRetries} attempts. Please try again later."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error on attempt {attempt + 1}");
            
            if (attempt >= maxRetries - 1)
            {
                return new QueryResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing query after {maxRetries} attempts: {ex.Message}"
                };
            }
            
            // For non-rate-limit errors, still wait before retry
            if (attempt < maxRetries - 1)
            {
                await Task.Delay(baseDelayMs);
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