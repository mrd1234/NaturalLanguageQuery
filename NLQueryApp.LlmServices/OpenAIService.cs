using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

public class OpenAIService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly Dictionary<ModelType, string> _models;

    public OpenAIService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAIService> logger)
        : base(httpClient, configuration, logger)
    {
        _apiKey = _configuration["LlmSettings:OpenAI:ApiKey"] ?? "";
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:OpenAI:TimeoutMinutes", 3));

        // Load model configuration
        _models = LoadModelConfiguration();
    }

    private Dictionary<ModelType, string> LoadModelConfiguration()
    {
        var models = new Dictionary<ModelType, string>();
        
        // Check if new model hierarchy is configured
        var queryModel = _configuration["LlmSettings:OpenAI:Models:Query"];
        var utilityModel = _configuration["LlmSettings:OpenAI:Models:Utility"];
        var summaryModel = _configuration["LlmSettings:OpenAI:Models:Summary"];
        
        if (!string.IsNullOrEmpty(queryModel))
        {
            // New hierarchy format
            models[ModelType.Query] = queryModel;
            
            if (!string.IsNullOrEmpty(utilityModel))
                models[ModelType.Utility] = utilityModel;
            else
                models[ModelType.Utility] = "gpt-3.5-turbo"; // Default fast model
                
            if (!string.IsNullOrEmpty(summaryModel))
                models[ModelType.Summary] = summaryModel;
            else
                models[ModelType.Summary] = utilityModel ?? "gpt-3.5-turbo";
        }
        else
        {
            // Legacy format - single model, with intelligent defaults
            var legacyModel = _configuration["LlmSettings:OpenAI:Model"] ?? "gpt-4-turbo-preview";
            models[ModelType.Query] = legacyModel;
            models[ModelType.Utility] = "gpt-3.5-turbo"; // Always use fast model for utilities
            models[ModelType.Summary] = "gpt-3.5-turbo";
        }
        
        return models;
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt, ModelType modelType)
    {
        var model = GetModelForType(modelType);
        var messages = new List<object>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        messages.Add(new { role = "user", content = userPrompt });

        var requestData = new
        {
            model,
            messages,
            temperature = modelType == ModelType.Query ? 0.0 : 0.1,
            max_tokens = modelType == ModelType.Query ? 4000 : 1000, // Smaller tokens for utility tasks
            response_format = modelType == ModelType.Query ? new { type = "json_object" } : null
        };

        return new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(requestData)
        };
    }

    protected override async Task<string> ExtractContentFromResponse(HttpResponseMessage response)
    {
        // Log rate limit headers for debugging
        if (response.Headers.TryGetValues("x-ratelimit-limit-requests", out var requestLimit))
            _logger.LogInformation("OpenAI requests limit: {Limit}", requestLimit.FirstOrDefault());
    
        if (response.Headers.TryGetValues("x-ratelimit-remaining-requests", out var requestsRemaining))
            _logger.LogInformation("OpenAI requests remaining: {Remaining}", requestsRemaining.FirstOrDefault());
    
        if (response.Headers.TryGetValues("x-ratelimit-limit-tokens", out var tokenLimit))
            _logger.LogInformation("OpenAI tokens limit: {Limit}", tokenLimit.FirstOrDefault());
    
        var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
        return responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    protected override string GetServiceName() => "openai";
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