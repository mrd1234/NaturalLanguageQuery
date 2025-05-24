using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Api.Models;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly IDatabaseService _dbService;
    private readonly IDataSourceManager _dataSourceManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationController> _logger;

    public ConversationController(
        IDatabaseService dbService, 
        IDataSourceManager dataSourceManager,
        IConfiguration configuration, 
        ILogger<ConversationController> logger)
    {
        _dbService = dbService;
        _dataSourceManager = dataSourceManager;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Conversation>>> GetConversations()
    {
        return await _dbService.GetConversationsAsync();
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(int id)
    {
        try
        {
            var conversation = await _dbService.GetConversationAsync(id);
            return conversation;
        }
        catch (Exception)
        {
            return NotFound();
        }
    }
    
    [HttpGet("{id}/title")]
    public async Task<ActionResult<object>> GetConversationTitle(int id)
    {
        try
        {
            var conversation = await _dbService.GetConversationAsync(id);
            return Ok(new { title = conversation.Title });
        }
        catch (Exception)
        {
            return NotFound();
        }
    }
    
    [HttpPost]
    public async Task<ActionResult<Conversation>> CreateConversation([FromBody] CreateConversationRequest request)
    {
        var result = await _dbService.CreateConversationAsync(request.Title, request.DataSourceId);
        return CreatedAtAction(nameof(GetConversation), new { id = result.Id }, result);
    }
    
    [HttpPost("{conversationId}/messages")]
    public async Task<ActionResult<AddMessageResponse>> AddMessage(
        int conversationId, 
        [FromBody] AddMessageRequest request)
    {
        try
        {
            var message = new ChatMessage
            {
                Role = request.Role,
                Content = request.Content,
                DataSourceId = request.DataSourceId,
                SqlQuery = request.SqlQuery,
                QuerySuccess = request.QuerySuccess
            };
            
            // Add the message first
            var result = await _dbService.AddMessageAsync(conversationId, message);
            
            var response = new AddMessageResponse
            {
                Message = result,
                TitleGenerationInProgress = false
            };
            
            // Check if this is the first user message and trigger title generation asynchronously
            if (message.Role == "user" && !string.IsNullOrWhiteSpace(message.Content) && !string.IsNullOrWhiteSpace(request.DataSourceId))
            {
                // Check if we should generate a title
                var conversation = await _dbService.GetConversationAsync(conversationId);
                var userMessageCount = conversation.Messages?.Count(m => m.Role == "user") ?? 0;
                
                if (conversation.Title == "New Conversation" && userMessageCount == 1)
                {
                    response.TitleGenerationInProgress = true;
                    
                    // Use data source manager for title generation
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            var title = await _dataSourceManager.GenerateTitleAsync(
                                request.DataSourceId, 
                                message.Content,
                                _configuration["LlmSettings:DefaultService"]);
                            
                            await _dbService.UpdateConversationTitleAsync(conversationId, title);
                            _logger.LogInformation("Successfully generated title for conversation {ConversationId}: {Title}", 
                                conversationId, title);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to generate title for conversation {ConversationId}", conversationId);
                        }
                    });
                }
            }
            
            return CreatedAtAction(nameof(GetConversation), new { id = conversationId }, response);
        }
        catch (Exception)
        {
            return NotFound();
        }
    }
    
    [HttpPut("{conversationId}/title")]
    public async Task<ActionResult<Conversation>> UpdateConversationTitle(int conversationId, [FromBody] UpdateTitleRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { error = "Title cannot be empty" });
            }
            
            if (request.Title.Length > 100)
            {
                return BadRequest(new { error = "Title cannot exceed 100 characters" });
            }
            
            var success = await _dbService.UpdateConversationTitleAsync(conversationId, request.Title.Trim());
            
            if (!success)
            {
                return NotFound();
            }
            
            var conversation = await _dbService.GetConversationAsync(conversationId);
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation title for conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = "Failed to update conversation title" });
        }
    }
    
    [HttpGet("{conversationId}/query-history")]
    public async Task<ActionResult<List<QueryHistoryItem>>> GetQueryHistory(int conversationId, [FromQuery] string? dataSourceId = null)
    {
        try
        {
            var conversation = await _dbService.GetConversationAsync(conversationId);
            
            var queries = conversation.Messages
                .Where(m => m.Role == "user" && 
                           (dataSourceId == null || m.DataSourceId == dataSourceId))
                .Select((m, index) => new QueryHistoryItem
                {
                    Index = index + 1,
                    Question = m.Content,
                    Timestamp = m.Timestamp,
                    DataSourceId = m.DataSourceId,
                    SqlQuery = conversation.Messages
                        .FirstOrDefault(r => r.Role == "assistant" && 
                                           r.Timestamp > m.Timestamp)?
                        .SqlQuery,
                    Success = conversation.Messages
                        .FirstOrDefault(r => r.Role == "assistant" && 
                                           r.Timestamp > m.Timestamp)?
                        .QuerySuccess
                })
                .ToList();
            
            return Ok(queries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting query history for conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = "Failed to retrieve query history" });
        }
    }
}

public class UpdateTitleRequest
{
    public string Title { get; set; } = string.Empty;
}
