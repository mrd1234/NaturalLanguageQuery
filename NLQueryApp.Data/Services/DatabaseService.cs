using Microsoft.Extensions.Configuration;
using NLQueryApp.Core;
using Npgsql;

namespace NLQueryApp.Data.Services;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;

    public DatabaseService(IConfiguration configuration)
    {
        _configuration = configuration;
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");
        }
        
        _connectionString = connectionString;
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            // Use using statement to ensure connection is disposed
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
        
            // Create schemas
            await using var schemaCommand = new NpgsqlCommand(@"
                CREATE SCHEMA IF NOT EXISTS app;
            ", connection);
        
            await schemaCommand.ExecuteNonQueryAsync();
        
            // Create app tables with proper transaction management
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using var appTablesCommand = new NpgsqlCommand(@"
                    -- App tables for the chat application
                    CREATE TABLE IF NOT EXISTS app.conversations (
                        id SERIAL PRIMARY KEY,
                        title VARCHAR(100) NOT NULL,
                        data_source_id VARCHAR(50),
                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE TABLE IF NOT EXISTS app.messages (
                        id SERIAL PRIMARY KEY,
                        conversation_id INTEGER NOT NULL REFERENCES app.conversations(id) ON DELETE CASCADE,
                        role VARCHAR(50) NOT NULL,
                        content TEXT NOT NULL,
                        data_source_id VARCHAR(50),
                        sql_query TEXT,
                        query_success BOOLEAN,
                        timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                    
                    -- Data source tables
                    CREATE TABLE IF NOT EXISTS app.data_sources (
                        id VARCHAR(50) PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        description TEXT,
                        source_type VARCHAR(50) NOT NULL,
                        connection_parameters JSONB NOT NULL,
                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                    
                    CREATE TABLE IF NOT EXISTS app.data_source_contexts (
                        data_source_id VARCHAR(50) REFERENCES app.data_sources(id) ON DELETE CASCADE,
                        context_text TEXT NOT NULL,
                        updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (data_source_id)
                    );
                    
                    -- Add foreign key constraints if they don't exist
                    DO $$ 
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM information_schema.table_constraints 
                            WHERE constraint_name = 'fk_conversation_data_source'
                        ) THEN
                            ALTER TABLE app.conversations 
                            ADD CONSTRAINT fk_conversation_data_source 
                            FOREIGN KEY (data_source_id) REFERENCES app.data_sources(id) ON DELETE SET NULL;
                        END IF;
                        
                        IF NOT EXISTS (
                            SELECT 1 FROM information_schema.table_constraints 
                            WHERE constraint_name = 'fk_message_data_source'
                        ) THEN
                            ALTER TABLE app.messages 
                            ADD CONSTRAINT fk_message_data_source 
                            FOREIGN KEY (data_source_id) REFERENCES app.data_sources(id) ON DELETE SET NULL;
                        END IF;
                    END $$;
                ", connection, transaction as NpgsqlTransaction);
            
                await appTablesCommand.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error creating database tables: {ex.Message}");
                throw;
            }
        
            Console.WriteLine("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing database: {ex.Message}");
            throw;
        }
    }

    public async Task<List<Conversation>> GetConversationsAsync()
    {
        var conversations = new List<Conversation>();

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(@"
                SELECT 
                    c.id, 
                    c.title, 
                    c.data_source_id,
                    c.created_at, 
                    c.updated_at
                FROM app.conversations c
                ORDER BY c.updated_at DESC", 
                connection);

            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var conversation = new Conversation
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    DataSourceId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt = reader.GetDateTime(3),
                    UpdatedAt = reader.GetDateTime(4),
                    Messages = new List<ChatMessage>() // Empty list, not placeholder messages
                };
                
                conversations.Add(conversation);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetConversationsAsync: {ex.Message}");
            throw;
        }
        
        return conversations;
    }
    
    public async Task<Conversation> GetConversationAsync(int id)
    {
        Conversation conversation;
        
        // First query to get conversation details
        await using (var connection1 = new NpgsqlConnection(_connectionString))
        {
            await connection1.OpenAsync();
            
            await using var conversationCommand = new NpgsqlCommand(
                "SELECT id, title, data_source_id, created_at, updated_at FROM app.conversations WHERE id = @id",
                connection1);
            
            conversationCommand.Parameters.AddWithValue("@id", id);
            
            await using var conversationReader = await conversationCommand.ExecuteReaderAsync();
            
            if (!await conversationReader.ReadAsync())
            {
                throw new Exception($"Conversation with ID {id} not found");
            }
            
            conversation = new Conversation
            {
                Id = conversationReader.GetInt32(0),
                Title = conversationReader.GetString(1),
                DataSourceId = conversationReader.IsDBNull(2) ? null : conversationReader.GetString(2),
                CreatedAt = conversationReader.GetDateTime(3),
                UpdatedAt = conversationReader.GetDateTime(4),
                Messages = new List<ChatMessage>()
            };
        }
        
        // Second query to get messages using a new connection
        await using (var connection2 = new NpgsqlConnection(_connectionString))
        {
            await connection2.OpenAsync();
            
            await using var messagesCommand = new NpgsqlCommand(
                @"SELECT id, role, content, data_source_id, sql_query, query_success, timestamp 
                  FROM app.messages 
                  WHERE conversation_id = @conversationId 
                  ORDER BY timestamp",
                connection2);
            
            messagesCommand.Parameters.AddWithValue("@conversationId", id);
            
            await using var messagesReader = await messagesCommand.ExecuteReaderAsync();
            
            while (await messagesReader.ReadAsync())
            {
                conversation.Messages.Add(new ChatMessage
                {
                    Id = messagesReader.GetInt32(0),
                    Role = messagesReader.GetString(1),
                    Content = messagesReader.GetString(2),
                    DataSourceId = messagesReader.IsDBNull(3) ? null : messagesReader.GetString(3),
                    SqlQuery = messagesReader.IsDBNull(4) ? null : messagesReader.GetString(4),
                    QuerySuccess = messagesReader.IsDBNull(5) ? null : (bool?)messagesReader.GetBoolean(5),
                    Timestamp = messagesReader.GetDateTime(6),
                    ConversationId = id
                });
            }
        }
        
        return conversation;
    }
    
    public async Task<Conversation> CreateConversationAsync(string title, string? dataSourceId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            @"INSERT INTO app.conversations (title, data_source_id, created_at, updated_at) 
              VALUES (@title, @dataSourceId, @createdAt, @updatedAt) 
              RETURNING id, title, data_source_id, created_at, updated_at",
            connection);
        
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@dataSourceId", (object?)dataSourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", now);
        command.Parameters.AddWithValue("@updatedAt", now);

        await using var reader = await command.ExecuteReaderAsync();
        
        if (!await reader.ReadAsync())
        {
            throw new Exception("Failed to create conversation");
        }
        
        return new Conversation
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            DataSourceId = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt = reader.GetDateTime(3),
            UpdatedAt = reader.GetDateTime(4),
            Messages = new List<ChatMessage>()
        };
    }
    
    public async Task<ChatMessage> AddMessageAsync(int conversationId, ChatMessage message)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        // Start a transaction to ensure both operations complete
        await using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            // Insert the message
            await using var messageCommand = new NpgsqlCommand(
                @"INSERT INTO app.messages (role, content, data_source_id, sql_query, query_success, timestamp, conversation_id) 
                  VALUES (@role, @content, @dataSourceId, @sqlQuery, @querySuccess, @timestamp, @conversationId) 
                  RETURNING id",
                connection, transaction as NpgsqlTransaction);
            
            messageCommand.Parameters.AddWithValue("@role", message.Role);
            messageCommand.Parameters.AddWithValue("@content", message.Content);
            messageCommand.Parameters.AddWithValue("@dataSourceId", (object?)message.DataSourceId ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("@sqlQuery", (object?)message.SqlQuery ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("@querySuccess", (object?)message.QuerySuccess ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("@timestamp", DateTime.UtcNow);
            messageCommand.Parameters.AddWithValue("@conversationId", conversationId);
            
            var messageIdResult = await messageCommand.ExecuteScalarAsync();
            var messageId = messageIdResult != null ? Convert.ToInt32(messageIdResult) : 0;
            
            // Update the conversation's updated_at timestamp
            await using var updateCommand = new NpgsqlCommand(
                "UPDATE app.conversations SET updated_at = @updatedAt WHERE id = @id",
                connection, transaction as NpgsqlTransaction);
            
            updateCommand.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            updateCommand.Parameters.AddWithValue("@id", conversationId);
            
            await updateCommand.ExecuteNonQueryAsync();
            
            // Commit the transaction
            await transaction.CommitAsync();
            
            // Return the created message
            message.Id = messageId;
            message.ConversationId = conversationId;
            return message;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    
    public async Task<bool> UpdateConversationTitleAsync(int conversationId, string title)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                @"UPDATE app.conversations 
                  SET title = @title, updated_at = @updatedAt 
                  WHERE id = @id",
                connection);
            
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("@id", conversationId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating conversation title: {ex.Message}");
            return false;
        }
    }
    
    public async Task<List<string>> GetAvailableSchemasAsync()
    {
        var schemas = new List<string>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            @"SELECT schema_name 
              FROM information_schema.schemata 
              WHERE schema_name NOT IN ('pg_catalog', 'information_schema', 'pg_toast', 'pg_temp_1', 'pg_toast_temp_1') 
              ORDER BY schema_name", 
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            schemas.Add(reader.GetString(0));
        }
        
        return schemas;
    }
    
    public async Task<List<string>> GetTablesInSchemaAsync(string schema)
    {
        var tables = new List<string>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            @"SELECT table_name 
              FROM information_schema.tables 
              WHERE table_schema = @schema 
              ORDER BY table_name", 
            connection);
        
        command.Parameters.AddWithValue("@schema", schema);

        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        
        return tables;
    }
}
