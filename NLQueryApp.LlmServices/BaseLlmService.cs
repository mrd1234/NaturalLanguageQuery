// Base abstract class for LLM services

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.LlmServices;

public abstract class BaseLlmService : ILlmService
{
    protected readonly HttpClient _httpClient;
    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;
    private readonly LlmRetryHandler _retryHandler;

    protected BaseLlmService(HttpClient httpClient, IConfiguration configuration, ILogger logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _retryHandler = new LlmRetryHandler(logger);
    }

    public async Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request)
    {
        var systemPrompt = SystemPrompt.CreateSystemPrompt(
            request.DatabaseSchema, 
            request.SchemaContext, 
            request.DataSourceType, 
            request.DialectNotes);
        var userPrompt = CreateUserPrompt(request);

        return await _retryHandler.ExecuteWithRetryAsync(async () =>
        {
            var httpRequest = CreateHttpRequest(systemPrompt, userPrompt);
            var response = await _httpClient.SendAsync(httpRequest);
            
            // Handle rate limiting
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = GetRetryAfterDelay(response);
                throw new RateLimitException($"Rate limited. Retry after {retryAfter}ms", retryAfter);
            }
            
            response.EnsureSuccessStatusCode();
            var content = await ExtractContentFromResponse(response);
            return ParseLlmResponse(content, request.DataSourceType);
        }, GetServiceName());
    }

    // Abstract methods each service must implement
    protected abstract HttpRequestMessage CreateHttpRequest(string systemPrompt, string userPrompt);
    protected abstract Task<string> ExtractContentFromResponse(HttpResponseMessage response);
    protected abstract string GetServiceName();
    public abstract bool HasApiKey();

    // Shared implementation
    protected string CreateUserPrompt(LlmQueryRequest request)
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

    protected LlmQueryResponse ParseLlmResponse(string content, string dataSourceType)
    {
        try
        {
            // First try to parse as JSON
            if (content.Contains("{") && content.Contains("}"))
            {
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(content, @"\{.*\}", 
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    var jsonResponse = JsonSerializer.Deserialize<LlmQueryResponse>(jsonMatch.Value, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                    if (jsonResponse != null && !string.IsNullOrEmpty(jsonResponse.SqlQuery))
                    {
                        return jsonResponse;
                    }
                }
            }
        
            // Fallback: Extract query from code blocks
            var queryLanguage = GetQueryLanguage(dataSourceType);
            var sqlMatch = System.Text.RegularExpressions.Regex.Match(
                content, 
                $@"```{queryLanguage}\s*(.*?)\s*```", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!sqlMatch.Success)
            {
                sqlMatch = System.Text.RegularExpressions.Regex.Match(
                    content, 
                    @"```\s*(.*?)\s*```", 
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            }
        
            if (sqlMatch.Success)
            {
                var sql = sqlMatch.Groups[1].Value.Trim();
                var explanation = content.Replace(sqlMatch.Value, "").Trim();
            
                return new LlmQueryResponse
                {
                    SqlQuery = sql,
                    Explanation = explanation
                };
            }
        
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Failed to parse query from response: {content}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LLM response");
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Error parsing LLM response: {ex.Message}"
            };
        }
    }

    private string GetQueryLanguage(string dataSourceType) => dataSourceType.ToLower() switch
    {
        "postgres" => "sql",
        "mysql" => "sql", 
        "sqlserver" => "sql",
        "mongodb" => "mongodb",
        "elasticsearch" => "elasticsearch",
        _ => "sql"
    };
    
    private int GetRetryAfterDelay(HttpResponseMessage response)
    {
        var maxRetrySeconds = _configuration.GetValue<int>("LlmSettings:Anthropic:MaxRetryDelaySeconds", 10);
        var failFast = _configuration.GetValue<bool>("LlmSettings:Anthropic:FailFastOnRateLimit", false);
    
        if (response.Headers.RetryAfter?.Delta.HasValue == true)
        {
            var seconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
        
            if (failFast && seconds > maxRetrySeconds)
            {
                throw new InvalidOperationException($"Rate limit retry delay too long: {seconds}s. Failing fast.");
            }
        
            return Math.Min(Math.Max(seconds * 1000, 2000), maxRetrySeconds * 1000);
        }
    
        return 5000;
    }
}