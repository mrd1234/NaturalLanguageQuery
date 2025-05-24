namespace NLQueryApp.Core.Models;

/// <summary>
/// Provides domain-specific context to enhance title generation for queries
/// </summary>
public class TitleGenerationContext
{
    /// <summary>
    /// Common abbreviations used in this domain (e.g., "PL" => "Premier League")
    /// </summary>
    public Dictionary<string, string> Abbreviations { get; set; } = new();
    
    /// <summary>
    /// Key terms commonly used in this domain
    /// </summary>
    public List<string> KeyTerms { get; set; } = new();
    
    /// <summary>
    /// Main entities in the domain with descriptions
    /// </summary>
    public List<(string Entity, string Description)> MainEntities { get; set; } = new();
    
    /// <summary>
    /// Example titles that represent good patterns for this domain
    /// </summary>
    public List<string> ExampleTitles { get; set; } = new();
    
    /// <summary>
    /// Additional context or rules for title generation
    /// </summary>
    public string? AdditionalContext { get; set; }
    
    /// <summary>
    /// Whether this context has meaningful content
    /// </summary>
    public bool HasContent => 
        (Abbreviations?.Count > 0) ||
        (KeyTerms?.Count > 0) ||
        (MainEntities?.Count > 0) ||
        (ExampleTitles?.Count > 0) ||
        !string.IsNullOrWhiteSpace(AdditionalContext);
}
