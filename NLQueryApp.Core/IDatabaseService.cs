namespace NLQueryApp.Core;

public interface IDatabaseService
{
    // Schema and query execution
    Task<string> GetDatabaseSchemaAsync();
    Task<string> GetSchemaContextAsync(string schemaName);
    Task<QueryResult> ExecuteSqlQueryAsync(string sqlQuery);
    
    // Database initialization
    Task InitializeDatabaseAsync();
    
    // Conversation management
    Task<List<Conversation>> GetConversationsAsync();
    Task<Conversation> GetConversationAsync(int id);
    Task<Conversation> CreateConversationAsync(string title);
    Task<ChatMessage> AddMessageAsync(int conversationId, ChatMessage message);
    Task<bool> UpdateConversationTitleAsync(int conversationId, string title);
    
    // Schema management (for flexibility)
    Task<List<string>> GetAvailableSchemasAsync();
    Task<List<string>> GetTablesInSchemaAsync(string schema);
    
    // Database setup for specific schemas
    Task SetupTeamMovementsSchemaAsync(bool dropIfExists = false);
}