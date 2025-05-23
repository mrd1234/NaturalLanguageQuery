namespace NLQueryApp.Core;

public interface ILlmService
{
    /// <summary>
    /// Generate SQL query using the Query model (heavy model for complex reasoning)
    /// </summary>
    Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request);
    
    /// <summary>
    /// Generate a concise title for a user question using lightweight Utility model
    /// </summary>
    Task<string> GenerateTitleAsync(string userQuestion);
    
    /// <summary>
    /// Generate a general utility response using the specified model type
    /// </summary>
    Task<string> GenerateUtilityResponseAsync(string prompt, ModelType modelType = ModelType.Utility);
    
    /// <summary>
    /// Check if the service has valid API key/configuration
    /// </summary>
    bool HasApiKey();
    
    /// <summary>
    /// Check if a specific model type is configured for this service
    /// </summary>
    bool HasModel(ModelType modelType);
}