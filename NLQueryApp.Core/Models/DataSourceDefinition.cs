namespace NLQueryApp.Core.Models;

public class DataSourceDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "postgres";  // Default is postgres, could be "mysql", "sqlserver", etc.
    public Dictionary<string, string> ConnectionParameters { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    // Helper to get a proper connection string (implementation depends on data source type)
    public string GetConnectionString()
    {
        switch (Type.ToLowerInvariant())
        {
            case "postgres":
                return BuildPostgresConnectionString();
            // Add other database types here
            default:
                throw new NotSupportedException($"Data source type '{Type}' is not supported");
        }
    }
    
    private string BuildPostgresConnectionString()
    {
        var parameters = new List<string>();
        
        if (ConnectionParameters.TryGetValue("Host", out var host))
            parameters.Add($"Host={host}");
            
        if (ConnectionParameters.TryGetValue("Port", out var port))
            parameters.Add($"Port={port}");
        else
            parameters.Add("Port=5432");  // Default PostgreSQL port
            
        if (ConnectionParameters.TryGetValue("Database", out var database))
            parameters.Add($"Database={database}");
            
        if (ConnectionParameters.TryGetValue("Username", out var username))
            parameters.Add($"Username={username}");
            
        if (ConnectionParameters.TryGetValue("Password", out var password))
            parameters.Add($"Password={password}");
            
        parameters.Add("Maximum Pool Size=50");
        parameters.Add("Timeout=30");
        parameters.Add("Command Timeout=30");
        
        return string.Join(";", parameters);
    }
}