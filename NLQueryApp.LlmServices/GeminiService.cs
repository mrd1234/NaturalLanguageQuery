using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

public class GeminiService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly Dictionary<ModelType, string> _models;

    public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        : base(httpClient, configuration, logger)
    {
        _apiKey = _configuration["LlmSettings:Gemini:ApiKey"] ?? "";
        
        var baseUrl = "https://generativelanguage.googleapis.com/v1beta/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Gemini:TimeoutMinutes", 3));

        // Load model configuration
        _models = LoadModelConfiguration();
    }

    private Dictionary<ModelType, string> LoadModelConfiguration()
    {
        var models = new Dictionary<ModelType, string>();
        
        // Check if new model hierarchy is configured
        var queryModel = _configuration["LlmSettings:Gemini:Models:Query"];
        var utilityModel = _configuration["LlmSettings:Gemini:Models:Utility"];
        var summaryModel = _configuration["LlmSettings:Gemini:Models:Summary"];
        
        if (!string.IsNullOrEmpty(queryModel))
        {
            // New hierarchy format
            models[ModelType.Query] = queryModel;
            
            if (!string.IsNullOrEmpty(utilityModel))
                models[ModelType.Utility] = utilityModel;
            else
                models[ModelType.Utility] = "gemini-pro"; // Default fast model
                
            if (!string.IsNullOrEmpty(summaryModel))
                models[ModelType.Summary] = summaryModel;
            else
                models[ModelType.Summary] = utilityModel ?? "gemini-pro";
        }
        else
        {
            // Legacy format - single model
            var legacyModel = _configuration["LlmSettings:Gemini:Model"] ?? "gemini-pro";
            models[ModelType.Query] = legacyModel;
            models[ModelType.Utility] = legacyModel; // Use same model since Gemini is already fast
            models[ModelType.Summary] = legacyModel;
        }
        
        return models;
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt, ModelType modelType)
    {
        var model = GetModelForType(modelType);
        var combinedPrompt = string.IsNullOrEmpty(systemPrompt) ? userPrompt : $@"{systemPrompt}

{userPrompt}";

        var requestData = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = combinedPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = modelType == ModelType.Query ? 0.0 : 0.1,
                maxOutputTokens = modelType == ModelType.Query ? 4000 : 1000,
                candidateCount = 1,
                responseMimeType = modelType == ModelType.Query ? "application/json" : "text/plain"
            }
        };

        // Only include API key if it's configured
        var url = !string.IsNullOrEmpty(_apiKey) 
            ? $"models/{model}:generateContent?key={_apiKey}"
            : $"models/{model}:generateContent";
        
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestData)
        };
    }

    protected override async Task<string> ExtractContentFromResponse(HttpResponseMessage response)
    {
        var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        // Gemini API structure: candidates[0].content.parts[0].text
        if (responseObject.TryGetProperty("candidates", out var candidates) && 
            candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0)
            {
                var firstPart = parts[0];
                if (firstPart.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? "";
                }
            }
        }
        
        _logger.LogError("Unexpected Gemini API response structure: {Response}", responseObject.ToString());
        return "";
    }

    protected override string GetServiceName() => "gemini";

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