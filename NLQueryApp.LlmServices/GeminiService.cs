using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class GeminiService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        : base(httpClient, configuration, logger)
    {
        _apiKey = _configuration["LlmSettings:Gemini:ApiKey"] ?? "";
        _model = _configuration["LlmSettings:Gemini:Model"] ?? "gemini-pro";
        
        var baseUrl = "https://generativelanguage.googleapis.com/v1beta/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Gemini:TimeoutMinutes", 3));
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt)
    {
        var combinedPrompt = $@"{systemPrompt}

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
                temperature = 0.0,
                maxOutputTokens = 4000,
                candidateCount = 1,
                responseMimeType = "application/json"
            }
        };

        // Only include API key if it's configured
        var url = !string.IsNullOrEmpty(_apiKey) 
            ? $"models/{_model}:generateContent?key={_apiKey}"
            : $"models/{_model}:generateContent";
        
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
    
    public override bool HasApiKey() => !string.IsNullOrEmpty(_apiKey);
}