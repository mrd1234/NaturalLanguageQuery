using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class OllamaService : BaseLlmService
{
    private readonly string _model;
    private readonly int _contextWindow;

    public OllamaService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaService> logger)
        : base(httpClient, configuration, logger)
    {
        var baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = _configuration["LlmSettings:Ollama:Model"] ?? "qwen2.5-coder:14b-instruct-q4_k_m0";
        _contextWindow = _configuration.GetValue<int>("LlmSettings:Ollama:ContextWindow", 128000);

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Ollama:TimeoutMinutes", 5));
    }

    protected override HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt)
    {
        var prompt = $@"{systemPrompt}

{userPrompt}";

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

        var requestData = new
        {
            model = _model,
            prompt,
            stream = false,
            temperature = 0.0,
            format = jsonSchema,
            options = new { num_ctx = _contextWindow }
        };

        return new HttpRequestMessage(HttpMethod.Post, "api/generate")
        {
            Content = JsonContent.Create(requestData)
        };
    }

    protected override async Task<string> ExtractContentFromResponse(HttpResponseMessage response)
    {
        var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
        return responseObject.GetProperty("response").GetString() ?? "";
    }

    protected override string GetServiceName() => "ollama";
    
    public override bool HasApiKey() => true; 
}