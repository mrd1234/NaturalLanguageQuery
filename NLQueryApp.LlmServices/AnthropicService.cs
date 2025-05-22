using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

namespace NLQueryApp.LlmServices;

public class AnthropicService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnthropicService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicService(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        _apiKey = _configuration["LlmSettings:Anthropic:ApiKey"] 
            ?? throw new ArgumentNullException("Anthropic API key is not configured");
        
        _model = _configuration["LlmSettings:Anthropic:Model"] ?? "claude-3-7-sonnet-20250219";
        
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.Timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("LlmSettings:Anthropic:TimeoutMinutes", 2));
    }

    public async Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request)
    {
        var systemPrompt = SystemPrompt.CreateSystemPrompt(request.DatabaseSchema, request.SchemaContext, request.DataSourceType);
        var userPrompt = CreateUserPrompt(request);
        
        // Enhance system prompt to ensure JSON response
        var enhancedSystemPrompt = $@"{systemPrompt}

IMPORTANT: Always respond with valid JSON in exactly this format:
{{
    ""sqlQuery"": ""your generated query here"",
    ""explanation"": ""brief explanation of the query""
}}

Do not include any text outside the JSON object.";
    
        var messages = new List<object>
        {
            new { role = "user", content = enhancedSystemPrompt + "\n\n" + userPrompt }
        };
    
        var requestData = new
        {
            model = _model,
            messages,
            max_tokens = 4000,
            temperature = 0.0
        };

        _logger.LogDebug("Anthropic Request Data: {RequestData}", JsonSerializer.Serialize(requestData));

        var content = string.Empty;

        try
        {
            var response = await _httpClient.PostAsJsonAsync("messages", requestData);
            response.EnsureSuccessStatusCode();

            var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
            content = responseObject.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
            
            _logger.LogDebug("Raw Anthropic response: {Response}", content);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response from Anthropic");
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = "Received empty response from Anthropic"
                };
            }

            // Parse the response with fallback handling
            return ParseLlmResponse(content, request.DataSourceType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error when calling Anthropic API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"HTTP error when calling Anthropic: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout when calling Anthropic API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = "Timeout when calling Anthropic API"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error from Anthropic response: {Content}", content);
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Failed to parse JSON from Anthropic response: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when calling Anthropic API");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Unexpected error when calling Anthropic: {ex.Message}"
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

    private LlmQueryResponse ParseLlmResponse(string content, string dataSourceType)
    {
        try
        {
            // First try to parse as JSON
            if (content.Contains("{") && content.Contains("}"))
            {
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(content, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    var jsonResponse = JsonSerializer.Deserialize<LlmQueryResponse>(jsonMatch.Value, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                
                    if (jsonResponse != null && !string.IsNullOrEmpty(jsonResponse.SqlQuery))
                    {
                        _logger.LogDebug("Successfully parsed JSON response from Anthropic");
                        return jsonResponse;
                    }
                }
            }
        
            // Fallback: Extract query and explanation manually
            var queryLanguage = GetQueryLanguage(dataSourceType);
            var sqlMatch = System.Text.RegularExpressions.Regex.Match(
                content, 
                $@"```{queryLanguage}\s*(.*?)\s*```", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!sqlMatch.Success)
            {
                // Try without language specifier
                sqlMatch = System.Text.RegularExpressions.Regex.Match(
                    content, 
                    @"```\s*(.*?)\s*```", 
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            }
        
            if (sqlMatch.Success)
            {
                var sql = sqlMatch.Groups[1].Value.Trim();
                var explanation = content.Replace(sqlMatch.Value, "").Trim();
                
                _logger.LogInformation("Parsed SQL from code block fallback");
            
                return new LlmQueryResponse
                {
                    SqlQuery = sql,
                    Explanation = explanation
                };
            }
        
            // If we still can't find SQL, return the whole response as an error
            _logger.LogWarning("Failed to parse any structured response from Anthropic");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Failed to parse query from response: {content}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Anthropic LLM response");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Error parsing LLM response: {ex.Message}"
            };
        }
    }
    
    private string GetQueryLanguage(string dataSourceType)
    {
        return dataSourceType.ToLower() switch
        {
            "postgres" => "sql",
            "mysql" => "sql",
            "sqlserver" => "sql",
            "mongodb" => "mongodb",
            "elasticsearch" => "elasticsearch",
            _ => "sql"
        };
    }
}