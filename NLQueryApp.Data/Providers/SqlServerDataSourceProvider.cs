using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Data.Providers;

public class SqlServerDataSourceProvider : IDataSourceProvider
{
    private readonly ILogger<SqlServerDataSourceProvider> _logger;
    private static readonly Regex DangerousSqlPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SqlServerDataSourceProvider(ILogger<SqlServerDataSourceProvider> logger)
    {
        _logger = logger;
    }

    public string ProviderType => "sqlserver";

    public Task<string> GetSchemaAsync(DataSourceDefinition dataSource)
    {
        // Implementation would extract schema from SQL Server
        throw new NotImplementedException("SQL Server schema extraction not implemented");
    }

    public async Task<DatabaseInfo?> GetDatabaseInfoAsync(DataSourceDefinition dataSource)
    {
        try
        {
            var connectionString = dataSource.GetConnectionString();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
        
            await using var command = new SqlCommand("SELECT @@VERSION", connection);
            var versionString = await command.ExecuteScalarAsync() as string ?? "";
        
            // Parse SQL Server version
            var match = Regex.Match(versionString, @"Microsoft SQL Server (\d+)");
            var version = "Unknown";
            var parsedVersion = new Version(1, 0);
        
            if (match.Success && match.Groups.Count >= 2)
            {
                var majorVersionStr = match.Groups[1].Value;
                version = majorVersionStr switch
                {
                    "16" => "2022",
                    "15" => "2019", 
                    "14" => "2017",
                    "13" => "2016",
                    "12" => "2014",
                    "11" => "2012",
                    "10" => "2008",
                    _ => majorVersionStr
                };
                
                if (int.TryParse(majorVersionStr, out var major))
                    parsedVersion = new Version(major, 0);
            }
        
            return new DatabaseInfo
            {
                Type = "sqlserver",
                Version = version,
                ParsedVersion = parsedVersion,
                FullVersionString = versionString
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get database version info for SQL Server data source {DataSourceId}", dataSource.Id);
            return null;
        }
    }

    public async Task<string> GetDialectNotesAsync(DataSourceDefinition dataSource)
    {
        try
        {
            var databaseInfo = await GetDatabaseInfoAsync(dataSource);
            var version = databaseInfo?.ParsedVersion ?? new Version(1, 0);
            
            var connectionString = dataSource.GetConnectionString();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            var features = DetectSqlServerFeatures(version);
            
            var notes = new StringBuilder();
            notes.AppendLine("SQL Server (T-SQL) specific notes:");
            
            // Basic syntax
            notes.AppendLine("- Use LIKE with LOWER() for case-insensitive matching, or use COLLATE");
            notes.AppendLine("- Use CAST() or CONVERT() for type conversion");
            notes.AppendLine("- Use square brackets [table] for identifiers with special characters");
            notes.AppendLine("- [schema].[table] notation for qualified names");
            
            // Row limiting
            if (features.HasOffsetFetch)
                notes.AppendLine("- Use TOP for row limiting, or OFFSET/FETCH for pagination");
            else
                notes.AppendLine("- Use TOP for row limiting");
            
            // CTEs
            if (features.HasCte)
                notes.AppendLine("- Support for CTEs (WITH clauses)");
            
            // JSON support
            if (features.HasJsonFunctions)
                notes.AppendLine("- JSON functions available (JSON_VALUE, OPENJSON, etc.)");
            else
                notes.AppendLine("- No JSON functions available (legacy version)");
            
            // String functions
            if (features.HasStringAgg)
                notes.AppendLine("- STRING_AGG available for string concatenation");
            else
                notes.AppendLine("- Use FOR XML PATH('') for string concatenation");
            
            // Date functions
            if (version.Major >= 12)
                notes.AppendLine("- GETDATE() or CURRENT_TIMESTAMP for current timestamp");
            else
                notes.AppendLine("- GETDATE() for current timestamp");
            
            // Window functions
            if (features.HasWindowFunctions)
                notes.AppendLine("- Window functions supported (ROW_NUMBER(), RANK(), etc.)");
            
            // Performance tips
            notes.AppendLine("- Use SET STATISTICS IO ON to analyze query performance");
            notes.AppendLine("- Consider using appropriate indexes for WHERE clauses");
            
            return notes.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate dialect notes for SQL Server data source {DataSourceId}", dataSource.Id);
            
            // Return basic dialect notes as fallback
            return @"
SQL Server (T-SQL) specific notes:
- Use LIKE with LOWER() for case-insensitive matching, or use COLLATE
- Use CAST() or CONVERT() for type conversion
- Use TOP for row limiting
- Use square brackets [table] for identifiers with special characters
- [schema].[table] notation for qualified names
- GETDATE() for current timestamp";
        }
    }
    
    private SqlServerFeatures DetectSqlServerFeatures(Version version)
    {
        var features = new SqlServerFeatures();
        
        try
        {
            // Version-based feature detection
            features.HasCte = version.Major >= 9; // SQL Server 2005+
            features.HasWindowFunctions = version.Major >= 9; // SQL Server 2005+
            features.HasOffsetFetch = version.Major >= 11; // SQL Server 2012+
            features.HasStringAgg = version.Major >= 14; // SQL Server 2017+
            features.HasJsonFunctions = version.Major >= 13; // SQL Server 2016+
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error detecting SQL Server features, using version-based defaults");
        }
        
        return features;
    }
    
    private class SqlServerFeatures
    {
        public bool HasCte { get; set; }
        public bool HasWindowFunctions { get; set; }
        public bool HasOffsetFetch { get; set; }
        public bool HasStringAgg { get; set; }
        public bool HasJsonFunctions { get; set; }
    }

    public Task<QueryResult> ExecuteQueryAsync(DataSourceDefinition dataSource, string query)
    {
        throw new NotImplementedException();
    }

    public Task<ValidationResult> ValidateQueryAsync(DataSourceDefinition dataSource, string query)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TestConnectionAsync(DataSourceDefinition dataSource)
    {
        throw new NotImplementedException();
    }

    public Task<string?> GetQueryPlanAsync(DataSourceDefinition dataSource, string query)
    {
        throw new NotImplementedException();
    }

    public Task<DataSourceMetadata> GetMetadataAsync(DataSourceDefinition dataSource)
    {
        throw new NotImplementedException();
    }

    public Task<string> GenerateTitleAsync(DataSourceDefinition dataSource, string userQuestion, ILlmService? llmService = null)
    {
        throw new NotImplementedException();
    }

    public Task<List<QueryExample>> GetQueryExamplesAsync(DataSourceDefinition dataSource)
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<string, string>> GetEntityDescriptionsAsync(DataSourceDefinition dataSource)
    {
        throw new NotImplementedException();
    }

    public Task<bool> SetupSchemaAsync(DataSourceDefinition dataSource, bool dropIfExists = false)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetPromptEnhancementsAsync(DataSourceDefinition dataSource)
    {
        throw new NotImplementedException();
    }

    public async Task<string> GetQueryLanguageAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return "T-SQL";
    }

