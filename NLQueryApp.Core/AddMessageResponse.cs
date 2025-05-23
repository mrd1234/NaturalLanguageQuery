namespace NLQueryApp.Core;

public class AddMessageResponse
{
    public ChatMessage Message { get; set; } = new();
    public bool TitleGenerationInProgress { get; set; }
}