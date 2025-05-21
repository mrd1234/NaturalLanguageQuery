using System.Text;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using Npgsql;

namespace NLQueryApp.Data.Providers;

public class PostgresDataSourceProvider : IDataSourceProvider
{
    private readonly ILogger<PostgresDataSourceProvider> _logger;

    public PostgresDataSourceProvider(ILogger<PostgresDataSourceProvider> logger)
    {
        _logger = logger;
    }

    public string ProviderType => "postgres";

    public async Task<string> GetSchemaAsync(DataSourceDefinition dataSource)
    {
        var connectionString = dataSource.GetConnectionString();
        var schemaInfo = new StringBuilder();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        
        // Extract schema information similar to how it's done in DatabaseService
        var includedSchemas = new[] {"public"};
        
        // Check if there are specifically included schemas in connection parameters
        if (dataSource.ConnectionParameters.TryGetValue("IncludedSchemas", out var schemasParam))
        {
            var schemasList = schemasParam.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            
            if (schemasList.Length > 0)
                includedSchemas = schemasList;
        }
        
        var excludedTables = Array.Empty<string>();
        
        // Check if there are specifically excluded tables in connection parameters
        if (dataSource.ConnectionParameters.TryGetValue("ExcludedTables", out var tablesParam))
        {
            excludedTables = tablesParam.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        // Query for table information
        var schemasClause = string.Join(",", includedSchemas.Select(s => $"'{s}'"));
        var excludesClause = excludedTables.Length > 0 
            ? $"AND table_name NOT IN ({string.Join(",", excludedTables.Select(t => $"'{t}'"))})" 
            : "";

        Dictionary<string, List<(string tableName, string columnName, string dataType, string isNullable, string defaultValue, string maxLength)>> schemaData = 
            new();

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
        
        // Add foreign keys
        await ExtractKeysInformation(connection, includedSchemas, excludedTables, schemaInfo);
        
        return schemaInfo.ToString();
    }

    public async Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query)
    {
        var result = new QueryResult { SqlQuery = query };
        
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(query, connection);
            
            // Only allow SELECT statements for security
            if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
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
            _logger.LogError(ex, "Error executing query against data source {DataSourceId}", dataSource.Id);
        }
        
        return result;
    }

    public async Task<bool> TestConnectionAsync(DataSourceDefinition dataSource)
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
            _logger.LogError(ex, "Failed to connect to data source {DataSourceId}", dataSource.Id);
            return false;
        }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting primary key information");
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
}