    public async Task<string> GetQueryLanguageDisplayAsync(DataSourceDefinition dataSource)
    {
        await Task.CompletedTask;
        return "T-SQL (Transact-SQL)";
    }
    
    public Task<TitleGenerationContext> GetTitleGenerationContextAsync(DataSourceDefinition dataSource)
    {
        // Return basic SQL Server context
        var context = new TitleGenerationContext
        {
            Abbreviations = new Dictionary<string, string>
            {
                ["DB"] = "Database",
                ["SQL"] = "Structured Query Language",
                ["T-SQL"] = "Transact-SQL",
                ["FK"] = "Foreign Key",
                ["PK"] = "Primary Key",
                ["IX"] = "Index",
                ["CTE"] = "Common Table Expression"
            },
            
            KeyTerms = new List<string>
            {
                "table", "column", "index", "constraint", "schema", "query",
                "join", "aggregate", "function", "trigger", "view", "stored procedure",
                "primary key", "foreign key", "performance", "execution plan"
            },
            
            MainEntities = new List<(string Entity, string Description)>
            {
                ("tables", "Database tables"),
                ("indexes", "Performance indexes"),
                ("constraints", "Data integrity rules"),
                ("schemas", "Database namespaces"),
                ("views", "Virtual tables"),
                ("procedures", "Stored procedures")
            },
            
            ExampleTitles = new List<string>
            {
                "Table Row Counts",
                "Database Size Analysis",
                "Index Usage Statistics",
                "Query Performance Review",
                "Schema Structure Overview",
                "Foreign Key Relationships"
            },
            
            AdditionalContext = "Focus on SQL Server specific features and T-SQL syntax when generating titles."
        };
        
        return Task.FromResult(context);
    }
}
