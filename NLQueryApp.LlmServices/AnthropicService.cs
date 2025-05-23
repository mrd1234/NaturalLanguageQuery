using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

public class AnthropicService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly Dictionary<ModelType, string> _models;

    public AnthropicService(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicService> logger)
        : base(httpClient, configuration, logger)
    {
        _apiKey = _configuration["LlmSettings:Anthropic:ApiKey"] ?? "";
        
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        
        // Only add headers if we have an API key
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Anthropic:TimeoutMinutes", 3));

        // Load model configuration
        _models = LoadModelConfiguration();
    }

    private Dictionary<ModelType, string> LoadModelConfiguration()
    {
        var models = new Dictionary<ModelType, string>();
        
        // Check if new model hierarchy is configured
        var queryModel = _configuration["LlmSettings:Anthropic:Models:Query"];
        var utilityModel = _configuration["LlmSettings:Anthropic:Models:Utility"];
        var summaryModel = _configuration["LlmSettings:Anthropic:Models:Summary"];
        
        if (!string.IsNullOrEmpty(queryModel))
        {
            // New hierarchy format
            models[ModelType.Query] = queryModel;
            
            if (!string.IsNullOrEmpty(utilityModel))
                models[ModelType.Utility] = utilityModel;
            else
                models[ModelType.Utility] = "claude-3-haiku-20240307"; // Default fast model
                
            if (!string.IsNullOrEmpty(summaryModel))
                models[ModelType.Summary] = summaryModel;
            else
                models[ModelType.Summary] = utilityModel ?? "claude-3-haiku-20240307";
        }
        else
        {
            // Legacy format - single model, with intelligent defaults
            var legacyModel = _configuration["LlmSettings:Anthropic:Model"] ?? "claude-3-7-sonnet-20250219";
            models[ModelType.Query] = legacyModel;
            models[ModelType.Utility] = "claude-3-haiku-20240307"; // Always use fast model for utilities
            models[ModelType.Summary] = "claude-3-haiku-20240307";
        }
        
        return models;
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt, ModelType modelType)
    {
        var model = GetModelForType(modelType);
        var enhancedPrompt = string.IsNullOrEmpty(systemPrompt) ? userPrompt : $@"{systemPrompt}

{userPrompt}";

        var messages = new List<object>
        {
            new { role = "user", content = enhancedPrompt }
        };

        var requestData = new
        {
            model,
            messages,
            max_tokens = modelType == ModelType.Query ? 4000 : 1000, // Smaller tokens for utility tasks
            temperature = modelType == ModelType.Query ? 0.0 : 0.1
        };

        return new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = JsonContent.Create(requestData)
        };
    }

    protected override async Task<string> ExtractContentFromResponse(HttpResponseMessage response)
    {
        // Log rate limit headers for debugging
        if (response.Headers.TryGetValues("x-ratelimit-requests-limit", out var requestLimit))
            _logger.LogInformation("Anthropic requests limit: {Limit}", requestLimit.FirstOrDefault());
    
        if (response.Headers.TryGetValues("x-ratelimit-requests-remaining", out var requestsRemaining))
            _logger.LogInformation("Anthropic requests remaining: {Remaining}", requestsRemaining.FirstOrDefault());
    
        if (response.Headers.TryGetValues("x-ratelimit-tokens-limit", out var tokenLimit))
            _logger.LogInformation("Anthropic tokens limit: {Limit}", tokenLimit.FirstOrDefault());
    
        var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
        return responseObject.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    protected override string GetServiceName() => "anthropic";

    protected override string GetModelForType(ModelType modelType)
    {
        return _models.TryGetValue(modelType, out var model) ? model : _models[ModelType.Query];
    }
    
    public override bool HasApiKey() => !string.IsNullOrEmpty(_apiKey);

    public override bool HasModel(ModelType modelType)
    {
        return HasApiKey() && _models.ContainsKey(modelType) && !string.IsNullOrEmpty(_models[modelType]);
    }
}