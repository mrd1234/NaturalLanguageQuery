using System.Text;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.LlmServices;

namespace NLQueryApp.Api.Services;

public class QueryService
{
    private readonly IDataSourceManager _dataSourceManager;
    private readonly IDatabaseService _databaseService;
    private readonly LlmServiceFactory _llmServiceFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        IDataSourceManager dataSourceManager,
        IDatabaseService databaseService,
        LlmServiceFactory llmServiceFactory,
        IConfiguration configuration,
        ILogger<QueryService> logger)
    {
        _logger = logger;
        _dataSourceManager = dataSourceManager;
        _databaseService = databaseService;
        _llmServiceFactory = llmServiceFactory;
        _configuration = configuration;
    }

    public async Task<QueryResult> ProcessNaturalLanguageQueryAsync(
        string dataSourceId, 
        string question, 
        string? llmServiceName = null,
        int? conversationId = null)
    {
        llmServiceName ??= _configuration["LlmSettings:DefaultService"] ?? "anthropic";
        var llmService = _llmServiceFactory.GetService(llmServiceName);
    
        // Get database schema, context, dialect notes, and prompt enhancements
        var dataSource = await _dataSourceManager.GetDataSourceAsync(dataSourceId);
        var schema = await _dataSourceManager.GetSchemaAsync(dataSourceId);
        var schemaContext = await _dataSourceManager.GetSchemaContextAsync(dataSourceId);
        var dialectNotes = await _dataSourceManager.GetDialectNotesAsync(dataSourceId);
        var promptEnhancements = await _dataSourceManager.GetPromptEnhancementsAsync(dataSourceId);
        
        // NEW: Get query language from the data source provider
        var queryLanguage = await _dataSourceManager.GetQueryLanguageAsync(dataSourceId);
        
        // Combine schema context with data source specific enhancements
        var enhancedContext = schemaContext;
        if (!string.IsNullOrWhiteSpace(promptEnhancements))
        {
            enhancedContext += "\n\n" + promptEnhancements;
        }
        
        // Get conversation context if available
        string? conversationContext = null;
        if (conversationId.HasValue)
        {
            conversationContext = await BuildConversationContext(conversationId.Value, dataSourceId);
        }
    
        // Create request with query language instead of data source type
        var request = new LlmQueryRequest
        {
            UserQuestion = question,
            DatabaseSchema = schema,
            SchemaContext = enhancedContext,
            DialectNotes = dialectNotes,
            QueryLanguage = queryLanguage,  // Changed from DataSourceType
            ConversationContext = conversationContext
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
                    _logger.LogInformation("Retry attempt {Attempt}, waiting {Delay}ms before calling LLM service", 
                        attempt + 1, delay);
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
                    _logger.LogInformation("Query succeeded on attempt {Attempt}", attempt + 1);
                    return queryResult;
                }
                
                // Log the failure for debugging
                _logger.LogWarning("Query failed on attempt {Attempt}: {Error}", attempt + 1, queryResult.ErrorMessage);
                
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
                    _logger.LogInformation("Preparing retry {Attempt} with previous error: {Error}", 
                        attempt + 2, queryResult.ErrorMessage);
                }
                else
                {
                    _logger.LogError("All {MaxRetries} attempts failed. Final error: {Error}", 
                        maxRetries, queryResult.ErrorMessage);
                    return queryResult;
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                // Handle rate limiting specifically
                _logger.LogWarning("Rate limited on attempt {Attempt}: {Message}", attempt + 1, ex.Message);
                
                if (attempt < maxRetries - 1)
                {
                    // Longer delay for rate limiting
                    var rateLimitDelay = baseDelayMs * (int)Math.Pow(3, attempt + 1); // 3s, 9s, 27s
                    _logger.LogInformation("Rate limited, waiting {Delay}ms before retry", rateLimitDelay);
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
                _logger.LogError(ex, "Unexpected error on attempt {Attempt}", attempt + 1);
                
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
    
    private async Task<string> BuildConversationContext(int conversationId, string dataSourceId)
    {
        try
        {
            var conversation = await _databaseService.GetConversationAsync(conversationId);
            var recentMessages = conversation.Messages
                .Where(m => m.DataSourceId == dataSourceId || m.DataSourceId == null) // Include null for backward compat
                .OrderByDescending(m => m.Timestamp)
                .Take(10) // Get last 10 messages
                .Reverse()
                .ToList();
            
            if (!recentMessages.Any())
                return string.Empty;
            
            var context = new StringBuilder();
            context.AppendLine("## Recent Conversation History");
            context.AppendLine();
            
            // Build context with query-response pairs
            for (int i = 0; i < recentMessages.Count; i++)
            {
                var msg = recentMessages[i];
                if (msg.Role == "user")
                {
                    context.AppendLine($"User asked: {msg.Content}");
                    
                    // Look for the assistant's response
                    if (i + 1 < recentMessages.Count && recentMessages[i + 1].Role == "assistant")
                    {
                        var response = recentMessages[i + 1];
                        if (response.QuerySuccess == true && !string.IsNullOrEmpty(response.SqlQuery))
                        {
                            context.AppendLine($"Generated SQL: {response.SqlQuery}");
                            context.AppendLine("Result: Query executed successfully");
                        }
                        else if (response.QuerySuccess == false)
                        {
                            context.AppendLine("Result: Query failed");
                        }
                        i++; // Skip the assistant message in next iteration
                    }
                    context.AppendLine();
                }
            }
            
            context.AppendLine("Use this conversation history to understand references to previous queries, results, or concepts discussed earlier.");
            
            return context.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build conversation context for conversation {ConversationId}", conversationId);
            return string.Empty;
        }
    }
}