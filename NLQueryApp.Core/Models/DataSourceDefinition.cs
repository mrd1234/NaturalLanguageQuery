namespace NLQueryApp.Core.Models;

public class DataSourceDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "postgres";  // postgres, sqlserver, mysql, mongodb, etc.
    public Dictionary<string, string> ConnectionParameters { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Get a proper connection string based on the data source type
    /// </summary>
    public string GetConnectionString()
    {
        return Type.ToLowerInvariant() switch
        {
            "postgres" => BuildPostgresConnectionString(),
            "sqlserver" => BuildSqlServerConnectionString(),
            "mysql" => BuildMySqlConnectionString(),
            "mongodb" => BuildMongoDbConnectionString(),
            _ => throw new NotSupportedException($"Data source type '{Type}' is not supported")
        };
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
            
        // Additional parameters
        if (ConnectionParameters.TryGetValue("SslMode", out var sslMode))
            parameters.Add($"SSL Mode={sslMode}");
            
        if (ConnectionParameters.TryGetValue("TrustServerCertificate", out var trustCert))
            parameters.Add($"Trust Server Certificate={trustCert}");
            
        parameters.Add("Maximum Pool Size=50");
        parameters.Add("Timeout=30");
        parameters.Add("Command Timeout=30");
        
        return string.Join(";", parameters);
    }
    
    private string BuildSqlServerConnectionString()
    {
        var parameters = new List<string>();
        
        // Handle server/instance format
        if (ConnectionParameters.TryGetValue("Server", out var server))
        {
            if (ConnectionParameters.TryGetValue("Instance", out var instance) && !string.IsNullOrEmpty(instance))
                parameters.Add($"Server={server}\\{instance}");
            else
                parameters.Add($"Server={server}");
        }
        
        if (ConnectionParameters.TryGetValue("Port", out var port) && port != "1433")
            parameters[^1] = parameters[^1] + $",{port}";
            
        if (ConnectionParameters.TryGetValue("Database", out var database))
            parameters.Add($"Database={database}");
            
        // Authentication
        if (ConnectionParameters.TryGetValue("AuthType", out var authType) && authType == "Windows")
        {
            parameters.Add("Integrated Security=true");
        }
        else
        {
            if (ConnectionParameters.TryGetValue("Username", out var username))
                parameters.Add($"User Id={username}");
                
            if (ConnectionParameters.TryGetValue("Password", out var password))
                parameters.Add($"Password={password}");
        }
        
        // Additional parameters
        if (ConnectionParameters.TryGetValue("Encrypt", out var encrypt))
            parameters.Add($"Encrypt={encrypt}");
        else
            parameters.Add("Encrypt=false");
            
        if (ConnectionParameters.TryGetValue("TrustServerCertificate", out var trustCert))
            parameters.Add($"TrustServerCertificate={trustCert}");
            
        parameters.Add("MultipleActiveResultSets=true");
        parameters.Add("Connection Timeout=30");
        
        return string.Join(";", parameters);
    }
    
    private string BuildMySqlConnectionString()
    {
        var parameters = new List<string>();
        
        if (ConnectionParameters.TryGetValue("Server", out var server))
            parameters.Add($"Server={server}");
            
        if (ConnectionParameters.TryGetValue("Port", out var port))
            parameters.Add($"Port={port}");
        else
            parameters.Add("Port=3306");  // Default MySQL port
            
        if (ConnectionParameters.TryGetValue("Database", out var database))
            parameters.Add($"Database={database}");
            
        if (ConnectionParameters.TryGetValue("User", out var user))
            parameters.Add($"User={user}");
            
        if (ConnectionParameters.TryGetValue("Password", out var password))
            parameters.Add($"Password={password}");
            
        // Additional parameters
        if (ConnectionParameters.TryGetValue("SslMode", out var sslMode))
            parameters.Add($"SslMode={sslMode}");
            
        parameters.Add("AllowUserVariables=true");
        parameters.Add("ConnectionTimeout=30");
        
        return string.Join(";", parameters);
    }
    
    private string BuildMongoDbConnectionString()
    {
        var builder = new System.Text.StringBuilder("mongodb://");
        
        // Authentication
        if (ConnectionParameters.TryGetValue("Username", out var username) &&
            ConnectionParameters.TryGetValue("Password", out var password))
        {
            builder.Append($"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@");
        }
        
        // Host(s)
        if (ConnectionParameters.TryGetValue("Hosts", out var hosts))
        {
            builder.Append(hosts);
        }
        else if (ConnectionParameters.TryGetValue("Host", out var host))
        {
            builder.Append(host);
            if (ConnectionParameters.TryGetValue("Port", out var port))
                builder.Append($":{port}");
            else
                builder.Append(":27017");  // Default MongoDB port
        }
        
        // Database
        if (ConnectionParameters.TryGetValue("Database", out var database))
            builder.Append($"/{database}");
            
        // Additional options
        var options = new List<string>();
        
        if (ConnectionParameters.TryGetValue("ReplicaSet", out var replicaSet))
            options.Add($"replicaSet={replicaSet}");
            
        if (ConnectionParameters.TryGetValue("AuthSource", out var authSource))
            options.Add($"authSource={authSource}");
            
        if (ConnectionParameters.TryGetValue("Ssl", out var ssl) && ssl.ToLower() == "true")
            options.Add("ssl=true");
            
        if (options.Count > 0)
            builder.Append($"?{string.Join("&", options)}");
            
        return builder.ToString();
    }
    
    /// <summary>
    /// Validate that required connection parameters are present for the data source type
    /// </summary>
    public (bool IsValid, List<string> MissingParameters) ValidateConnectionParameters()
    {
        var missing = new List<string>();
        
        switch (Type.ToLowerInvariant())
        {
            case "postgres":
                if (!ConnectionParameters.ContainsKey("Host")) missing.Add("Host");
                if (!ConnectionParameters.ContainsKey("Database")) missing.Add("Database");
                if (!ConnectionParameters.ContainsKey("Username")) missing.Add("Username");
                if (!ConnectionParameters.ContainsKey("Password")) missing.Add("Password");
                break;
                
            case "sqlserver":
                if (!ConnectionParameters.ContainsKey("Server")) missing.Add("Server");
                if (!ConnectionParameters.ContainsKey("Database")) missing.Add("Database");
                if (!ConnectionParameters.TryGetValue("AuthType", out var authType) || authType != "Windows")
                {
                    if (!ConnectionParameters.ContainsKey("Username")) missing.Add("Username");
                    if (!ConnectionParameters.ContainsKey("Password")) missing.Add("Password");
                }
                break;
                
            case "mysql":
                if (!ConnectionParameters.ContainsKey("Server")) missing.Add("Server");
                if (!ConnectionParameters.ContainsKey("Database")) missing.Add("Database");
                if (!ConnectionParameters.ContainsKey("User")) missing.Add("User");
                if (!ConnectionParameters.ContainsKey("Password")) missing.Add("Password");
                break;
                
            case "mongodb":
                if (!ConnectionParameters.ContainsKey("Host") && !ConnectionParameters.ContainsKey("Hosts")) 
                    missing.Add("Host or Hosts");
                break;
        }
        
        return (missing.Count == 0, missing);
    }
}