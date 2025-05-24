namespace NLQueryApp.Core.Utilities;

public static class TitleGenerationUtility
{
    public static string SanitizeTitle(string title)
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
    
    public static string GenerateFallbackTitle(string userQuestion, string dataSourceName)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
            return "New Conversation";

        // Clean the question
        var cleaned = userQuestion.Trim();
        
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

        // Add data source context if provided
        if (!string.IsNullOrEmpty(dataSourceName) && dataSourceName != "Unknown")
        {
            cleaned = $"{cleaned} ({dataSourceName})";
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
}
