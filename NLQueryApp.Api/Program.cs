using NLQueryApp.Api.Services;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.Data; // Make sure this is imported
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
        policy.WithOrigins("http://localhost:5297", "https://localhost:5297", "http://localhost:5101", "https://localhost:5101","http://localhost:44359", "https://localhost:44359")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Register services
builder.Services.AddDataServices(); // This should work now
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
                // Increase delay for next retry (exponential backoff)
                retryDelayMs *= 2;
            }
            else
            {
                logger.LogCritical(ex, "Database initialization failed after {MaxRetries} attempts", maxRetries);
                // Allow app to start but in a degraded state - may not work properly without DB
            }
        }
    }

    // Initialize default data source if none exists
    try
    {
        var dataSourceManager = scope.ServiceProvider.GetRequiredService<IDataSourceManager>();
        var dataSources = await dataSourceManager.GetDataSourcesAsync();
        
        if (!dataSources.Any())
        {
            logger.LogInformation("No data sources found. Creating default data source...");
            
            var defaultDataSource = new DataSourceDefinition
            {
                Id = "default",
                Name = "Default PostgreSQL",
                Description = "Default PostgreSQL data source created on application startup",
                Type = "postgres",
                ConnectionParameters = new Dictionary<string, string>
                {
                    {"Host", builder.Configuration.GetValue<string>("DefaultDataSource:Host") ?? "localhost"},
                    {"Port", builder.Configuration.GetValue<string>("DefaultDataSource:Port") ?? "5432"},
                    {"Database", builder.Configuration.GetValue<string>("DefaultDataSource:Database") ?? "nlquery"},
                    {"Username", builder.Configuration.GetValue<string>("DefaultDataSource:Username") ?? "postgres"},
                    {"Password", builder.Configuration.GetValue<string>("DefaultDataSource:Password") ?? "postgres"},
                    {"IncludedSchemas", "team_movements,lookup"},
                }
            };
            
            await dataSourceManager.CreateDataSourceAsync(defaultDataSource);
            logger.LogInformation("Default data source created successfully");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize default data source");
    }
}

app.Run();