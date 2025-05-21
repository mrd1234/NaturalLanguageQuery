using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.LlmServices;

public class OllamaService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;

        _baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = _configuration["LlmSettings:Ollama:Model"] ?? "llama3";

        _httpClient.BaseAddress = new Uri(_baseUrl);
    }

    public async Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request)
{
    var systemPrompt = SystemPrompt.CreateSystemPrompt(request.DatabaseSchema, request.SchemaContext, request.DataSourceType);
    var userPrompt = CreateUserPrompt(request);

    // Include structure requirements in the prompt itself
    var structureInstructions = @"
Return your response in this exact JSON format:
{
  ""sqlQuery"": ""SELECT * FROM table;"",
  ""explanation"": ""Description of what the query does.""
}";

    var prompt = $"{systemPrompt}\n\n{structureInstructions}\n\n{userPrompt}";

    var requestData = new
    {
        model = _model,
        prompt,
        stream = false,
        temperature = 0.0,
        format = "json" // This just tells Ollama to return JSON, but doesn't define the structure
    };

    var response = await _httpClient.PostAsJsonAsync("api/generate", requestData);
    response.EnsureSuccessStatusCode();

    var responseObject = await response.Content.ReadFromJsonAsync<JsonElement>();
    var content = responseObject.GetProperty("response").GetString();
    
    try
    {
        // Direct JSON deserialization
        return JsonSerializer.Deserialize<LlmQueryResponse>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
    }
    catch (JsonException)
    {
        // Fallback to extract JSON from text if needed
        var jsonMatch = System.Text.RegularExpressions.Regex.Match(content, @"\{.*\}", 
            System.Text.RegularExpressions.RegexOptions.Singleline);
            
        if (jsonMatch.Success)
        {
            return JsonSerializer.Deserialize<LlmQueryResponse>(jsonMatch.Value, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        
        // If all else fails, create a default response
        return new LlmQueryResponse
        {
            SqlQuery = "",
            Explanation = $"Failed to parse JSON from LLM response: {content}"
        };
    }
}

//     private string CreateSystemPrompt(string databaseSchema, string schemaContext, string dataSourceType)
//     {
//         var queryLanguage = GetQueryLanguage(dataSourceType);
//     
//         return @$"
// You are an expert SQL query generator for PostgreSQL databases. Your task is to convert natural language questions into valid PostgreSQL queries.
//
// ### DATABASE SCHEMA:
// ```sql
// {databaseSchema}
// ADDITIONAL CONTEXT:
// {schemaContext}
// IMPORTANT RULES:
//
// Only generate SELECT queries - no INSERT, UPDATE, DELETE, or other modifying statements.
// Wrap your SQL query in triple backticks like this: sql [YOUR QUERY HERE] 
// Keep your explanation brief and separate from the SQL code.
// Use standard PostgreSQL syntax.
// If asked about errors, generate a query to help debug the issue.
// If the schema doesn't match what's in the database, generate a query to show available tables.
// Make the query as efficient as possible.
// Use proper JOINs when necessary.
//
// FORMAT YOUR RESPONSE LIKE THIS:
// sqlSELECT * FROM example_table WHERE condition = true;
// Explanation: Brief explanation of what this query does.
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
        Console.WriteLine($"Raw Ollama response: {content}");  // Debug logging
        
        // First try to parse as JSON
        if (content.Contains("{") && content.Contains("}"))
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(content, @"\{.*\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (jsonMatch.Success)
            {
                var jsonResponse = JsonSerializer.Deserialize<LlmQueryResponse>(jsonMatch.Value,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (jsonResponse != null && !string.IsNullOrWhiteSpace(jsonResponse.SqlQuery))
                {
                    return jsonResponse;
                }
            }
        }

        // Improved SQL extraction pattern
        var sqlPattern = $@"```(?:sql|{GetQueryLanguage(dataSourceType)})?\s*(.*?)\s*```";
        var sqlMatch = System.Text.RegularExpressions.Regex.Match(
            content, 
            sqlPattern,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        
        if (sqlMatch.Success)
        {
            var sql = sqlMatch.Groups[1].Value.Trim();
            
            // Extract any explanation text that might come after the SQL
            var explanationText = content;
            var sqlBlockIndex = content.IndexOf("```");
            if (sqlBlockIndex >= 0)
            {
                var endBlockIndex = content.IndexOf("```", sqlBlockIndex + 3);
                if (endBlockIndex >= 0 && endBlockIndex + 3 < content.Length)
                {
                    explanationText = content.Substring(endBlockIndex + 3).Trim();
                }
            }
            
            // If we still have explanation text in the SQL, clean it up
            if (sql.Contains("Explanation:"))
            {
                var parts = sql.Split(new[] { "Explanation:" }, StringSplitOptions.RemoveEmptyEntries);
                sql = parts[0].Trim();
                if (parts.Length > 1)
                {
                    explanationText = "Explanation: " + parts[1].Trim();
                }
            }

            return new LlmQueryResponse
            {
                SqlQuery = sql,
                Explanation = explanationText
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
        Console.WriteLine($"Error parsing Ollama response: {ex}");
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