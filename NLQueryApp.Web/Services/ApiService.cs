using System.Net.Http.Json;
using System.Text.Json;
using NLQueryApp.Core;

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
        try
        {
            Console.WriteLine($"API Base Address: {_httpClient.BaseAddress}");
            var conversations = await _httpClient.GetFromJsonAsync<List<Conversation>>("api/conversation");
            return conversations ?? new List<Conversation>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to connect to the API server. Please check if the server is running.", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting conversations: {ex.Message}");
            throw new Exception($"Failed to load conversations: {ex.Message}", ex);
        }
    }
    
    public async Task<Conversation> GetConversationAsync(int id)
    {
        try
        {
            var conversation = await _httpClient.GetFromJsonAsync<Conversation>($"api/conversation/{id}");
            return conversation ?? throw new Exception("Conversation not found or returned null");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            throw new Exception($"Conversation with ID {id} not found");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to connect to the API server: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting conversation: {ex.Message}");
            throw;
        }
    }
    
    public async Task<Conversation> CreateConversationAsync(string title)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "New Conversation";
            }
            
            var conversation = new Conversation { Title = title.Trim() };
            var response = await _httpClient.PostAsJsonAsync("api/conversation", conversation);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<Conversation>();
            return result ?? throw new Exception("Server returned null when creating conversation");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to create conversation: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while creating conversation. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating conversation: {ex.Message}");
            throw;
        }
    }
    
    public async Task<ChatMessage> AddMessageAsync(int conversationId, ChatMessage message)
    {
        try
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                throw new ArgumentException("Message content cannot be empty");
            }
            
            // Ensure message has proper values
            message.ConversationId = conversationId;
            message.Timestamp = DateTime.UtcNow;
            
            var response = await _httpClient.PostAsJsonAsync($"api/conversation/{conversationId}/messages", message);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<ChatMessage>();
            return result ?? throw new Exception("Server returned null when adding message");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to add message: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while adding message. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding message: {ex.Message}");
            throw;
        }
    }
    
    // Data Source methods
    public async Task<List<Core.Models.DataSourceDefinition>> GetDataSourcesAsync()
    {
        try
        {
            var dataSources = await _httpClient.GetFromJsonAsync<List<Core.Models.DataSourceDefinition>>("api/datasource");
            return dataSources ?? new List<Core.Models.DataSourceDefinition>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to connect to the API server: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while loading data sources. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting data sources: {ex.Message}");
            throw new Exception($"Failed to load data sources: {ex.Message}", ex);
        }
    }
    
    public async Task<Core.Models.DataSourceDefinition> GetDataSourceAsync(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            var dataSource = await _httpClient.GetFromJsonAsync<Core.Models.DataSourceDefinition>($"api/datasource/{id}");
            return dataSource ?? throw new Exception("Data source not found or returned null");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            throw new Exception($"Data source with ID '{id}' not found");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to connect to the API server: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting data source: {ex.Message}");
            throw;
        }
    }
    
    public async Task<Core.Models.DataSourceDefinition> CreateDataSourceAsync(Core.Models.DataSourceDefinition dataSource)
    {
        try
        {
            if (dataSource == null)
            {
                throw new ArgumentNullException(nameof(dataSource));
            }
            
            if (string.IsNullOrWhiteSpace(dataSource.Name))
            {
                throw new ArgumentException("Data source name is required");
            }
            
            var response = await _httpClient.PostAsJsonAsync("api/datasource", dataSource);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<Core.Models.DataSourceDefinition>();
            return result ?? throw new Exception("Server returned null when creating data source");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to create data source: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while creating data source. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating data source: {ex.Message}");
            throw;
        }
    }
    
    public async Task<Core.Models.DataSourceDefinition> UpdateDataSourceAsync(string id, Core.Models.DataSourceDefinition dataSource)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            if (dataSource == null)
            {
                throw new ArgumentNullException(nameof(dataSource));
            }
            
            if (string.IsNullOrWhiteSpace(dataSource.Name))
            {
                throw new ArgumentException("Data source name is required");
            }
            
            var response = await _httpClient.PutAsJsonAsync($"api/datasource/{id}", dataSource);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<Core.Models.DataSourceDefinition>();
            return result ?? throw new Exception("Server returned null when updating data source");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to update data source: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while updating data source. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating data source: {ex.Message}");
            throw;
        }
    }
    
    public async Task DeleteDataSourceAsync(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            var response = await _httpClient.DeleteAsync($"api/datasource/{id}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to delete data source: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while deleting data source. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting data source: {ex.Message}");
            throw;
        }
    }
    
    public async Task<bool> TestDataSourceConnectionAsync(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            var response = await _httpClient.GetFromJsonAsync<dynamic>($"api/datasource/{id}/test");
            
            // Handle different possible response formats
            if (response is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("success", out var successProp))
                {
                    return successProp.GetBoolean();
                }
            }
            
            return response?.success ?? false;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error testing connection: {ex.Message}");
            return false;
        }
    }
    
    public async Task<string> GetDataSourceContextAsync(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            var response = await _httpClient.GetFromJsonAsync<dynamic>($"api/datasource/{id}/context");
            
            // Handle different possible response formats
            if (response is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("context", out var contextProp))
                {
                    return contextProp.GetString() ?? "";
                }
            }
            
            return response?.context?.ToString() ?? "";
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            return "";
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting context: {ex.Message}");
            return "";
        }
    }
    
    public async Task SetDataSourceContextAsync(string id, string context)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Data source ID cannot be empty");
            }
            
            // Context can be empty/null - that's valid
            context ??= "";
            
            var response = await _httpClient.PutAsJsonAsync($"api/datasource/{id}/context", context);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to save context: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"Request timed out: {ex.Message}");
            throw new Exception("Request timed out while saving context. Please try again.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting context: {ex.Message}");
            throw;
        }
    }
    
    // Query methods
    public async Task<QueryResult> ProcessQueryAsync(string question, string dataSourceId, string? llmService = null)
    {
        try
        {
            var query = new { Question = question, DataSourceId = dataSourceId, LlmService = llmService };
            var response = await _httpClient.PostAsJsonAsync("api/query", query);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<QueryResult>() 
                   ?? throw new Exception("Failed to process query");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP request failed: {ex.Message}");
            throw new Exception($"Failed to process query: {ex.Message}");
        }
    }
    
    // LLM Service methods - FIXED parsing
    public async Task<List<(string Name, string DisplayName, bool IsAvailable)>> GetAvailableLlmServicesAsync()
    {
        try
        {
            Console.WriteLine("Fetching LLM services from API...");
            
            var response = await _httpClient.GetAsync("api/llm/services");
            response.EnsureSuccessStatusCode();
            
            var jsonString = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Raw API response: {jsonString}");
            
            // Parse the response properly using System.Text.Json
            using var document = JsonDocument.Parse(jsonString);
            var services = new List<(string Name, string DisplayName, bool IsAvailable)>();
            
            foreach (var element in document.RootElement.EnumerateArray())
            {
                string name = "";
                string displayName = "";
                bool isAvailable = false;
                
                if (element.TryGetProperty("name", out var nameProp))
                    name = nameProp.GetString() ?? "";
                
                if (element.TryGetProperty("displayName", out var displayNameProp))
                    displayName = displayNameProp.GetString() ?? "";
                    
                if (element.TryGetProperty("isAvailable", out var isAvailableProp))
                    isAvailable = isAvailableProp.GetBoolean();
                
                Console.WriteLine($"Parsed service: {name} - {displayName} - Available: {isAvailable}");
                
                services.Add((name, displayName, isAvailable));
            }
            
            Console.WriteLine($"Total parsed services: {services.Count}");
            return services;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting LLM services: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            
            // Return default services if API call fails
            return new List<(string Name, string DisplayName, bool IsAvailable)>
            {
                ("ollama", "Ollama (Local)", true),
                ("anthropic", "Anthropic Claude", false),
                ("openai", "OpenAI GPT-4", false),
                ("gemini", "Google Gemini", false)
            };
        }
    }
}