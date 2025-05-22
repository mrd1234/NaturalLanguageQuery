using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

namespace NLQueryApp.LlmServices;

public class OllamaService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaService> _logger;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly int _contextWindow;

    public OllamaService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = _configuration["LlmSettings:Ollama:Model"] ?? "llama3";
        _contextWindow = _configuration.GetValue<int>("LlmSettings:Ollama:ContextWindow", 128000);

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Ollama:TimeoutMinutes", 5));
    }

    public async Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request)
    {
        var systemPrompt = SystemPrompt.CreateSystemPrompt(request.DatabaseSchema, request.SchemaContext, request.DataSourceType);
        var userPrompt = CreateUserPrompt(request);

        // Enhanced prompt to ensure JSON response
        var prompt = $@"{systemPrompt}

IMPORTANT: Always respond with valid JSON in exactly this format:
{{
    ""sqlQuery"": ""your generated query here"",
    ""explanation"": ""brief explanation of the query""
}}

Do not include any text outside the JSON object.

{userPrompt}";

        // JSON schema definition for the expected response format
        var jsonSchema = new
        {
            type = "object",
            properties = new
            {
                sqlQuery = new
                {
                    type = "string",
                    description = "The SQL query to execute against the database"
                },
                explanation = new
                {
                    type = "string",
                    description = "Brief explanation of what the query does"
                }
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
            options = new {
                num_ctx = _contextWindow
            }
        };
        
        _logger.LogDebug("Ollama Request Data: {RequestData}", JsonSerializer.Serialize(requestData));

        var content = string.Empty;
        
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/generate", requestData);
            response.EnsureSuccessStatusCode();

            var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
            content = responseObject.GetProperty("response").GetString() ?? string.Empty;
            
            _logger.LogDebug("Raw Ollama response: {Response}", content);
            
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response from Ollama");
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = "Received empty response from Ollama"
                };
            }
            
            // Direct JSON deserialization with schema enforcement
            var result = JsonSerializer.Deserialize<LlmQueryResponse>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (result == null)
            {
                _logger.LogWarning("Failed to deserialize Ollama response to LlmQueryResponse");
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = "Failed to deserialize response from Ollama"
                };
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error when calling Ollama API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"HTTP error when calling Ollama: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout when calling Ollama API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = "Timeout when calling Ollama API"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error from Ollama response: {Content}", content);
            
            // Fallback: try to extract JSON from response if it's wrapped in text
            if (!string.IsNullOrEmpty(content))
            {
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(content, @"\{.*\}", 
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                    
                if (jsonMatch.Success)
                {
                    try 
                    {
                        var fallbackResult = JsonSerializer.Deserialize<LlmQueryResponse>(jsonMatch.Value, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        
                        if (fallbackResult != null)
                        {
                            _logger.LogInformation("Successfully parsed JSON from wrapped response");
                            return fallbackResult;
                        }
                    }
                    catch (JsonException fallbackEx)
                    {
                        _logger.LogError(fallbackEx, "Fallback JSON parsing also failed");
                    }
                }
            }
            
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Failed to parse JSON from Ollama response: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when calling Ollama API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Unexpected error when calling Ollama: {ex.Message}"
            };
        }
    }

    private string CreateUserPrompt(LlmQueryRequest request)
    {
        if (!string.IsNullOrEmpty(request.PreviousError))
        {
            return $@"
My previous query was:
{request.PreviousSqlQuery}

But it resulted in the following error:
{request.PreviousError}

Please fix the query. The original question was: {request.UserQuestion}
";
        }

        return $"Convert the following question to a {request.DataSourceType} query: {request.UserQuestion}";
    }
}