using System.Net.Http.Json;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Web.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Conversation methods
    public async Task<List<Conversation>> GetConversationsAsync()
    {
        Console.WriteLine($"API Base Address: {_httpClient.BaseAddress}");
        return await _httpClient.GetFromJsonAsync<List<Conversation>>("api/conversation") ?? new List<Conversation>();
    }
    
    public async Task<Conversation> GetConversationAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<Conversation>($"api/conversation/{id}") 
               ?? throw new Exception("Conversation not found");
    }
    
    public async Task<Conversation> CreateConversationAsync(string title)
    {
        var conversation = new Conversation { Title = title };
        var response = await _httpClient.PostAsJsonAsync("api/conversation", conversation);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<Conversation>() 
               ?? throw new Exception("Failed to create conversation");
    }
    
    public async Task<ChatMessage> AddMessageAsync(int conversationId, ChatMessage message)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/conversation/{conversationId}/messages", message);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<ChatMessage>() 
               ?? throw new Exception("Failed to add message");
    }
    
    // Data Source methods
    public async Task<List<DataSourceDefinition>> GetDataSourcesAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<DataSourceDefinition>>("api/datasource") ?? new List<DataSourceDefinition>();
    }
    
    public async Task<DataSourceDefinition> GetDataSourceAsync(string id)
    {
        return await _httpClient.GetFromJsonAsync<DataSourceDefinition>($"api/datasource/{id}") 
               ?? throw new Exception("Data source not found");
    }
    
    public async Task<DataSourceDefinition> CreateDataSourceAsync(DataSourceDefinition dataSource)
    {
        var response = await _httpClient.PostAsJsonAsync("api/datasource", dataSource);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<DataSourceDefinition>() 
               ?? throw new Exception("Failed to create data source");
    }
    
    public async Task<DataSourceDefinition> UpdateDataSourceAsync(string id, DataSourceDefinition dataSource)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/datasource/{id}", dataSource);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<DataSourceDefinition>() 
               ?? throw new Exception("Failed to update data source");
    }
    
    public async Task DeleteDataSourceAsync(string id)
    {
        var response = await _httpClient.DeleteAsync($"api/datasource/{id}");
        response.EnsureSuccessStatusCode();
    }
    
    public async Task<bool> TestDataSourceConnectionAsync(string id)
    {
        var response = await _httpClient.GetFromJsonAsync<dynamic>($"api/datasource/{id}/test");
        return response?.success ?? false;
    }
    
    public async Task<string> GetDataSourceContextAsync(string id)
    {
        var response = await _httpClient.GetFromJsonAsync<dynamic>($"api/datasource/{id}/context");
        return response?.context?.ToString() ?? "";
    }
    
    public async Task SetDataSourceContextAsync(string id, string context)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/datasource/{id}/context", context);
        response.EnsureSuccessStatusCode();
    }
    
    // Query methods
    public async Task<QueryResult> ProcessQueryAsync(string question, string dataSourceId, string? llmService = null)
    {
        var query = new { Question = question, DataSourceId = dataSourceId, LlmService = llmService };
        var response = await _httpClient.PostAsJsonAsync("api/query", query);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<QueryResult>() 
               ?? throw new Exception("Failed to process query");
    }
}