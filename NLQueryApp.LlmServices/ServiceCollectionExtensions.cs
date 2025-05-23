using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NLQueryApp.LlmServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register all LLM services
        services.AddHttpClient<AnthropicService>();
        services.AddHttpClient<OllamaService>();
        services.AddHttpClient<OpenAIService>();
        services.AddHttpClient<GeminiService>();
        
        // Register service factory with proper DI
        services.AddSingleton<LlmServiceFactory>();
    
        return services;
    }
}