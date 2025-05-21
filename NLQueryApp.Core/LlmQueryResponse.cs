namespace NLQueryApp.Core;

public class LlmQueryResponse
{
    public string SqlQuery { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

