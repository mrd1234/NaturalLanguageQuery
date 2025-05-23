using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

namespace NLQueryApp.LlmServices;

public class LlmServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LlmServiceFactory> _logger;

    public LlmServiceFactory(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = loggerFactory.CreateLogger<LlmServiceFactory>();
    }
    
    public ILlmService GetService(string serviceName)
    {
        _logger.LogInformation("Getting LLM service: {ServiceName}", serviceName);
        
        return serviceName.ToLower() switch
        {
            "anthropic" => _serviceProvider.GetRequiredService<AnthropicService>(),
            "ollama" => _serviceProvider.GetRequiredService<OllamaService>(),
            "openai" => _serviceProvider.GetRequiredService<OpenAIService>(),
            "gemini" => _serviceProvider.GetRequiredService<GeminiService>(),
            _ => throw new ArgumentException($"Unknown LLM service: {serviceName}")
        };
    }
    
    public List<(string Name, string DisplayName, bool IsAvailable)> GetAvailableServices()
    {
        var services = new List<(string Name, string DisplayName, bool IsAvailable)>();
        
        _logger.LogInformation("Checking LLM service availability");
        
        // Check each service
        var serviceConfigs = new[]
        {
            ("anthropic", "Anthropic Claude"),
            ("ollama", "Ollama (Local)"),
            ("openai", "OpenAI GPT-4"),
            ("gemini", "Google Gemini")
        };
        
        foreach (var (name, displayName) in serviceConfigs)
        {
            try
            {
                _logger.LogInformation("Checking service: {ServiceName}", name);
                
                var service = GetService(name);
                var isAvailable = service.HasApiKey();
                
                _logger.LogInformation("Service {ServiceName} availability: {IsAvailable}", name, isAvailable);
                
                services.Add((name, displayName, isAvailable));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check service {ServiceName}", name);
                services.Add((name, displayName, false));
            }
        }
        
        _logger.LogInformation("Total services checked: {Count}, Available: {AvailableCount}", 
            services.Count, services.Count(s => s.IsAvailable));
        
        return services;
    }
}