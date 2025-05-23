namespace NLQueryApp.Core;

/// <summary>
/// Defines the types of models used for different LLM tasks
/// </summary>
public enum ModelType
{
    /// <summary>
    /// Heavy model for complex SQL query generation and reasoning
    /// </summary>
    Query,
    
    /// <summary>
    /// Lightweight model for quick utility tasks like title generation, classifications
    /// </summary>
    Utility,
    
    /// <summary>
    /// Model for generating summaries and condensing content
    /// </summary>
    Summary
}