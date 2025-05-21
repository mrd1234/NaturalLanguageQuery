using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataSourceController : ControllerBase
{
    private readonly IDataSourceManager _dataSourceManager;
    private readonly ILogger<DataSourceController> _logger;

    public DataSourceController(IDataSourceManager dataSourceManager, ILogger<DataSourceController> logger)
    {
        _dataSourceManager = dataSourceManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<DataSourceDefinition>>> GetDataSources()
    {
        try
        {
            return await _dataSourceManager.GetDataSourcesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data sources");
            return StatusCode(500, new { error = "Failed to retrieve data sources" });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DataSourceDefinition>> GetDataSource(string id)
    {
        try
        {
            var dataSource = await _dataSourceManager.GetDataSourceAsync(id);
            
            // For security, don't return connection parameters to the client
            dataSource.ConnectionParameters = new Dictionary<string, string>();
            
            return dataSource;
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to retrieve data source" });
        }
    }

    [HttpPost]
    public async Task<ActionResult<DataSourceDefinition>> CreateDataSource([FromBody] DataSourceDefinition dataSource)
    {
        try
        {
            var result = await _dataSourceManager.CreateDataSourceAsync(dataSource);
            
            // For security, don't return connection parameters to the client
            result.ConnectionParameters = new Dictionary<string, string>();
            
            return CreatedAtAction(nameof(GetDataSource), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating data source");
            return StatusCode(500, new { error = "Failed to create data source" });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DataSourceDefinition>> UpdateDataSource(string id, [FromBody] DataSourceDefinition dataSource)
    {
        try
        {
            var result = await _dataSourceManager.UpdateDataSourceAsync(id, dataSource);
            
            // For security, don't return connection parameters to the client
            result.ConnectionParameters = new Dictionary<string, string>();
            
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to update data source" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDataSource(string id)
    {
        try
        {
            var result = await _dataSourceManager.DeleteDataSourceAsync(id);
            
            if (!result)
                return NotFound(new { error = $"Data source with ID {id} not found" });
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to delete data source" });
        }
    }

    [HttpGet("{id}/schema")]
    public async Task<ActionResult<string>> GetDataSourceSchema(string id)
    {
        try
        {
            var schema = await _dataSourceManager.GetSchemaAsync(id);
            return Ok(new { schema });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving schema for data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to retrieve schema" });
        }
    }

    [HttpGet("{id}/context")]
    public async Task<ActionResult<string>> GetDataSourceContext(string id)
    {
        try
        {
            var context = await _dataSourceManager.GetSchemaContextAsync(id);
            return Ok(new { context });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving context for data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to retrieve context" });
        }
    }

    [HttpPut("{id}/context")]
    public async Task<ActionResult> SetDataSourceContext(string id, [FromBody] string context)
    {
        try
        {
            var result = await _dataSourceManager.SetSchemaContextAsync(id, context);
            
            if (!result)
                return NotFound(new { error = $"Data source with ID {id} not found" });
            
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting context for data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to set context" });
        }
    }

    [HttpGet("{id}/test")]
    public async Task<ActionResult> TestDataSourceConnection(string id)
    {
        try
        {
            var result = await _dataSourceManager.TestConnectionAsync(id);
            return Ok(new { success = result });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Data source with ID {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection for data source {DataSourceId}", id);
            return StatusCode(500, new { error = "Failed to test connection" });
        }
    }
}