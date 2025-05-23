using Microsoft.AspNetCore.Mvc;
using NLQueryApp.LlmServices;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LlmController : ControllerBase
{
    private readonly LlmServiceFactory _llmServiceFactory;
    private readonly ILogger<LlmController> _logger;
    private readonly IConfiguration _configuration;

    public LlmController(LlmServiceFactory llmServiceFactory, ILogger<LlmController> logger, IConfiguration configuration)
    {
        _llmServiceFactory = llmServiceFactory;
        _logger = logger;
        _configuration = configuration;
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
                    model = _configuration["LlmSettings:Anthropic:Model"] ?? "not configured"
                },
                openai = new
                {
                    hasKey = !string.IsNullOrEmpty(_configuration["LlmSettings:OpenAI:ApiKey"]),
                    model = _configuration["LlmSettings:OpenAI:Model"] ?? "not configured"
                },
                gemini = new
                {
                    hasKey = !string.IsNullOrEmpty(_configuration["LlmSettings:Gemini:ApiKey"]),
                    model = _configuration["LlmSettings:Gemini:Model"] ?? "not configured"
                },
                ollama = new
                {
                    baseUrl = _configuration["LlmSettings:Ollama:BaseUrl"] ?? "not configured",
                    model = _configuration["LlmSettings:Ollama:Model"] ?? "not configured"
                }
            };
            
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LLM configuration");
            return StatusCode(500, new { error = "Failed to retrieve LLM configuration" });
        }
    }
}