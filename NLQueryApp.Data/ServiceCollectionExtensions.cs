using Microsoft.Extensions.DependencyInjection;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using NLQueryApp.Data.Providers;
using NLQueryApp.Data.Services;

namespace NLQueryApp.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        // Register base data source providers
        services.AddSingleton<PostgresDataSourceProvider>();
        services.AddSingleton<SqlServerDataSourceProvider>();
        // Add more base providers as they are implemented:
        // services.AddSingleton<MySqlDataSourceProvider>();
        // services.AddSingleton<MongoDbDataSourceProvider>();
        
        // Register specialized data source providers
        services.AddSingleton<TeamMovementsDataSourceProvider>();
        
        // Register all providers as IDataSourceProvider for injection into DataSourceManager
        services.AddSingleton<IDataSourceProvider>(sp => sp.GetRequiredService<PostgresDataSourceProvider>());
        services.AddSingleton<IDataSourceProvider>(sp => sp.GetRequiredService<TeamMovementsDataSourceProvider>());
        // Uncomment as providers are implemented:
        // services.AddSingleton<IDataSourceProvider>(sp => sp.GetRequiredService<SqlServerDataSourceProvider>());
        // services.AddSingleton<IDataSourceProvider>(sp => sp.GetRequiredService<MySqlDataSourceProvider>());
        // services.AddSingleton<IDataSourceProvider>(sp => sp.GetRequiredService<MongoDbDataSourceProvider>());
        
        // Register data source manager
        services.AddSingleton<IDataSourceManager, DataSourceManager>();
        
        // Register database service (for app's own database)
        services.AddSingleton<IDatabaseService, DatabaseService>();
        
        return services;
    }
}