using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Data.Providers;

public abstract class BaseDataSourceProvider : IDataSourceProvider
{
    protected readonly ILogger _logger;

    protected BaseDataSourceProvider(ILogger logger)
    {
        _logger = logger;
    }

    public abstract string ProviderType { get; }
    
    public abstract Task<string> GetSchemaAsync(DataSourceDefinition dataSource);
    
    public abstract Task<DatabaseInfo?> GetDatabaseInfoAsync(DataSourceDefinition dataSource);
    
    public abstract Task<string> GetDialectNotesAsync(DataSourceDefinition dataSource);
    
    public abstract Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query);
    
    public abstract Task<ValidationResult> ValidateQueryAsync(DataSourceDefinition dataSource, string query);
    
    public abstract Task<bool> TestConnectionAsync(DataSourceDefinition dataSource);
    
    public abstract Task<string?> GetQueryPlanAsync(DataSourceDefinition dataSource, string query);
    
    public abstract Task<DataSourceMetadata> GetMetadataAsync(DataSourceDefinition dataSource);
    
    // Default implementation for title generation - can be overridden
    public virtual async Task<string> GenerateTitleAsync(DataSourceDefinition dataSource, string userQuestion, ILlmService? llmService = null)
    {
        try
        {
            if (llmService != null && llmService.HasModel(ModelType.Utility))
            {
                // Get schema context for better title generation
                var schemaContext = await GetSchemaContextForTitleGeneration(dataSource);
                var entities = await GetEntityDescriptionsAsync(dataSource);
                
                var prompt = CreateTitleGenerationPrompt(userQuestion, schemaContext, entities);
                
                try
                {
                    var response = await llmService.GenerateUtilityResponseAsync(prompt, ModelType.Utility);
                    
                    if (!string.IsNullOrWhiteSpace(response) && response != "New Conversation")
                    {
                        var title = SanitizeTitle(response);
                        _logger.LogInformation("Generated title using LLM for {DataSourceType}: {Title}", ProviderType, title);
                        return title;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate title using LLM for {DataSourceType}, using fallback", ProviderType);
                }
            }
            
            // Fallback to basic title generation
            return GenerateFallbackTitle(userQuestion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating title for {DataSourceType}", ProviderType);
            return GenerateFallbackTitle(userQuestion);
        }
    }
    
    // Default implementation - should be overridden by specific providers
    public virtual async Task<List<QueryExample>> GetQueryExamplesAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return new List<QueryExample>();
    }
    
    // Default implementation - should be overridden by specific providers
    public virtual async Task<Dictionary<string, string>> GetEntityDescriptionsAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return new Dictionary<string, string>();
    }
    
    // Default implementation - not all providers need schema setup
    public virtual async Task<bool> SetupSchemaAsync(DataSourceDefinition dataSource, bool dropIfExists = false)
    {
        await Task.CompletedTask;
        _logger.LogInformation("Schema setup not implemented for provider type {ProviderType}", ProviderType);
        return false;
    }
    
    // Default implementation for prompt enhancements
    public virtual async Task<string> GetPromptEnhancementsAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return string.Empty;
    }
    
    /// <summary>
    /// Get the query language name used by this data source
    /// Default implementation returns "SQL" - override in specific providers
    /// </summary>
    public virtual async Task<string> GetQueryLanguageAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return "SQL"; // Default fallback
    }
    
    /// <summary>
    /// Get the display name for the query language
    /// Default implementation returns the same as GetQueryLanguageAsync
    /// </summary>
    public virtual async Task<string> GetQueryLanguageDisplayAsync(DataSourceDefinition dataSource)
    {
        return await GetQueryLanguageAsync(dataSource);
    }
    
    // Protected helper methods
    protected virtual async Task<string> GetSchemaContextForTitleGeneration(DataSourceDefinition dataSource)
    {
        try
        {
            // Get a simplified version of the schema for title generation
            var metadata = await GetMetadataAsync(dataSource);
            var entities = await GetEntityDescriptionsAsync(dataSource);
            
            var context = $"Database Type: {metadata.DatabaseType}\n";
            context += $"Available Schemas: {string.Join(", ", metadata.AvailableSchemas)}\n";
            
            if (entities.Any())
            {
                context += "\nMain Entities:\n";
                foreach (var entity in entities.Take(10)) // Limit to avoid token overflow
                {
                    context += $"- {entity.Key}: {entity.Value}\n";
                }
            }
            
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get schema context for title generation");
            return string.Empty;
        }
    }
    
    protected virtual string CreateTitleGenerationPrompt(string userQuestion, string schemaContext, Dictionary<string, string> entities)
    {
        var prompt = $@"You are creating a concise title for a database query.

Database Context:
{schemaContext}

Generate a concise, descriptive title (4-8 words maximum) that captures what the user is asking about.
Focus on the key entities and action they're interested in.
Do not include quotes or extra formatting. Just return the title text.

";

        if (entities.Any())
        {
            prompt += "Key entities in this database:\n";
            foreach (var entity in entities.Take(5))
            {
                prompt += $"- {entity.Key}: {entity.Value}\n";
            }
            prompt += "\n";
        }

        prompt += $"User question: {userQuestion}";
        
        return prompt;
    }
    
    protected string GenerateFallbackTitle(string userQuestion)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
            return "New Conversation";

        // Clean the question
        var cleaned = userQuestion.Trim();
        
        // Remove common question starters to save space
        var commonStarters = new[] { "how do i ", "how can i ", "what is ", "what are ", "show me ", "find ", "get ", "list ", "count " };
        foreach (var starter in commonStarters)
        {
            if (cleaned.StartsWith(starter, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(starter.Length);
                break;
            }
        }

        // Truncate at word boundary
        if (cleaned.Length <= 50)
            return SanitizeTitle(cleaned);

        var truncated = cleaned.Substring(0, 47);
        var lastSpace = truncated.LastIndexOf(' ');
        
        if (lastSpace > 20) // Don't truncate too aggressively
        {
            truncated = truncated.Substring(0, lastSpace);
        }
        
        return SanitizeTitle(truncated + "...");
    }
    
    protected string SanitizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "New Conversation";
            
        // Remove problematic characters and clean up
        var sanitized = title.Trim()
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("\n", " ")
            .Replace("\r", "")
            .Replace("\t", " ");
        
        // Collapse multiple spaces
        while (sanitized.Contains("  "))
            sanitized = sanitized.Replace("  ", " ");
        
        // Ensure reasonable length
        if (sanitized.Length > 80)
        {
            sanitized = sanitized.Substring(0, 77) + "...";
        }
        
        return string.IsNullOrWhiteSpace(sanitized) ? "New Conversation" : sanitized;
    }
}