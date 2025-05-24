using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using Npgsql;

namespace NLQueryApp.Data.Providers;

public class PostgresDataSourceProvider : BaseDataSourceProvider
{
    private static readonly Regex DangerousSqlPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PostgresDataSourceProvider(ILogger<PostgresDataSourceProvider> logger)
        : base(logger)
    {
    }

    public override string ProviderType => "postgres";

    public override async Task<string> GetSchemaAsync(DataSourceDefinition dataSource)
    {
        var connectionString = dataSource.GetConnectionString();
        var schemaInfo = new StringBuilder();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        
        var includedSchemas = GetIncludedSchemas(dataSource);
        var excludedTables = GetExcludedTables(dataSource);
        
        _logger.LogDebug("Extracting schema for schemas: {Schemas}", string.Join(", ", includedSchemas));
        
        await ExtractSchemaInfo(connection, includedSchemas, excludedTables, schemaInfo);
        await ExtractKeysInformation(connection, includedSchemas, excludedTables, schemaInfo);
        
        return schemaInfo.ToString();
    }

    public override async Task<DatabaseInfo?> GetDatabaseInfoAsync(DataSourceDefinition dataSource)
    {
        try
        {
            var connectionString = dataSource.GetConnectionString();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
        
            await using var command = new NpgsqlCommand("SELECT version()", connection);
            var versionString = await command.ExecuteScalarAsync() as string ?? "";
        
            // Parse version from string like "PostgreSQL 14.2 on x86_64-pc-linux-gnu..."
            var match = Regex.Match(versionString, @"PostgreSQL (\d+)\.(\d+)");
            var version = "Unknown";
            var parsedVersion = new Version();
        
            if (match.Success && match.Groups.Count >= 3)
            {
                version = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
                if (Version.TryParse(version, out var parsed))
                    parsedVersion = parsed;
            }
        
            return new DatabaseInfo
            {
                Type = "postgres",
                Version = version,
                ParsedVersion = parsedVersion,
                FullVersionString = versionString
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get database version info for PostgreSQL data source {DataSourceId}", dataSource.Id);
            return null;
        }
    }

    public override async Task<string> GetDialectNotesAsync(DataSourceDefinition dataSource)
    {
        try
        {
            var databaseInfo = await GetDatabaseInfoAsync(dataSource);
            var version = databaseInfo?.ParsedVersion ?? new Version();
            
            // Check for specific features by querying the database
            var connectionString = dataSource.GetConnectionString();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            
            var features = await DetectPostgresFeatures(connection, version);
            
            var notes = new StringBuilder();
            notes.AppendLine("PostgreSQL-specific notes:");
            
            // JSON support
            if (features.HasJsonb)
                notes.AppendLine("- JSONB support available with operators like @>, ?, ?&, ?|");
            else if (features.HasJson)
                notes.AppendLine("- Basic JSON support available (use JSONB functions with caution)");
            
            // Window functions
            if (features.HasWindowFunctions)
                notes.AppendLine("- Window functions supported (ROW_NUMBER(), RANK(), LAG(), LEAD(), etc.)");
            
            // CTE support
            if (features.HasCte)
                notes.AppendLine("- Common Table Expressions (WITH clauses) supported");
            
            // Basic syntax notes
            notes.AppendLine("- Use double quotes for identifiers: \"column_name\"");
            notes.AppendLine("- String literals use single quotes: 'text'");
            notes.AppendLine("- Use ILIKE for case-insensitive text matching");
            notes.AppendLine("- Use LIMIT for row limiting");
            notes.AppendLine("- Use || for string concatenation");
            notes.AppendLine("- Use schema.table notation for qualified names");
            notes.AppendLine("- NOW() or CURRENT_TIMESTAMP for current timestamp");
            
            // Version-specific features
            if (version >= new Version(9, 5))
                notes.AppendLine("- UPSERT support available with ON CONFLICT clauses");
            
            if (version >= new Version(10, 0))
                notes.AppendLine("- Partitioning support available");
            
            if (version >= new Version(12, 0))
                notes.AppendLine("- Generated columns supported");
            
            // Performance tips
            notes.AppendLine("- Use EXPLAIN (ANALYZE, BUFFERS) to analyze query performance");
            notes.AppendLine("- Consider using appropriate indexes for WHERE clauses");
            
            return notes.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate dialect notes for PostgreSQL data source {DataSourceId}", dataSource.Id);
            
            // Return basic dialect notes as fallback
            return @"PostgreSQL-specific notes:
- Use double quotes for identifiers: ""column_name""
- String literals use single quotes: 'text'
- Use ILIKE for case-insensitive text matching
- Use LIMIT for row limiting
- Use || for string concatenation
- Use schema.table notation for qualified names
- NOW() or CURRENT_TIMESTAMP for current timestamp";
        }
    }
    
    public override async Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query)
    {
        var result = new QueryResult { SqlQuery = query };
        
        try
        {
            // Validate the query first
            var validation = await ValidateQueryAsync(dataSource, query);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.ErrorMessage = string.Join("; ", validation.Errors);
                return result;
            }

            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();

            // Set query timeout if specified
            var timeout = GetQueryTimeout(dataSource);
            
            await using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = timeout
            };

