using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

public class OllamaService : BaseLlmService
{
    private readonly Dictionary<ModelType, string> _models;
    private readonly int _contextWindow;
    private readonly string _baseUrl;

    public OllamaService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaService> logger)
        : base(httpClient, configuration, logger)
    {
        _baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _contextWindow = _configuration.GetValue<int>("LlmSettings:Ollama:ContextWindow", 128000);

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Ollama:TimeoutMinutes", 5));

        // Load model configuration - support both old and new format
        _models = LoadModelConfiguration();
        
        _logger.LogInformation("Ollama service initialized with base URL: {BaseUrl}", _baseUrl);
        _logger.LogInformation("Configured models: Query={Query}, Utility={Utility}, Summary={Summary}", 
            _models.GetValueOrDefault(ModelType.Query, "not set"),
            _models.GetValueOrDefault(ModelType.Utility, "not set"),
            _models.GetValueOrDefault(ModelType.Summary, "not set"));
    }

    private Dictionary<ModelType, string> LoadModelConfiguration()
    {
        var models = new Dictionary<ModelType, string>();
        
        // Check if new model hierarchy is configured
        var queryModel = _configuration["LlmSettings:Ollama:Models:Query"];
        var utilityModel = _configuration["LlmSettings:Ollama:Models:Utility"];
        var summaryModel = _configuration["LlmSettings:Ollama:Models:Summary"];
        
        if (!string.IsNullOrEmpty(queryModel))
        {
            // New hierarchy format
            _logger.LogInformation("Using new model hierarchy configuration for Ollama");
            models[ModelType.Query] = queryModel;
            
            if (!string.IsNullOrEmpty(utilityModel))
                models[ModelType.Utility] = utilityModel;
            else
                models[ModelType.Utility] = queryModel; // Fallback to query model
                
            if (!string.IsNullOrEmpty(summaryModel))
                models[ModelType.Summary] = summaryModel;
            else
                models[ModelType.Summary] = utilityModel ?? queryModel; // Fallback hierarchy
        }
        else
        {
            // Legacy format - single model for all purposes
            var legacyModel = _configuration["LlmSettings:Ollama:Model"] ?? "qwen2.5-coder:14b-instruct-q4_k_m";
            _logger.LogInformation("Using legacy model configuration for Ollama: {Model}", legacyModel);
            
            models[ModelType.Query] = legacyModel;
            models[ModelType.Utility] = legacyModel;
            models[ModelType.Summary] = legacyModel;
        }
        
        return models;
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt, ModelType modelType)
    {
        var model = GetModelForType(modelType);
        var prompt = string.IsNullOrEmpty(systemPrompt) ? userPrompt : $@"{systemPrompt}

{userPrompt}";

        object requestData;

        if (modelType == ModelType.Query)
        {
            // Use JSON schema for structured SQL responses
            var jsonSchema = new
            {
                type = "object",
                properties = new
                {
                    sqlQuery = new { type = "string", description = "The SQL query to execute" },
                    explanation = new { type = "string", description = "Brief explanation of the query" }
                },
                required = new[] { "sqlQuery", "explanation" }
            };

            requestData = new
            {
                model,
                prompt,
                stream = false,
                temperature = 0.0,
                format = jsonSchema,
                options = new { num_ctx = _contextWindow }
            };
        }
        else
        {
            // Simple text response for utility tasks
            requestData = new
            {
                model,
                prompt,
                stream = false,
                temperature = 0.1, // Slightly higher for more creative utility responses
                options = new { 
                    num_ctx = Math.Min(_contextWindow, 4096), // Use smaller context for utility tasks
                    top_p = 0.9,
                    top_k = 40
                }
            };
        }

        _logger.LogDebug("Creating Ollama request for model {Model} with type {ModelType}", model, modelType);

        return new HttpRequestMessage(HttpMethod.Post, "api/generate")
        {
            Content = JsonContent.Create(requestData)
        };
    }

    protected override async Task<string> ExtractContentFromResponse(HttpResponseMessage response)
    {
        try
        {
            var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
            
            if (responseObject.TryGetProperty("response", out var responseProperty))
            {
                return responseProperty.GetString() ?? "";
            }
            
            _logger.LogWarning("Ollama response missing 'response' property");
            return "";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Ollama response JSON");
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Raw response content: {Content}", content);
            throw new InvalidOperationException($"Failed to parse Ollama response: {ex.Message}");
        }
    }

    protected override string GetServiceName() => "ollama";

    protected override string GetModelForType(ModelType modelType)
    {
        if (_models.TryGetValue(modelType, out var model) && !string.IsNullOrEmpty(model))
        {
            return model;
        }
        
        // Fallback to query model if available
        if (_models.TryGetValue(ModelType.Query, out var queryModel))
        {
            _logger.LogWarning("Model for type {ModelType} not found, falling back to Query model: {Model}", 
                modelType, queryModel);
            return queryModel;
        }
        
        // Last resort fallback
        var defaultModel = "qwen2.5-coder:14b-instruct-q4_k_m";
        _logger.LogWarning("No models configured for Ollama, using default: {Model}", defaultModel);
        return defaultModel;
    }

    public override bool HasApiKey() => true; // Ollama doesn't require API keys

    public override bool HasModel(ModelType modelType)
    {
        var hasModel = _models.ContainsKey(modelType) && !string.IsNullOrEmpty(_models[modelType]);
        _logger.LogDebug("HasModel({ModelType}) = {HasModel} for Ollama", modelType, hasModel);
        return hasModel;
    }
}