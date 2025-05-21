using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.LlmServices;

public class AnthropicService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        
        _apiKey = _configuration["LlmSettings:Anthropic:ApiKey"] 
            ?? throw new ArgumentNullException("Anthropic API key is not configured");
        
        _model = _configuration["LlmSettings:Anthropic:Model"] ?? "claude-3-opus-20240229";
        
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request)
    {
        var systemPrompt = SystemPrompt.CreateSystemPrompt(request.DatabaseSchema, request.SchemaContext, request.DataSourceType);
        var userPrompt = CreateUserPrompt(request);
    
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };
    
        var requestData = new
        {
            model = _model,
            messages,
            max_tokens = 4000,
            temperature = 0.0,
            response_format = new { type = "json_object" }
        };
    
        var response = await _httpClient.PostAsJsonAsync("messages", requestData);
        response.EnsureSuccessStatusCode();
    
        var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = responseObject.GetProperty("content").GetProperty("0").GetProperty("text").GetString();
    
        // Parse the JSON response
        return JsonSerializer.Deserialize<LlmQueryResponse>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
    }
    
//     private string CreateSystemPrompt(string databaseSchema, string schemaContext, string dataSourceType)
//     {
//         var queryLanguage = GetQueryLanguage(dataSourceType);
//     
//         return @$"
// You are an expert query generator for {dataSourceType} databases. Your task is to convert natural language questions into valid {queryLanguage} queries.
//
// Here is the database schema you'll be working with:
//
// {databaseSchema}
//
// ## Additional Context About This Schema
//
// {schemaContext}
//
// Important rules:
// 1. Only generate READ-ONLY queries - no INSERT, UPDATE, DELETE, or other modifying statements.
// 2. Always wrap the query in triple backticks (```{queryLanguage}) for clear identification.
// 3. Provide a brief explanation of your query logic after the query.
// 4. Use standard {dataSourceType} syntax and features.
// 5. If you receive an error from a previous query attempt, analyze it carefully and fix the issue.
// 6. Always return your answer in JSON format with two fields: 'sqlQuery' and 'explanation'.
// 7. Make the query as efficient as possible.
// 8. Use appropriate joins when necessary and ensure condition columns match types.
// 9. Do not use functions or features not available in {dataSourceType}.
//
// CRITICAL: When using table aliases, ALWAYS use column names exactly as specified in the schema.
// For example, use mt.type_name (NOT mt.name or mt.movement_type) when querying from movement_types.
//
// CRITICALLY IMPORTANT: 
// - 'team_movements' is a SCHEMA name, NOT a table name
// - All tables are in the team_movements schema
// - Always use team_movements.table_name in your SQL queries
// - For example: FROM team_movements.movement_types mt
// - NEVER use FROM team_movements mt (this is incorrect)
// ";
//     }
    
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
                
                    if (jsonResponse != null)
                    {
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
            
                return new LlmQueryResponse
                {
                    SqlQuery = sql,
                    Explanation = explanation
                };
            }
        
            // If we still can't find SQL, return the whole response as an error
            return new LlmQueryResponse
            {
                SqlQuery = "",
                Explanation = $"Failed to parse query from response: {content}"
            };
        }
        catch (Exception ex)
        {
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