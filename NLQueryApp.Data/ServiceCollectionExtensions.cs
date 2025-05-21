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
        
        // Register data source manager
        services.AddSingleton<IDataSourceManager, DataSourceManager>();
        
        // Register database service
        services.AddSingleton<IDatabaseService, DatabaseService>();
        
        return services;
    }
}