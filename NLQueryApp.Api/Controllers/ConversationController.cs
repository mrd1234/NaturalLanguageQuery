using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController(IDatabaseService dbService) : ControllerBase
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
    
    [HttpPost]
    public async Task<ActionResult<Conversation>> CreateConversation([FromBody] Conversation conversation)
    {
        var result = await dbService.CreateConversationAsync(conversation.Title);
        return CreatedAtAction(nameof(GetConversation), new { id = result.Id }, result);
    }
    
    [HttpPost("{conversationId}/messages")]
    public async Task<ActionResult<ChatMessage>> AddMessage(int conversationId, [FromBody] ChatMessage message)
    {
        try
        {
            var result = await dbService.AddMessageAsync(conversationId, message);
            return CreatedAtAction(nameof(GetConversation), new { id = conversationId }, result);
        }
        catch (Exception)
        {
            return NotFound();
        }
    }
}