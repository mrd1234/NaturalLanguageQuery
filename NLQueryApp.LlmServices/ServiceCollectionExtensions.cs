using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NLQueryApp.LlmServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register both LLM services
        services.AddHttpClient<AnthropicService>();
        services.AddHttpClient<OllamaService>();
        // Register service factory
        services.AddSingleton<LlmServiceFactory>();
    
        return services;
    }
}