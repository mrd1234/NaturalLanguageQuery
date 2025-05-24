using Microsoft.AspNetCore.Mvc;
using NLQueryApp.LlmServices;
using System.Net.Http;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LlmController : ControllerBase
{
    private readonly LlmServiceFactory _llmServiceFactory;
    private readonly ILogger<LlmController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmController(
        LlmServiceFactory llmServiceFactory, 
        ILogger<LlmController> logger, 
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _llmServiceFactory = llmServiceFactory;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("services")]
    public ActionResult<List<object>> GetAvailableServices()
    {
        try
        {
            _logger.LogInformation("Getting available LLM services");
            
            var services = _llmServiceFactory.GetAvailableServices();
            
            _logger.LogInformation("Found {Count} LLM services", services.Count);
            
            foreach (var service in services)
            {
                _logger.LogInformation("Service {Name}: Available={IsAvailable}", service.Name, service.IsAvailable);
            }
            
            return Ok(services.Select(s => new
            {
                name = s.Name,
                displayName = s.DisplayName,
                isAvailable = s.IsAvailable
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available LLM services");
            return StatusCode(500, new { error = "Failed to retrieve LLM services" });
        }
    }
    
    [HttpGet("config")]
    public ActionResult GetLlmConfig()
    {
        try
        {
            var config = new
            {
                anthropic = new
                {
                    hasKey = !string.IsNullOrEmpty(_configuration["LlmSettings:Anthropic:ApiKey"]),
                    models = new
                    {
                        query = _configuration["LlmSettings:Anthropic:Models:Query"] ?? "not configured",
                        utility = _configuration["LlmSettings:Anthropic:Models:Utility"] ?? "not configured",
                        summary = _configuration["LlmSettings:Anthropic:Models:Summary"] ?? "not configured"
                    }
                },
                openai = new
                {
                    hasKey = !string.IsNullOrEmpty(_configuration["LlmSettings:OpenAI:ApiKey"]),
                    models = new
                    {
                        query = _configuration["LlmSettings:OpenAI:Models:Query"] ?? "not configured",
                        utility = _configuration["LlmSettings:OpenAI:Models:Utility"] ?? "not configured",
                        summary = _configuration["LlmSettings:OpenAI:Models:Summary"] ?? "not configured"
                    }
                },
                gemini = new
                {
                    hasKey = !string.IsNullOrEmpty(_configuration["LlmSettings:Gemini:ApiKey"]),
                    models = new
                    {
                        query = _configuration["LlmSettings:Gemini:Models:Query"] ?? "not configured",
                        utility = _configuration["LlmSettings:Gemini:Models:Utility"] ?? "not configured",
                        summary = _configuration["LlmSettings:Gemini:Models:Summary"] ?? "not configured"
                    }
                },
                ollama = new
                {
                    baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "not configured",
                    models = new
                    {
                        query = _configuration["LlmSettings:Ollama:Models:Query"] ?? "not configured",
                        utility = _configuration["LlmSettings:Ollama:Models:Utility"] ?? "not configured",
                        summary = _configuration["LlmSettings:Ollama:Models:Summary"] ?? "not configured"
                    }
                },
                defaultService = _configuration["LlmSettings:DefaultService"] ?? "not configured"
            };
            
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LLM configuration");
            return StatusCode(500, new { error = "Failed to retrieve LLM configuration" });
        }
    }
    
    [HttpGet("health/{serviceName}")]
    public async Task<ActionResult> CheckServiceHealth(string serviceName)
    {
        try
        {
            _logger.LogInformation("Checking health for service: {ServiceName}", serviceName);
            
            if (serviceName.ToLower() == "ollama")
            {
                var baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "http://localhost:11434";
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                try
                {
                    // Try to reach Ollama's version endpoint
                    var response = await httpClient.GetAsync($"{baseUrl}/api/version");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var version = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation("Ollama is reachable. Version response: {Version}", version);
                        
                        // Also check if we can list models
                        var modelsResponse = await httpClient.GetAsync($"{baseUrl}/api/tags");
                        var models = await modelsResponse.Content.ReadAsStringAsync();
                        
                        return Ok(new 
                        { 
                            status = "healthy", 
                            baseUrl = baseUrl,
                            version = version,
                            modelsAvailable = modelsResponse.IsSuccessStatusCode,
                            models = modelsResponse.IsSuccessStatusCode ? models : null
                        });
                    }
                    else
                    {
                        _logger.LogWarning("Ollama returned status code: {StatusCode}", response.StatusCode);
                        return Ok(new { status = "unhealthy", error = $"Status code: {response.StatusCode}", baseUrl = baseUrl });
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Cannot reach Ollama at {BaseUrl}", baseUrl);
                    return Ok(new { status = "unreachable", error = ex.Message, baseUrl = baseUrl });
                }
                catch (TaskCanceledException)
                {
                    _logger.LogError("Timeout reaching Ollama at {BaseUrl}", baseUrl);
                    return Ok(new { status = "timeout", error = "Connection timeout", baseUrl = baseUrl });
                }
            }
            
            // For other services, just check if they're configured
            var service = _llmServiceFactory.GetService(serviceName);
            var details = _llmServiceFactory.GetServiceDetails(serviceName);
            
            return Ok(new 
            { 
                status = service.HasApiKey() ? "configured" : "not configured",
                details = details
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for service {ServiceName}", serviceName);
            return StatusCode(500, new { error = "Failed to check service health", message = ex.Message });
        }
    }
}
