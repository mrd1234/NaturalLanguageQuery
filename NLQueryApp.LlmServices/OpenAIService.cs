using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class OpenAIService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAIService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAIService> logger)
        : base(httpClient, configuration, logger)
    {
        _apiKey = _configuration["LlmSettings:OpenAI:ApiKey"] ?? "";
        _model = _configuration["LlmSettings:OpenAI:Model"] ?? "gpt-4-turbo-preview";
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:OpenAI:TimeoutMinutes", 3));
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var requestData = new
        {
            model = _model,
            messages,
            temperature = 0.0,
            max_tokens = 4000,
            response_format = new { type = "json_object" }
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
    
    public override bool HasApiKey() => !string.IsNullOrEmpty(_apiKey);
}