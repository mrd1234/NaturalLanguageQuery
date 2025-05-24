using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Core;

namespace NLQueryApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController(IDatabaseService dbService, ILogger<HealthController> logger)
        : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                // Try to get available schemas as a health check
                var schemas = await dbService.GetAvailableSchemasAsync();
                return Ok(new { status = "healthy", database = "connected", schemaCount = schemas.Count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check failed");
                return StatusCode(500, new { status = "unhealthy", message = ex.Message });
            }
        }
    }
}