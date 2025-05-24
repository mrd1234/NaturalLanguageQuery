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

// Add HttpClient factory
builder.Services.AddHttpClient();

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

// Register plugins if enabled
if (builder.Configuration.GetValue<bool>("EnableDataSourceTemplate:team-movements", false))
{
    // Dynamically load the Team Movements plugin assembly
    try
    {
        var pluginAssembly = System.Reflection.Assembly.Load("NLQueryApp.Plugins.TeamMovements");
        var pluginType = pluginAssembly.GetType("NLQueryApp.Plugins.TeamMovements.TeamMovementsPlugin");
        if (pluginType != null)
        {
            builder.Services.AddSingleton(typeof(IDataSourcePlugin), pluginType);
        }
    }
    catch (Exception ex)
    {
        // Log the warning after the app is built instead of creating a service provider early
        builder.Services.AddSingleton<PluginLoadException>(new PluginLoadException 
        { 
            Message = "Team Movements plugin assembly not found. Plugin features will be disabled.",
            Exception = ex
        });
    }
}

var app = builder.Build();

// Log any plugin load exceptions
var pluginLoadException = app.Services.GetService<PluginLoadException>();
if (pluginLoadException != null)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(pluginLoadException.Exception, pluginLoadException.Message);
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    
    // Add developer exception page for better error visibility
    app.UseDeveloperExceptionPage();
}

// Add global exception handling middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception occurred. Path: {Path}", context.Request.Path);
        
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        
        var errorResponse = new
        {
            error = "Internal Server Error",
            message = app.Environment.IsDevelopment() ? ex.Message : "An error occurred processing your request",
            detail = app.Environment.IsDevelopment() ? ex.ToString() : null,
            path = context.Request.Path.ToString()
        };
        
        await context.Response.WriteAsJsonAsync(errorResponse);
    }
});

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
                // Don't throw here - let the app start even if DB init fails
                // The error will be caught when the first request tries to use the DB
            }
        }
    }

    // Load data source templates if configured
    try
    {
        var dataSourceManager = scope.ServiceProvider.GetRequiredService<IDataSourceManager>();
        var dataSources = await dataSourceManager.GetDataSourcesAsync();
        
        logger.LogInformation("Found {Count} existing data sources", dataSources.Count);
        
        // Check if we should create any templates
        var dataSourceTemplates = builder.Configuration.GetSection("DataSourceTemplates").Get<List<DataSourceTemplate>>();
        
        if (dataSourceTemplates != null && dataSourceTemplates.Any())
        {
            logger.LogInformation("Found {Count} data source templates in configuration", dataSourceTemplates.Count);
            
            foreach (var template in dataSourceTemplates)
            {
                // Skip if already exists
                if (dataSources.Any(ds => ds.Id == template.Id))
                {
                    logger.LogInformation("Data source {Id} already exists, skipping template", template.Id);
                    continue;
                }
                
                // Skip optional templates unless explicitly enabled
                if (template.Optional)
                {
                    var enableKey = $"EnableDataSourceTemplate:{template.Id}";
                    if (!builder.Configuration.GetValue<bool>(enableKey, false))
                    {
                        logger.LogInformation("Optional data source template {Id} is not enabled, skipping", template.Id);
                        continue;
                    }
                }
                
                try
                {
                    logger.LogInformation("Creating data source from template: {Id}", template.Id);
                    
                    var dataSource = new DataSourceDefinition
                    {
                        Id = template.Id,
                        Name = template.Name,
                        Description = template.Description,
                        Type = template.Type,
                        ConnectionParameters = new Dictionary<string, string>(template.DefaultConnectionParameters)
                    };
                    
                    // Override with environment-specific values if present
                    foreach (var param in dataSource.ConnectionParameters.Keys.ToList())
                    {
                        var envValue = builder.Configuration[$"DataSources:{template.Id}:{param}"];
                        if (!string.IsNullOrEmpty(envValue))
                        {
                            dataSource.ConnectionParameters[param] = envValue;
                        }
                    }
                    
                    await dataSourceManager.CreateDataSourceAsync(dataSource);
                    logger.LogInformation("Created data source {Id} from template", template.Id);
                    
                    // Load schema context if provided
                    if (!string.IsNullOrEmpty(template.SchemaContextFile) && File.Exists(template.SchemaContextFile))
                    {
                        var context = await File.ReadAllTextAsync(template.SchemaContextFile);
                        await dataSourceManager.SetSchemaContextAsync(template.Id, context);
                        logger.LogInformation("Loaded schema context for data source {Id}", template.Id);
                    }
                    else if (!string.IsNullOrEmpty(template.DefaultSchemaContext))
                    {
                        await dataSourceManager.SetSchemaContextAsync(template.Id, template.DefaultSchemaContext);
                        logger.LogInformation("Set default schema context for data source {Id}", template.Id);
                    }
                    
                    // Setup schema if this is a specialized data source type with a plugin
                    var plugins = scope.ServiceProvider.GetServices<IDataSourcePlugin>();
                    var plugin = plugins.FirstOrDefault(p => p.DataSourceType == template.Type);
                    if (plugin != null)
                    {
                        logger.LogInformation("Found plugin for data source type {Type}, initializing schema", template.Type);
                        var connectionString = dataSource.GetConnectionString();
                        await plugin.InitializeSchemaAsync(connectionString);
                        
                        // Set the default schema context from the plugin
                        var pluginContext = plugin.GetDefaultSchemaContext();
                        if (!string.IsNullOrEmpty(pluginContext))
                        {
                            await dataSourceManager.SetSchemaContextAsync(template.Id, pluginContext);
                            logger.LogInformation("Set plugin-provided schema context for data source {Id}", template.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create data source from template {Id}", template.Id);
                }
            }
        }
        else
        {
            logger.LogInformation("No data source templates configured");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize data sources");
    }
}

app.Run();

// Helper class to store plugin load exceptions
internal class PluginLoadException
{
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
