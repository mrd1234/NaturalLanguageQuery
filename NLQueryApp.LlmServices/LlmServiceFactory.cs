using Microsoft.Extensions.DependencyInjection;
using NLQueryApp.Core;

namespace NLQueryApp.LlmServices;

public class LlmServiceFactory(IServiceProvider serviceProvider)
{
    public ILlmService GetService(string serviceName)
    {
        return serviceName.ToLower() switch
        {
            "anthropic" => serviceProvider.GetRequiredService<AnthropicService>(),
            "ollama" => serviceProvider.GetRequiredService<OllamaService>(),
            _ => throw new ArgumentException($"Unknown LLM service: {serviceName}")
        };
    }
}