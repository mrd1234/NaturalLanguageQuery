using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class AnthropicService : BaseLlmService
{
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicService(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicService> logger)
        : base(httpClient, configuration, logger)
    {
        // Don't throw exception - just use empty string as default like other services
        _apiKey = _configuration["LlmSettings:Anthropic:ApiKey"] ?? "";
        
        _model = _configuration["LlmSettings:Anthropic:Model"] ?? "claude-3-7-sonnet-20250219";
        
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        
        // Only add headers if we have an API key
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Anthropic:TimeoutMinutes", 3));
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt)
    {
        var enhancedPrompt = $@"{systemPrompt}

{userPrompt}";

        var messages = new List<object>
        {
            new { role = "user", content = enhancedPrompt }
        };

        var requestData = new
        {
            model = _model,
            messages,
            max_tokens = 4000,
            temperature = 0.0
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
    
    public override bool HasApiKey() => !string.IsNullOrEmpty(_apiKey);
}