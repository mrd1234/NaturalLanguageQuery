using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using Npgsql;

namespace NLQueryApp.Data.Services;

public class DataSourceManager : IDataSourceManager
{
    private readonly string _connectionString;
    private readonly ILogger<DataSourceManager> _logger;
    private readonly Dictionary<string, IDataSourceProvider> _providers;
    private bool _initialized = false;

    public DataSourceManager(IConfiguration configuration, ILogger<DataSourceManager> logger, 
        IEnumerable<IDataSourceProvider> providers)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
                           ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _providers = providers.ToDictionary(p => p.ProviderType.ToLowerInvariant());
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        
        await InitializeAppTables();
        _initialized = true;
    }

    private async Task InitializeAppTables()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            
            // Create the schema
            await using var schemaCommand = new NpgsqlCommand(
                "CREATE SCHEMA IF NOT EXISTS app", connection);
            await schemaCommand.ExecuteNonQueryAsync();
            
            // Create data sources table
            await using var dataSourcesCommand = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS app.data_sources (
                    id VARCHAR(50) PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    description TEXT,
                    source_type VARCHAR(50) NOT NULL,
                    connection_parameters JSONB NOT NULL,
                    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                )", connection);
            await dataSourcesCommand.ExecuteNonQueryAsync();
            
            // Create data source contexts table
            await using var contextsCommand = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS app.data_source_contexts (
                    data_source_id VARCHAR(50) REFERENCES app.data_sources(id) ON DELETE CASCADE,
                    context_text TEXT NOT NULL,
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (data_source_id)
                )", connection);
            await contextsCommand.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Data source tables initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize data source tables");
            throw;
        }
    }

    public async Task<List<DataSourceDefinition>> GetDataSourcesAsync()
    {
        await EnsureInitializedAsync();
        
        var dataSources = new List<DataSourceDefinition>();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            SELECT id, name, description, source_type, connection_parameters, created_at, updated_at
            FROM app.data_sources
            ORDER BY name", connection);
        
        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var dataSource = new DataSourceDefinition
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Type = reader.GetString(3),
                ConnectionParameters = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4)) 
                                      ?? new Dictionary<string, string>(),
                Created = reader.GetDateTime(5),
                LastUpdated = reader.GetDateTime(6)
            };
            
            dataSources.Add(dataSource);
        }
        
        return dataSources;
    }

    public async Task<DataSourceDefinition> GetDataSourceAsync(string id)
    {
        await EnsureInitializedAsync();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            SELECT id, name, description, source_type, connection_parameters, created_at, updated_at
            FROM app.data_sources
            WHERE id = @id", connection);
        
        command.Parameters.AddWithValue("@id", id);
        
        await using var reader = await command.ExecuteReaderAsync();
        
        if (!await reader.ReadAsync())
            throw new KeyNotFoundException($"Data source with ID {id} not found");
        
        return new DataSourceDefinition
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            Type = reader.GetString(3),
            ConnectionParameters = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4)) 
                                  ?? new Dictionary<string, string>(),
            Created = reader.GetDateTime(5),
            LastUpdated = reader.GetDateTime(6)
        };
    }

    public async Task<DataSourceDefinition> CreateDataSourceAsync(DataSourceDefinition dataSource)
    {
        await EnsureInitializedAsync();
        
        // Validate connection parameters
        var (isValid, missingParams) = dataSource.ValidateConnectionParameters();
        if (!isValid)
        {
            throw new ArgumentException($"Missing required connection parameters: {string.Join(", ", missingParams)}");
        }
        
        // Validate provider type
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported. " +
                                      $"Supported types: {string.Join(", ", _providers.Keys)}");
        }
        
        // Test connection
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        if (!await provider.TestConnectionAsync(dataSource))
        {
            throw new InvalidOperationException("Connection test failed. Please check connection parameters.");
        }
        
        // Insert into database
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            INSERT INTO app.data_sources (id, name, description, source_type, connection_parameters, created_at, updated_at)
            VALUES (@id, @name, @description, @sourceType, @connectionParameters::jsonb, @createdAt, @updatedAt)
            RETURNING id", connection);
        
        var now = DateTime.UtcNow;
        dataSource.Created = now;
        dataSource.LastUpdated = now;
        
        command.Parameters.AddWithValue("@id", dataSource.Id);
        command.Parameters.AddWithValue("@name", dataSource.Name);
        command.Parameters.AddWithValue("@description", dataSource.Description);
        command.Parameters.AddWithValue("@sourceType", dataSource.Type);
        
        var jsonParams = JsonSerializer.Serialize(dataSource.ConnectionParameters);
        var npgsqlParameter = new NpgsqlParameter("@connectionParameters", NpgsqlTypes.NpgsqlDbType.Jsonb)
        {
            Value = jsonParams
        };
        command.Parameters.Add(npgsqlParameter);
        
        command.Parameters.AddWithValue("@createdAt", dataSource.Created);
        command.Parameters.AddWithValue("@updatedAt", dataSource.LastUpdated);
        
        var id = await command.ExecuteScalarAsync() as string;
        dataSource.Id = id ?? dataSource.Id;
        
        _logger.LogInformation("Created data source {DataSourceId} of type {DataSourceType}", 
            dataSource.Id, dataSource.Type);
        
        return dataSource;
    }

    public async Task<DataSourceDefinition> UpdateDataSourceAsync(string id, DataSourceDefinition dataSource)
    {
        await EnsureInitializedAsync();
        
        // Make sure the data source exists
        var existingDataSource = await GetDataSourceAsync(id);
        
        // Validate connection parameters
        var (isValid, missingParams) = dataSource.ValidateConnectionParameters();
        if (!isValid)
        {
            throw new ArgumentException($"Missing required connection parameters: {string.Join(", ", missingParams)}");
        }
        
        // Validate provider type
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        // Test connection
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        if (!await provider.TestConnectionAsync(dataSource))
        {
            throw new InvalidOperationException("Connection test failed. Please check connection parameters.");
        }
        
        // Update in database
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            UPDATE app.data_sources 
            SET name = @name, 
                description = @description, 
                source_type = @sourceType, 
                connection_parameters = @connectionParameters::jsonb,
                updated_at = @updatedAt
            WHERE id = @id", connection);
        
        dataSource.Id = id;
        dataSource.Created = existingDataSource.Created;
        dataSource.LastUpdated = DateTime.UtcNow;
        
        command.Parameters.AddWithValue("@id", dataSource.Id);
        command.Parameters.AddWithValue("@name", dataSource.Name);
        command.Parameters.AddWithValue("@description", dataSource.Description);
        command.Parameters.AddWithValue("@sourceType", dataSource.Type);
        
        var jsonParams = JsonSerializer.Serialize(dataSource.ConnectionParameters);
        var npgsqlParameter = new NpgsqlParameter("@connectionParameters", NpgsqlTypes.NpgsqlDbType.Jsonb)
        {
            Value = jsonParams
        };
        command.Parameters.Add(npgsqlParameter);
        
        command.Parameters.AddWithValue("@updatedAt", dataSource.LastUpdated);
        
        await command.ExecuteNonQueryAsync();
        
        _logger.LogInformation("Updated data source {DataSourceId}", dataSource.Id);
        
        return dataSource;
    }

    public async Task<bool> DeleteDataSourceAsync(string id)
    {
        await EnsureInitializedAsync();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            DELETE FROM app.data_sources 
            WHERE id = @id", connection);
        
        command.Parameters.AddWithValue("@id", id);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        
        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deleted data source {DataSourceId}", id);
        }
        
        return rowsAffected > 0;
    }

    public async Task<string> GetSchemaAsync(string dataSourceId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.GetSchemaAsync(dataSource);
    }

    public async Task<string> GetDialectNotesAsync(string dataSourceId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.GetDialectNotesAsync(dataSource);
    }

    public async Task<string> GetSchemaContextAsync(string dataSourceId)
    {
        await EnsureInitializedAsync();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            SELECT context_text 
            FROM app.data_source_contexts 
            WHERE data_source_id = @dataSourceId", connection);
        
        command.Parameters.AddWithValue("@dataSourceId", dataSourceId);
        
        var contextText = await command.ExecuteScalarAsync() as string;
        
        return contextText ?? "No additional context available for this data source.";
    }

    public async Task<bool> SetSchemaContextAsync(string dataSourceId, string context)
    {
        await EnsureInitializedAsync();
        
        // Make sure the data source exists
        await GetDataSourceAsync(dataSourceId);
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(@"
            INSERT INTO app.data_source_contexts (data_source_id, context_text, updated_at)
            VALUES (@dataSourceId, @contextText, @updatedAt)
            ON CONFLICT (data_source_id) 
            DO UPDATE SET context_text = EXCLUDED.context_text, updated_at = EXCLUDED.updated_at", connection);
        
        command.Parameters.AddWithValue("@dataSourceId", dataSourceId);
        command.Parameters.AddWithValue("@contextText", context);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        
        _logger.LogInformation("Updated schema context for data source {DataSourceId}", dataSourceId);
        
        return rowsAffected > 0;
    }

    public async Task<QueryResult> ExecuteQueryAsync(string dataSourceId, string query)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        
        _logger.LogInformation("Executing query against data source {DataSourceId} of type {DataSourceType}", 
            dataSourceId, dataSource.Type);
        
        return await provider.ExecuteQueryAsync(dataSource, query);
    }

    public async Task<ValidationResult> ValidateQueryAsync(string dataSourceId, string query)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.ValidateQueryAsync(dataSource, query);
    }

    public async Task<bool> TestConnectionAsync(string dataSourceId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.TestConnectionAsync(dataSource);
    }

    public async Task<string?> GetQueryPlanAsync(string dataSourceId, string query)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.GetQueryPlanAsync(dataSource, query);
    }

    public async Task<DataSourceMetadata> GetMetadataAsync(string dataSourceId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
        
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
        {
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
        }
        
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.GetMetadataAsync(dataSource);
    }
    
    public async Task<DatabaseInfo?> GetDatabaseInfoAsync(string dataSourceId)
    {
        var dataSource = await GetDataSourceAsync(dataSourceId);
    
        if (!_providers.ContainsKey(dataSource.Type.ToLowerInvariant()))
            throw new ArgumentException($"Provider type '{dataSource.Type}' is not supported");
    
        var provider = _providers[dataSource.Type.ToLowerInvariant()];
        return await provider.GetDatabaseInfoAsync(dataSource);
    }
}