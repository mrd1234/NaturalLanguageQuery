using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Core;
using NLQueryApp.LlmServices;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController(IDatabaseService dbService, LlmServiceFactory llmServiceFactory, IConfiguration configuration, ILogger<ConversationController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Conversation>>> GetConversations()
    {
        return await dbService.GetConversationsAsync();
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(int id)
    {
        try
        {
            var conversation = await dbService.GetConversationAsync(id);
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
            var conversation = await dbService.GetConversationAsync(id);
            return Ok(new { title = conversation.Title });
        }
        catch (Exception)
        {
            return NotFound();
        }
    }
    
    [HttpPost]
    public async Task<ActionResult<Conversation>> CreateConversation([FromBody] Conversation conversation)
    {
        var result = await dbService.CreateConversationAsync(conversation.Title);
        return CreatedAtAction(nameof(GetConversation), new { id = result.Id }, result);
    }
    
    [HttpPost("{conversationId}/messages")]
    public async Task<ActionResult<AddMessageResponse>> AddMessage(int conversationId, [FromBody] ChatMessage message)
    {
        try
        {
            // Add the message first
            var result = await dbService.AddMessageAsync(conversationId, message);
            
            var response = new AddMessageResponse
            {
                Message = result,
                TitleGenerationInProgress = false
            };
            
            // Check if this is the first user message and trigger title generation asynchronously
            if (message.Role == "user" && !string.IsNullOrWhiteSpace(message.Content))
            {
                // Check if we should generate a title
                var conversation = await dbService.GetConversationAsync(conversationId);
                var userMessageCount = conversation.Messages?.Count(m => m.Role == "user") ?? 0;
                
                if (conversation.Title == "New Conversation" && userMessageCount == 1)
                {
                    response.TitleGenerationInProgress = true;
                    
                    // Start title generation asynchronously without awaiting
                    _ = Task.Run(async () => await TryGenerateConversationTitleAsync(conversationId, message.Content));
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
            
            var success = await dbService.UpdateConversationTitleAsync(conversationId, request.Title.Trim());
            
            if (!success)
            {
                return NotFound();
            }
            
            var conversation = await dbService.GetConversationAsync(conversationId);
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating conversation title for conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = "Failed to update conversation title" });
        }
    }
    
    private async Task TryGenerateConversationTitleAsync(int conversationId, string userMessage)
    {
        try
        {
            logger.LogInformation("Starting title generation for conversation {ConversationId}", conversationId);
            
            // Get the conversation to check current title and message count
            var conversation = await dbService.GetConversationAsync(conversationId);
            
            // Only generate title if it's still "New Conversation" and we have exactly 1 user message
            var userMessageCount = conversation.Messages?.Count(m => m.Role == "user") ?? 0;
            if (conversation.Title != "New Conversation" || userMessageCount > 1)
            {
                logger.LogInformation("Skipping title generation - conversation {ConversationId} already has custom title or multiple messages", conversationId);
                return;
            }
            
            // Generate title using LLM or fallback
            var titleToUse = await GenerateConversationTitle(userMessage);
            
            // Update conversation title
            await UpdateConversationTitleSafely(conversationId, titleToUse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during title generation for conversation {ConversationId}", conversationId);
        }
    }
    
    private async Task<string> GenerateConversationTitle(string userMessage)
    {
        // Get the default LLM service
        var defaultServiceName = configuration["LlmSettings:DefaultService"] ?? "ollama";
        
        try
        {
            var llmService = llmServiceFactory.GetService(defaultServiceName);
            
            // Check if the service has a utility model configured
            if (!llmService.HasModel(ModelType.Utility))
            {
                logger.LogWarning("LLM service {ServiceName} doesn't have utility model configured, using fallback title generation", defaultServiceName);
                return GenerateFallbackTitle(userMessage);
            }
            
            // Generate title with reduced timeout (10 seconds instead of 15)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            try
            {
                var titleTask = llmService.GenerateTitleAsync(userMessage);
                var completedTask = await Task.WhenAny(titleTask, Task.Delay(10000, cts.Token));
                
                if (completedTask == titleTask)
                {
                    var generatedTitle = await titleTask;
                    
                    if (!string.IsNullOrWhiteSpace(generatedTitle) && generatedTitle != "New Conversation")
                    {
                        // Sanitize and validate the title
                        var sanitizedTitle = SanitizeTitle(generatedTitle);
                        if (!string.IsNullOrWhiteSpace(sanitizedTitle))
                        {
                            logger.LogInformation("Successfully generated title for conversation: {Title}", sanitizedTitle);
                            return sanitizedTitle;
                        }
                    }
                    
                    logger.LogWarning("Generated title was empty or unchanged, using fallback");
                }
                else
                {
                    logger.LogWarning("Title generation timed out after 10 seconds, using fallback");
                    cts.Cancel();
                }
            }
            catch (Exception titleEx)
            {
                logger.LogWarning(titleEx, "Error during title generation, using fallback");
            }
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning("LLM service {ServiceName} not available for title generation: {Error}, using fallback", defaultServiceName, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting LLM service {ServiceName}, using fallback", defaultServiceName);
        }
        
        // Fallback to simple title generation
        return GenerateFallbackTitle(userMessage);
    }
    
    private async Task UpdateConversationTitleSafely(int conversationId, string title)
    {
        try
        {
            var success = await dbService.UpdateConversationTitleAsync(conversationId, title);
            if (!success)
            {
                logger.LogWarning("Failed to update title in database for conversation {ConversationId}", conversationId);
            }
            else
            {
                logger.LogInformation("Successfully updated title for conversation {ConversationId}: {Title}", conversationId, title);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating conversation title for conversation {ConversationId}", conversationId);
        }
    }
    
    private string GenerateFallbackTitle(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return "New Conversation";

        // Clean the question
        var cleaned = userMessage.Trim();
        
        // Remove common question starters to save space
        var commonStarters = new[] { "how do i ", "how can i ", "what is ", "what are ", "show me ", "find ", "get ", "list ", "count " };
        foreach (var starter in commonStarters)
        {
            if (cleaned.StartsWith(starter, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(starter.Length);
                break;
            }
        }

        // Truncate at word boundary
        if (cleaned.Length <= 50)
            return SanitizeTitle(cleaned);

        var truncated = cleaned.Substring(0, 47);
        var lastSpace = truncated.LastIndexOf(' ');
        
        if (lastSpace > 20) // Don't truncate too aggressively
        {
            truncated = truncated.Substring(0, lastSpace);
        }
        
        return SanitizeTitle(truncated + "...");
    }
    
    private string SanitizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "New Conversation";
            
        // Remove problematic characters and clean up
        var sanitized = title.Trim()
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("\n", " ")
            .Replace("\r", "")
            .Replace("\t", " ");
        
        // Collapse multiple spaces
        while (sanitized.Contains("  "))
            sanitized = sanitized.Replace("  ", " ");
        
        // Ensure reasonable length
        if (sanitized.Length > 80)
        {
            sanitized = sanitized.Substring(0, 77) + "...";
        }
        
        return string.IsNullOrWhiteSpace(sanitized) ? "New Conversation" : sanitized;
    }
}

public class UpdateTitleRequest
{
    public string Title { get; set; } = string.Empty;
}