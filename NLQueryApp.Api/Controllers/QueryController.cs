using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Api.Models;
using NLQueryApp.Api.Services;
using NLQueryApp.Core;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QueryController : ControllerBase
{
    private readonly QueryService _queryService;
    private readonly ILogger<QueryController> _logger;

    public QueryController(QueryService queryService, ILogger<QueryController> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<QueryResult>> ProcessQuery([FromBody] NaturalLanguageQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Question))
        {
            return BadRequest(new { error = "Question cannot be empty" });
        }
    
        if (string.IsNullOrWhiteSpace(query.DataSourceId))
        {
            return BadRequest(new { error = "Data source ID is required" });
        }
    
        try
        {
            var result = await _queryService.ProcessNaturalLanguageQueryAsync(
                query.DataSourceId,
                query.Question,
                query.LlmService,
                query.ConversationId); // Pass conversation context
        
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {query.DataSourceId} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing query for data source {DataSourceId}", query.DataSourceId);
            return StatusCode(500, new { error = "Failed to process query" });
        }
    }
}