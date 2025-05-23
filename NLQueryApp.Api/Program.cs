using NLQueryApp.Api.Services;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.Data;
using NLQueryApp.LlmServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5297", "https://localhost:7107",  // Web app ports
                "http://localhost:5101", "https://localhost:7237"   // API ports (for testing)
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Register services
builder.Services.AddDataServices();
builder.Services.AddLlmServices(builder.Configuration);
builder.Services.AddScoped<QueryService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseCors("AllowBlazorApp");
app.UseAuthorization();
app.MapControllers();

// Initialize the database with retry logic
using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    const int maxRetries = 5;
    var retryDelayMs = 1000;
    
    for (var i = 0; i < maxRetries; i++)
    {
        try
        {
            logger.LogInformation("Attempting database initialization...");
            await dbService.InitializeDatabaseAsync();
            logger.LogInformation("Database initialization successful");
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialization attempt {Attempt} failed", i + 1);
            
            if (i < maxRetries - 1)
            {
                logger.LogInformation("Retrying in {RetryDelay}ms", retryDelayMs);
                await Task.Delay(retryDelayMs);
                retryDelayMs *= 2;
            }
            else
            {
                logger.LogCritical(ex, "Database initialization failed after {MaxRetries} attempts", maxRetries);
            }
        }
    }

    // Initialize default data source if none exists
    try
    {
        var dataSourceManager = scope.ServiceProvider.GetRequiredService<IDataSourceManager>();
        var dataSources = await dataSourceManager.GetDataSourcesAsync();
        
        var defaultDsConfig = builder.Configuration.GetSection("DefaultDataSource");
        var defaultDsId = defaultDsConfig["Id"] ?? "team-movements";
        
        // Check if the default data source already exists
        var existingDefault = dataSources.FirstOrDefault(ds => ds.Id == defaultDsId);
        
        if (existingDefault == null && defaultDsConfig.Exists())
        {
            logger.LogInformation("Creating default data source...");
            
            var defaultDataSource = new DataSourceDefinition
            {
                Id = defaultDsId,
                Name = defaultDsConfig["Name"] ?? "Team Movements Database",
                Description = defaultDsConfig["Description"] ?? "Default PostgreSQL data source",
                Type = defaultDsConfig["Type"] ?? "postgres",
                ConnectionParameters = new Dictionary<string, string>()
            };
            
            // Build connection parameters from configuration
            foreach (var config in defaultDsConfig.GetChildren())
            {
                var key = config.Key;
                var value = config.Value;
                
                // Skip non-connection parameter fields
                if (key == "Id" || key == "Name" || key == "Description" || key == "Type")
                    continue;
                    
                if (!string.IsNullOrEmpty(value))
                {
                    defaultDataSource.ConnectionParameters[key] = value;
                }
            }
            
            try
            {
                await dataSourceManager.CreateDataSourceAsync(defaultDataSource);
                logger.LogInformation("Default data source created successfully");
                
                // Set the schema context if the file exists
                var contextPath = "SchemaContext/team_movements_context.md";
                if (File.Exists(contextPath))
                {
                    var context = await File.ReadAllTextAsync(contextPath);
                    await dataSourceManager.SetSchemaContextAsync(defaultDsId, context);
                    logger.LogInformation("Schema context loaded for default data source");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create default data source. It may already exist or connection may be invalid.");
            }
        }
        else if (existingDefault != null)
        {
            logger.LogInformation("Default data source already exists");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize default data source");
    }
}

app.Run();