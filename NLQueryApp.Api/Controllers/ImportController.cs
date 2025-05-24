using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImportController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ImportController(IConfiguration configuration, ILogger<ImportController> logger, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    [HttpPost("{pluginName}")]
    public async Task<IActionResult> ImportData(string pluginName)
    {
        try
        {
            _logger.LogInformation("Import request for plugin: {PluginName}", pluginName);
            
            // Find the plugin
            var plugin = _serviceProvider.GetServices<IDataSourcePlugin>()
                .FirstOrDefault(p => p.DataSourceType.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            
            if (plugin == null)
            {
                return NotFound(new { success = false, message = $"Plugin '{pluginName}' not found" });
            }
            
            // Get configuration for the import
            var directoryPath = _configuration[$"ImportSettings:{pluginName}:DirectoryPath"] 
                ?? _configuration["ImportSettings:DirectoryPath"];
                
            if (string.IsNullOrEmpty(directoryPath))
            {
                return BadRequest(new { success = false, message = "Import directory path not configured" });
            }
            
            // Validate directory exists
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogError("Directory not found: {DirectoryPath}", directoryPath);
                return BadRequest(new { success = false, message = $"Import directory not found: {directoryPath}" });
            }
            
            // Get connection string
            var connectionString = _configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string not configured");
            
            _logger.LogInformation("Starting import from {DirectoryPath} using plugin {PluginName}", 
                directoryPath, plugin.PluginName);
            
            // Execute the import
            await plugin.ImportDataAsync(connectionString, directoryPath);
            
            return Ok(new {
                success = true,
                message = $"Import completed successfully using {plugin.PluginName} plugin"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import process failed for plugin {PluginName}", pluginName);
            
            return StatusCode(500, new { 
                success = false,
                error = ex.Message,
                errorType = ex.GetType().Name
            });
        }
    }
    
    [HttpGet("plugins")]
    public ActionResult<List<object>> GetAvailablePlugins()
    {
        try
        {
            var plugins = _serviceProvider.GetServices<IDataSourcePlugin>()
                .Select(p => new {
                    name = p.DataSourceType,
                    displayName = p.PluginName,
                    description = $"Import plugin for {p.PluginName}"
                })
                .ToList();
            
            return Ok(plugins);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available import plugins");
            return StatusCode(500, new { error = "Failed to retrieve plugins" });
        }
    }
}
