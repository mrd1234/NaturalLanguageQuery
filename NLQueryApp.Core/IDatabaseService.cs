namespace NLQueryApp.Core;

public interface IDatabaseService
{
    // Database initialization - only for app's own database
    Task InitializeDatabaseAsync();
    
    // Conversation management - app-specific functionality
    Task<List<Conversation>> GetConversationsAsync();
    Task<Conversation> GetConversationAsync(int id);
    Task<Conversation> CreateConversationAsync(string title, string? dataSourceId = null);
    Task<ChatMessage> AddMessageAsync(int conversationId, ChatMessage message);
    Task<bool> UpdateConversationTitleAsync(int conversationId, string title);
    
    // Generic utility methods (if needed)
    Task<List<string>> GetAvailableSchemasAsync();
    Task<List<string>> GetTablesInSchemaAsync(string schema);
}
