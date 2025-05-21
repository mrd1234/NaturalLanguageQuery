using System.Text;
using Microsoft.Extensions.Configuration;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using Npgsql;

namespace NLQueryApp.Data.Services;

public class DatabaseService(IConfiguration configuration) : IDatabaseService
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") 
                                                ?? throw new ArgumentNullException(nameof(configuration));

    public async Task<string> GetDatabaseSchemaAsync()
    {
        var extractorMode = configuration["DatabaseSettings:SchemaExtractorMode"] ?? "Dynamic";
        
        if (extractorMode.Equals("Static", StringComparison.OrdinalIgnoreCase))
        {
            var schemaPath = configuration["DatabaseSettings:StaticSchemaPath"];
            if (File.Exists(schemaPath))
            {
                return await File.ReadAllTextAsync(schemaPath);
            }
            // Fall back to dynamic if file doesn't exist
        }
        
        // Extract schema dynamically from the database
        var includedSchemasSection = configuration.GetSection("DatabaseSettings:IncludedSchemas");
        var includedSchemas = new[] {"team_movements", "lookup", "app"};
        if (includedSchemasSection.Exists())
        {
            var schemasList = new List<string>();
            includedSchemasSection.Bind(schemasList);
            if (schemasList.Count > 0)
            {
                includedSchemas = schemasList.ToArray();
            }
        }
        
        var excludedTablesSection = configuration.GetSection("DatabaseSettings:ExcludedTables");
        var excludedTables = Array.Empty<string>();
        if (!excludedTablesSection.Exists()) return await ExtractDatabaseSchemaAsync(includedSchemas, excludedTables);
        var tablesList = new List<string>();
        excludedTablesSection.Bind(tablesList);
        if (tablesList.Count > 0)
        {
            excludedTables = tablesList.ToArray();
        }

        return await ExtractDatabaseSchemaAsync(includedSchemas, excludedTables);
    }
    
    public async Task<string> GetSchemaContextAsync(string schemaName)
    {
        var contextPath = configuration[$"DatabaseSettings:SchemaContextFiles:{schemaName}"] 
                          ?? $"SchemaContext/{schemaName}_context.md";
    
        if (File.Exists(contextPath))
        {
            return await File.ReadAllTextAsync(contextPath);
        }
    
        return "No additional context available for this schema.";
    }

    private async Task<string> ExtractDatabaseSchemaAsync(string[] includedSchemas, string[] excludedTables)
{
    var schemaInfo = new StringBuilder();

    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();
    
    // Query for table information
    var schemasClause = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
    var excludesClause = excludedTables.Length > 0 
        ? $"AND table_name NOT IN ({string.Join(",", excludedTables.Select(t => $"'{t}'"))})" 
        : "";

    Dictionary<string, List<(string tableName, string columnName, string dataType, string isNullable, string defaultValue, string maxLength)>> schemaData = 
        new Dictionary<string, List<(string, string, string, string, string, string)>>();

    await using var tableCommand = new NpgsqlCommand($@"
        SELECT 
            table_schema,
            table_name, 
            column_name, 
            data_type, 
            is_nullable,
            column_default,
            character_maximum_length
        FROM 
            information_schema.columns 
        WHERE 
            table_schema IN ({schemasClause}) {excludesClause}
        ORDER BY 
            table_schema, table_name, ordinal_position", connection);

    await using var reader = await tableCommand.ExecuteReaderAsync();
    
    // Collect all data first
    while (await reader.ReadAsync())
    {
        var schema = reader.GetString(0);
        var tableName = reader.GetString(1);
        var columnName = reader.GetString(2);
        var dataType = reader.GetString(3);
        var isNullable = reader.GetString(4) == "YES" ? "NULL" : "NOT NULL";
        var defaultValue = reader.IsDBNull(5) ? "" : $" DEFAULT {reader.GetString(5)}";
        var maxLength = reader.IsDBNull(6) ? "" : $"({reader.GetInt32(6)})";
        
        if (!schemaData.ContainsKey(schema))
        {
            schemaData[schema] = new List<(string, string, string, string, string, string)>();
        }
        
        schemaData[schema].Add((tableName, columnName, dataType, isNullable, defaultValue, maxLength));
    }
    
    // Close the reader before proceeding
    await reader.CloseAsync();
    
    // Now format the collected data into SQL
    var currentSchema = "";
    var currentTable = "";
    
    foreach (var schema in schemaData.Keys.OrderBy(k => k))
    {
        if (schema != currentSchema)
        {
            if (!string.IsNullOrEmpty(currentSchema) && !string.IsNullOrEmpty(currentTable))
            {
                schemaInfo.AppendLine(");");
                schemaInfo.AppendLine();
            }
            
            currentSchema = schema;
            currentTable = "";
            schemaInfo.AppendLine($"-- Schema: {schema}");
            schemaInfo.AppendLine();
        }
        
        var tableGroups = schemaData[schema].GroupBy(x => x.tableName);
        
        foreach (var tableGroup in tableGroups)
        {
            var tableName = tableGroup.Key;
            var fullTableName = $"{schema}.{tableName}";

            if (fullTableName == currentTable) continue;
            if (!string.IsNullOrEmpty(currentTable))
            {
                schemaInfo.AppendLine(");");
                schemaInfo.AppendLine();
            }
                
            currentTable = fullTableName;
            schemaInfo.AppendLine($"CREATE TABLE {fullTableName} (");
                
            var first = true;
            foreach (var column in tableGroup)
            {
                if (!first)
                {
                    schemaInfo.AppendLine(",");
                }
                first = false;
                    
                schemaInfo.Append($"    {column.columnName} {column.dataType}{column.maxLength} {column.isNullable}{column.defaultValue}");
            }
                
            schemaInfo.AppendLine();
            schemaInfo.AppendLine(");");
        }
    }
    
    // Now get keys information (with the reader properly closed)
    await ExtractKeysInformation(connection, includedSchemas, excludedTables, schemaInfo);
    
    return schemaInfo.ToString();
}

    private async Task ExtractKeysInformation(NpgsqlConnection connection, string[] includedSchemas, 
                                              string[] excludedTables, StringBuilder schemaInfo)
    {
        var schemasClause = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
        var excludesClause = excludedTables.Length > 0 
            ? $"AND tc.table_name NOT IN ({string.Join(",", excludedTables.Select(t => $"'{t}'"))})" 
            : "";
        
        // Get primary keys
        schemaInfo.AppendLine();
        schemaInfo.AppendLine("-- Primary Keys");

        await using var pkCommand = new NpgsqlCommand($@"
            SELECT
                tc.table_schema, 
                tc.table_name, 
                kc.column_name 
            FROM 
                information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kc 
                  ON tc.constraint_name = kc.constraint_name 
                  AND tc.table_schema = kc.table_schema
            WHERE 
                tc.constraint_type = 'PRIMARY KEY' 
                AND tc.table_schema IN ({schemasClause}) {excludesClause}
            ORDER BY 
                tc.table_schema,
                tc.table_name, 
                kc.ordinal_position", connection);

        try
        {
            await using var pkReader = await pkCommand.ExecuteReaderAsync();
        
            while (await pkReader.ReadAsync())
            {
                var schema = pkReader.GetString(0);
                var tableName = pkReader.GetString(1);
                var columnName = pkReader.GetString(2);
            
                schemaInfo.AppendLine($"ALTER TABLE {schema}.{tableName} ADD PRIMARY KEY ({columnName});");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
        
        // Get foreign keys
        schemaInfo.AppendLine();
        schemaInfo.AppendLine("-- Foreign Keys");

        await using var fkCommand = new NpgsqlCommand($@"
            SELECT
                tc.table_schema, 
                tc.table_name, 
                kc.column_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name,
                ccu.column_name AS foreign_column_name 
            FROM 
                information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kc 
                  ON tc.constraint_name = kc.constraint_name 
                  AND tc.table_schema = kc.table_schema
                JOIN information_schema.constraint_column_usage ccu 
                  ON ccu.constraint_name = tc.constraint_name 
                  AND ccu.table_schema = tc.table_schema
            WHERE 
                tc.constraint_type = 'FOREIGN KEY' 
                AND tc.table_schema IN ({schemasClause}) {excludesClause}
            ORDER BY 
                tc.table_schema,
                tc.table_name", connection);

        await using var fkReader = await fkCommand.ExecuteReaderAsync();
        
        while (await fkReader.ReadAsync())
        {
            var schema = fkReader.GetString(0);
            var tableName = fkReader.GetString(1);
            var columnName = fkReader.GetString(2);
            var foreignSchema = fkReader.GetString(3);
            var foreignTable = fkReader.GetString(4);
            var foreignColumn = fkReader.GetString(5);
            
            schemaInfo.AppendLine($"ALTER TABLE {schema}.{tableName} ADD FOREIGN KEY ({columnName}) REFERENCES {foreignSchema}.{foreignTable}({foreignColumn});");
        }
    }

    public async Task<QueryResult> ExecuteSqlQueryAsync(string sqlQuery)
    {
        var result = new QueryResult { SqlQuery = sqlQuery };
        
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sqlQuery, connection);
            
            // Only allow SELECT statements for security
            if (!sqlQuery.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.ErrorMessage = "Only SELECT queries are allowed";
                return result;
            }

            await using var reader = await command.ExecuteReaderAsync();
            result.Data = new List<Dictionary<string, object>>();
            
            while (await reader.ReadAsync())
            {
                var rowData = new Dictionary<string, object>();
                
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rowData[columnName] = value ?? DBNull.Value;
                }
                
                result.Data.Add(rowData);
            }
            
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        
        return result;
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
                    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS app.messages (
                    id SERIAL PRIMARY KEY,
                    conversation_id INTEGER NOT NULL REFERENCES app.conversations(id) ON DELETE CASCADE,
                    role VARCHAR(50) NOT NULL,
                    content TEXT NOT NULL,
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
    
    public async Task SetupTeamMovementsSchemaAsync(bool dropIfExists = false)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        if (dropIfExists)
        {
            // Drop existing tables in reverse order of dependency
            await using var dropTablesCommand = new NpgsqlCommand(@"
                DROP TABLE IF EXISTS team_movements.shift_break;
                DROP TABLE IF EXISTS team_movements.contract_shift;
                DROP TABLE IF EXISTS team_movements.contract_week;
                DROP TABLE IF EXISTS team_movements.contract_mutual_flag;
                DROP TABLE IF EXISTS team_movements.contract;
                DROP TABLE IF EXISTS team_movements.job_info;
                DROP TABLE IF EXISTS team_movements.movement_history;
                DROP TABLE IF EXISTS team_movements.participant;
                DROP TABLE IF EXISTS team_movements.movement_tag;
                DROP TABLE IF EXISTS team_movements.movement;
                DROP TABLE IF EXISTS team_movements.employee;
                DROP TABLE IF EXISTS team_movements.position;
                
                DROP TABLE IF EXISTS lookup.history_event_type;
                DROP TABLE IF EXISTS lookup.break_type;
                DROP TABLE IF EXISTS lookup.mutual_flag;
                DROP TABLE IF EXISTS lookup.job_role;
                DROP TABLE IF EXISTS lookup.role_type;
                DROP TABLE IF EXISTS lookup.cost_centre;
                DROP TABLE IF EXISTS lookup.department;
                DROP TABLE IF EXISTS lookup.brand;
                DROP TABLE IF EXISTS lookup.banner;
                DROP TABLE IF EXISTS lookup.employee_subgroup;
                DROP TABLE IF EXISTS lookup.employee_group;
                DROP TABLE IF EXISTS lookup.movement_type;
                DROP TABLE IF EXISTS lookup.movement_status;
            ", connection);
            
            await dropTablesCommand.ExecuteNonQueryAsync();
        }
        
        // Read SQL from file
        var schemaScript = await File.ReadAllTextAsync("schema.sql");

        await using var createTablesCommand = new NpgsqlCommand(schemaScript, connection);
        await createTablesCommand.ExecuteNonQueryAsync();
        
        // Insert default lookup values
        await InsertLookupValues(connection);
    }
    
    private async Task InsertLookupValues(NpgsqlConnection connection)
    {
        // Insert movement statuses
        await using var statusCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.movement_status (status_name)
            VALUES ('Completed'), ('Expired'), ('Rejected')
            ON CONFLICT (status_name) DO NOTHING;
        ", connection);
        
        await statusCommand.ExecuteNonQueryAsync();
        
        // Insert movement types
        await using var typeCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.movement_type (type_name)
            VALUES 
                ('ContractAndPositionPermanent'), 
                ('ContractAndPositionSecondment'),
                ('ContractTemporary'),
                ('ContractPermanent')
            ON CONFLICT (type_name) DO NOTHING;
        ", connection);
        
        await typeCommand.ExecuteNonQueryAsync();
        
        // Insert employee groups
        await using var groupCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.employee_group (group_name)
            VALUES ('FullTime'), ('PartTime'), ('Casual')
            ON CONFLICT (group_name) DO NOTHING;
        ", connection);
        
        await groupCommand.ExecuteNonQueryAsync();
        
        // Insert employee subgroups
        await using var subgroupCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.employee_subgroup (subgroup_name)
            VALUES ('Salaried'), ('EnterpriseAgreement'), ('Unknown')
            ON CONFLICT (subgroup_name) DO NOTHING;
        ", connection);
        
        await subgroupCommand.ExecuteNonQueryAsync();
        
        // Insert banners
        await using var bannerCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.banner (banner_name)
            VALUES ('FoodGroup'), ('BigW'), ('ProactiveServices'), ('Unknown')
            ON CONFLICT (banner_name) DO NOTHING;
        ", connection);
        
        await bannerCommand.ExecuteNonQueryAsync();
        
        // Insert role types
        await using var roleCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.role_type (role_name)
            VALUES 
                ('TeamMember'), 
                ('SendingManager'), 
                ('ReceivingManager'),
                ('AdditionalReceivingManager'),
                ('CulturePeoplePartner'),
                ('HigherCulturePeoplePartner'),
                ('HigherSendingManager')
            ON CONFLICT (role_name) DO NOTHING;
        ", connection);
        
        await roleCommand.ExecuteNonQueryAsync();
        
        // Insert mutual flags
        await using var flagCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.mutual_flag (flag_name)
            VALUES 
                ('OutsideOrdinaryHours'), 
                ('NonConsecutiveDaysOff'),
                ('NonConsecutiveWeekendDaysOff'),
                ('RegularlyWorkingSundays'),
                ('TwentyDaysOverFourWeeks'),
                ('LessThan12hrsRestPeriod')
            ON CONFLICT (flag_name) DO NOTHING;
        ", connection);
        
        await flagCommand.ExecuteNonQueryAsync();
        
        // Insert break types
        await using var breakCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.break_type (break_name)
            VALUES 
                ('Unpaid30Mins'), 
                ('Unpaid60Mins'),
                ('Paid30Mins')
            ON CONFLICT (break_name) DO NOTHING;
        ", connection);
        
        await breakCommand.ExecuteNonQueryAsync();
        
        // Insert history event types
        await using var eventCommand = new NpgsqlCommand(@"
            INSERT INTO lookup.history_event_type (event_type_name)
            VALUES 
                ('MovementInitiated'), 
                ('MovementEdited'),
                ('MovementApproved'),
                ('MovementRejected'),
                ('MovementExpired'),
                ('MovementCompleted'),
                ('SuccessFactorsWorkflowDetected'),
                ('SuccessFactorsWorkflowCompleted'),
                ('SuccessFactorsWorkflowCancelled'),
                ('LetterOfOfferGenerated'),
                ('LetterOfOfferPurged'),
                ('PayrollContractCreated'),
                ('KronosContractCreated'),
                ('BotSubmitted'),
                ('BotResult')
            ON CONFLICT (event_type_name) DO NOTHING;
        ", connection);
        
        await eventCommand.ExecuteNonQueryAsync();
    }

    public async Task<List<Conversation>> GetConversationsAsync()
    {
        var conversations = new List<Conversation>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT id, title, created_at, updated_at FROM app.conversations ORDER BY updated_at DESC", 
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            conversations.Add(new Conversation
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2),
                UpdatedAt = reader.GetDateTime(3)
            });
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
                "SELECT id, title, created_at, updated_at FROM app.conversations WHERE id = @id",
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
                CreatedAt = conversationReader.GetDateTime(2),
                UpdatedAt = conversationReader.GetDateTime(3),
                Messages = new List<ChatMessage>()
            };
        }
        
        // Second query to get messages using a new connection
        await using (var connection2 = new NpgsqlConnection(_connectionString))
        {
            await connection2.OpenAsync();
            
            await using var messagesCommand = new NpgsqlCommand(
                "SELECT id, role, content, timestamp FROM app.messages WHERE conversation_id = @conversationId ORDER BY timestamp",
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
                    Timestamp = messagesReader.GetDateTime(3),
                    ConversationId = id
                });
            }
        }
        
        return conversation;
    }
    
    public async Task<Conversation> CreateConversationAsync(string title)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            @"INSERT INTO app.conversations (title, created_at, updated_at) 
              VALUES (@title, @createdAt, @updatedAt) 
              RETURNING id, title, created_at, updated_at",
            connection);
        
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("@title", title);
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
            CreatedAt = reader.GetDateTime(2),
            UpdatedAt = reader.GetDateTime(3)
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
                @"INSERT INTO app.messages (role, content, timestamp, conversation_id) 
                  VALUES (@role, @content, @timestamp, @conversationId) 
                  RETURNING id",
                connection, transaction as NpgsqlTransaction);
            
            messageCommand.Parameters.AddWithValue("@role", message.Role);
            messageCommand.Parameters.AddWithValue("@content", message.Content);
            messageCommand.Parameters.AddWithValue("@timestamp", DateTime.UtcNow);
            messageCommand.Parameters.AddWithValue("@conversationId", conversationId);
            
            var messageId = (int)await messageCommand.ExecuteScalarAsync();
            
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