using Microsoft.Extensions.DependencyInjection;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.Data.Providers;
using NLQueryApp.Data.Services;

namespace NLQueryApp.Data; // Make sure this matches the using directive in Program.cs

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        // Register data source providers
        services.AddSingleton<IDataSourceProvider, PostgresDataSourceProvider>();
        // Add more providers as they are implemented:
        // services.AddSingleton<IDataSourceProvider, SqlServerDataSourceProvider>();
        // services.AddSingleton<IDataSourceProvider, MySqlDataSourceProvider>();
        // services.AddSingleton<IDataSourceProvider, MongoDbDataSourceProvider>();
        
        // Register data source manager
        services.AddSingleton<IDataSourceManager, DataSourceManager>();
        
        // Register database service (for app's own database)
        services.AddSingleton<IDatabaseService, DatabaseService>();
        
        return services;
    }
}