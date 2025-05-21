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
                // Simple query to test database connectivity
                var result = await dbService.ExecuteSqlQueryAsync("SELECT 1 as HealthCheck");
                return result.Success ? Ok(new { status = "healthy", database = "connected" }) : StatusCode(500, new { status = "unhealthy", database = "error", message = result.ErrorMessage });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check failed");
                return StatusCode(500, new { status = "unhealthy", message = ex.Message });
            }
        }
    }
}