            await using var reader = await command.ExecuteReaderAsync();
            result.Data = new List<Dictionary<string, object>>();
            
            var rowLimit = GetRowLimit(dataSource);
            var rowCount = 0;
            
            while (await reader.ReadAsync() && (rowLimit <= 0 || rowCount < rowLimit))
            {
                var rowData = new Dictionary<string, object>();
                
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    
                    // Handle PostgreSQL-specific types
                    if (value != null)
                    {
                        value = ConvertPostgresType(value);
                    }
                    
                    rowData[columnName] = value ?? DBNull.Value;
                }
                
                result.Data.Add(rowData);
                rowCount++;
            }
            
            if (rowLimit > 0 && rowCount >= rowLimit)
            {
                _logger.LogWarning("Query result truncated at {RowLimit} rows", rowLimit);
            }
            
            result.Success = true;
        }
        catch (NpgsqlException ex)
        {
            result.Success = false;
            result.ErrorMessage = FormatPostgresError(ex);
            _logger.LogError(ex, "PostgreSQL error executing query against data source {DataSourceId}", dataSource.Id);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error executing query against data source {DataSourceId}", dataSource.Id);
        }
        
        return result;
    }

    public override async Task<ValidationResult> ValidateQueryAsync(DataSourceDefinition dataSource, string query)
    {
        var result = new ValidationResult { IsValid = true };
        
        // Check for dangerous SQL
        if (DangerousSqlPattern.IsMatch(query))
        {
            result.IsValid = false;
            result.Errors.Add("Only SELECT queries are allowed");
            return result;
        }
        
        // Try to parse the query with PostgreSQL
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();
            
            // Use EXPLAIN to validate without executing
            var explainQuery = $"EXPLAIN (FORMAT JSON) {query}";
            await using var command = new NpgsqlCommand(explainQuery, connection);
            await command.ExecuteScalarAsync();
        }
        catch (NpgsqlException ex)
        {
            result.IsValid = false;
            result.Errors.Add(FormatPostgresError(ex));
        }
        
        return result;
    }

    public override async Task<bool> TestConnectionAsync(DataSourceDefinition dataSource)
    {
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to PostgreSQL data source {DataSourceId}", dataSource.Id);
            return false;
        }
    }

    public override async Task<string?> GetQueryPlanAsync(DataSourceDefinition dataSource, string query)
    {
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();
            
            var explainQuery = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {query}";
            await using var command = new NpgsqlCommand(explainQuery, connection);
            
            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get query plan for data source {DataSourceId}", dataSource.Id);
            return null;
        }
    }

    public override async Task<DataSourceMetadata> GetMetadataAsync(DataSourceDefinition dataSource)
    {
        var metadata = new DataSourceMetadata
        {
            DatabaseType = "PostgreSQL"
        };
        
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();
            
            // Get version
            await using var versionCmd = new NpgsqlCommand("SELECT version()", connection);
            var versionResult = await versionCmd.ExecuteScalarAsync();
            metadata.Version = versionResult?.ToString() ?? "Unknown";
            
            // Get available schemas
            await using var schemaCmd = new NpgsqlCommand(@"
                SELECT schema_name 
                FROM information_schema.schemata 
                WHERE schema_name NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
                ORDER BY schema_name", connection);
            
            await using var reader = await schemaCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                metadata.AvailableSchemas.Add(reader.GetString(0));
            }
            await reader.CloseAsync();
            
            // Additional PostgreSQL-specific info
            metadata.AdditionalInfo["max_connections"] = await GetServerParameter(connection, "max_connections");
            metadata.AdditionalInfo["server_encoding"] = await GetServerParameter(connection, "server_encoding");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for data source {DataSourceId}", dataSource.Id);
        }
        
        return metadata;
    }

    // Explicitly override GenerateTitleAsync to ensure it's available
    public override async Task<string> GenerateTitleAsync(DataSourceDefinition dataSource, string userQuestion, ILlmService? llmService = null)
    {
        // Call the base implementation which has the logic
        return await base.GenerateTitleAsync(dataSource, userQuestion, llmService);
    }

    public override Task<List<QueryExample>> GetQueryExamplesAsync(DataSourceDefinition dataSource)
    {
        // Check if this is a specialized plugin data source
        var plugins = dataSource.ConnectionParameters.TryGetValue("PluginId", out var pluginId) ? pluginId : null;
        
        // Return generic PostgreSQL examples
        return Task.FromResult(new List<QueryExample>
        {
            new QueryExample
            {
                Title = "Table Row Counts",
                Category = "Basic Statistics",
                NaturalLanguageQuery = "How many rows are in each table?",
                SqlQuery = @"SELECT 
    schemaname AS schema_name,
    tablename AS table_name,
    n_live_tup AS row_count
FROM pg_stat_user_tables
ORDER BY n_live_tup DESC;",
                Description = "Shows approximate row counts for all tables"
            },
            new QueryExample
            {
                Title = "Database Size",
                Category = "Database Metrics",
                NaturalLanguageQuery = "What's the size of the database?",
                SqlQuery = @"SELECT 
    pg_database_size(current_database()) AS size_bytes,
    pg_size_pretty(pg_database_size(current_database())) AS size_pretty;",
                Description = "Shows the total size of the current database"
            },
            new QueryExample
            {
                Title = "Table Sizes",
                Category = "Storage Analysis",
                NaturalLanguageQuery = "What are the largest tables?",
                SqlQuery = @"SELECT
    schemaname AS schema_name,
    tablename AS table_name,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size,
    pg_total_relation_size(schemaname||'.'||tablename) AS size_bytes
FROM pg_tables
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC
LIMIT 20;",
                Description = "Lists the 20 largest tables by size"
            }
        });
    }

    public override async Task<Dictionary<string, string>> GetEntityDescriptionsAsync(DataSourceDefinition dataSource)
    {
        var descriptions = new Dictionary<string, string>();
        
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();
            
            // Get table comments
            var includedSchemas = GetIncludedSchemas(dataSource);
            var schemasList = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
            
            await using var cmd = new NpgsqlCommand($@"
                SELECT 
                    c.relname AS table_name,
                    obj_description(c.oid) AS table_comment
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind = 'r'
                  AND n.nspname IN ({schemasList})
                  AND obj_description(c.oid) IS NOT NULL;", connection);
            
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                var comment = reader.GetString(1);
                descriptions[tableName] = comment;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get entity descriptions for data source {DataSourceId}", dataSource.Id);
        }
        
        return descriptions;
    }

    public override Task<TitleGenerationContext> GetTitleGenerationContextAsync(DataSourceDefinition dataSource)
    {
        var context = new TitleGenerationContext
        {
            Abbreviations = new Dictionary<string, string>
            {
                ["DB"] = "Database",
                ["PG"] = "PostgreSQL",
                ["FK"] = "Foreign Key",
                ["PK"] = "Primary Key",
                ["IX"] = "Index",
                ["CTE"] = "Common Table Expression",
                ["JSONB"] = "JSON Binary"
            },
            
            KeyTerms = new List<string>
            {
                "table", "column", "index", "constraint", "schema", "query",
                "join", "aggregate", "function", "trigger", "view", "sequence",
                "primary key", "foreign key", "performance", "explain plan"
            },
            
            MainEntities = new List<(string Entity, string Description)>
            {
                ("tables", "Database tables"),
                ("indexes", "Performance indexes"),
                ("constraints", "Data integrity rules"),
                ("schemas", "Database namespaces"),
                ("views", "Virtual tables")
            },
            
            ExampleTitles = new List<string>
            {
                "Table Row Counts",
                "Database Size Analysis",
                "Index Usage Statistics",
                "Query Performance Review",
                "Schema Structure Overview",
                "Foreign Key Relationships",
                "Data Type Distribution"
            },
            
            AdditionalContext = "Focus on database structure and performance aspects when generating titles."
        };
        
        return Task.FromResult(context);
    }

    public override Task<string> GetQueryLanguageAsync(DataSourceDefinition dataSource)
    {
        return Task.FromResult("PostgreSQL");
    }

    public override Task<string> GetQueryLanguageDisplayAsync(DataSourceDefinition dataSource)
    {
        return Task.FromResult("PostgreSQL");
    }

    // Protected helper methods for schema extraction
    protected async Task ExtractSchemaInfo(NpgsqlConnection connection, string[] includedSchemas, 
                                        string[] excludedTables, StringBuilder schemaInfo)
    {
        var schemasClause = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
        var excludesClause = excludedTables.Length > 0 
            ? $"AND table_name NOT IN ({string.Join(",", excludedTables.Select(t => $"'{t}'"))})" 
            : "";

        var schemaData = new Dictionary<string, List<(string tableName, string columnName, string dataType, 
            string isNullable, string defaultValue, string maxLength)>>();

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
        
        await reader.CloseAsync();
        
        // Format the collected data
        foreach (var schema in schemaData.Keys.OrderBy(k => k))
        {
            schemaInfo.AppendLine($"-- Schema: {schema}");
            schemaInfo.AppendLine();
            
            var tableGroups = schemaData[schema].GroupBy(x => x.tableName);
            
            foreach (var tableGroup in tableGroups)
            {
                var tableName = tableGroup.Key;
                var fullTableName = $"{schema}.{tableName}";
                
                schemaInfo.AppendLine($"CREATE TABLE {fullTableName} (");
                
                var columns = tableGroup.ToList();
                for (var i = 0; i < columns.Count; i++)
                {
                    var column = columns[i];
                    schemaInfo.Append($"    {column.columnName} {column.dataType}{column.maxLength} " +
                                    $"{column.isNullable}{column.defaultValue}");
                    
                    if (i < columns.Count - 1)
                        schemaInfo.AppendLine(",");
                    else
                        schemaInfo.AppendLine();
                }
                
                schemaInfo.AppendLine(");");
                schemaInfo.AppendLine();
            }
        }
    }

    protected async Task ExtractKeysInformation(NpgsqlConnection connection, string[] includedSchemas, 
                                            string[] excludedTables, StringBuilder schemaInfo)
    {
        var schemasClause = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
        var excludesClause = excludedTables.Length > 0 
            ? $"AND tc.table_name NOT IN ({string.Join(",", excludedTables.Select(t => $"'{t}'"))})" 
            : "";
        
        // Get primary keys
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

        await using var pkReader = await pkCommand.ExecuteReaderAsync();
        
        while (await pkReader.ReadAsync())
        {
            var schema = pkReader.GetString(0);
            var tableName = pkReader.GetString(1);
            var columnName = pkReader.GetString(2);
            
            schemaInfo.AppendLine($"ALTER TABLE {schema}.{tableName} ADD PRIMARY KEY ({columnName});");
        }
        
        await pkReader.CloseAsync();
        
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
            
            schemaInfo.AppendLine($"ALTER TABLE {schema}.{tableName} ADD FOREIGN KEY ({columnName}) " +
                                $"REFERENCES {foreignSchema}.{foreignTable}({foreignColumn});");
        }
    }

    // Private helper methods
    private string[] GetIncludedSchemas(DataSourceDefinition dataSource)
    {
        if (dataSource.ConnectionParameters.TryGetValue("IncludedSchemas", out var schemasParam))
        {
            var schemasList = schemasParam.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            
            if (schemasList.Length > 0)
                return schemasList;
        }
        
        return new[] { "public" };
    }

    private string[] GetExcludedTables(DataSourceDefinition dataSource)
    {
        if (dataSource.ConnectionParameters.TryGetValue("ExcludedTables", out var tablesParam))
        {
            return tablesParam.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }
        
        return Array.Empty<string>();
    }

    private int GetQueryTimeout(DataSourceDefinition dataSource)
    {
        if (dataSource.ConnectionParameters.TryGetValue("QueryTimeout", out var timeoutParam) &&
            int.TryParse(timeoutParam, out var timeout))
        {
            return timeout;
        }
        
        return 30; // Default 30 seconds
    }

    private int GetRowLimit(DataSourceDefinition dataSource)
    {
        if (dataSource.ConnectionParameters.TryGetValue("MaxRows", out var maxRowsParam) &&
            int.TryParse(maxRowsParam, out var maxRows))
        {
            return maxRows;
        }
        
        return 1000; // Default 1000 rows
    }

    private object? ConvertPostgresType(object value)
    {
        // Convert PostgreSQL-specific types to JSON-serializable types
        return value switch
        {
            // Arrays
            Array array => array.Cast<object>().ToList(),
            // Dates with infinity
            DateTime dt when dt == DateTime.MinValue => null,
            DateTime dt when dt == DateTime.MaxValue => null,
            // NpgsqlPoint, NpgsqlBox, etc. would need custom handling
            _ => value
        };
    }

    private string FormatPostgresError(NpgsqlException ex)
    {
        var message = ex.Message ?? "";
    
        if (ex is PostgresException pgEx)
        {
            if (!string.IsNullOrEmpty(pgEx.Detail))
                message += $" Detail: {pgEx.Detail}";
            
            if (!string.IsNullOrEmpty(pgEx.Hint))
                message += $" Hint: {pgEx.Hint}";
            
            if (!string.IsNullOrEmpty(pgEx.Where))
                message += $" Where: {pgEx.Where}";
            
            if (!string.IsNullOrEmpty(pgEx.SchemaName))
                message += $" Schema: {pgEx.SchemaName}";
            
            if (!string.IsNullOrEmpty(pgEx.TableName))
                message += $" Table: {pgEx.TableName}";
            
            if (!string.IsNullOrEmpty(pgEx.ColumnName))
                message += $" Column: {pgEx.ColumnName}";
        }
        else
        {
            // Fallback for other NpgsqlException types
            if (ex.InnerException != null)
                message += $" Inner: {ex.InnerException.Message}";
            
            if (!string.IsNullOrEmpty(ex.Source))
                message += $" Source: {ex.Source}";
        }
    
        // Ensure we never return null
        return string.IsNullOrEmpty(message) ? "Unknown PostgreSQL error" : message;
    }

    private async Task<string> GetServerParameter(NpgsqlConnection connection, string parameter)
    {
        try
        {
            await using var cmd = new NpgsqlCommand($"SHOW {parameter}", connection);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
    
    private async Task<PostgresFeatures> DetectPostgresFeatures(NpgsqlConnection connection, Version version)
    {
        var features = new PostgresFeatures();
        
        try
        {
            // Check for JSONB support (9.4+)
            if (version >= new Version(9, 4))
            {
                await using var jsonbCmd = new NpgsqlCommand(
                    "SELECT 1 FROM pg_type WHERE typname = 'jsonb'", connection);
                var jsonbResult = await jsonbCmd.ExecuteScalarAsync();
                features.HasJsonb = jsonbResult != null;
            }
            
            // Check for basic JSON support (9.2+)
            if (version >= new Version(9, 2))
            {
                await using var jsonCmd = new NpgsqlCommand(
                    "SELECT 1 FROM pg_type WHERE typname = 'json'", connection);
                var jsonResult = await jsonCmd.ExecuteScalarAsync();
                features.HasJson = jsonResult != null;
            }
            
            // Window functions (8.4+)
            features.HasWindowFunctions = version >= new Version(8, 4);
            
            // CTE support (8.4+)
            features.HasCte = version >= new Version(8, 4);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error detecting PostgreSQL features, using version-based defaults");
            
            // Fallback to version-based detection
            features.HasJsonb = version >= new Version(9, 4);
            features.HasJson = version >= new Version(9, 2);
            features.HasWindowFunctions = version >= new Version(8, 4);
            features.HasCte = version >= new Version(8, 4);
        }
        
        return features;
    }
    
    private class PostgresFeatures
    {
        public bool HasJsonb { get; set; }
        public bool HasJson { get; set; }
        public bool HasWindowFunctions { get; set; }
        public bool HasCte { get; set; }
    }
}
