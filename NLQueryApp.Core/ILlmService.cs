namespace NLQueryApp.Core;

public interface ILlmService
{
    Task<LlmQueryResponse> GenerateSqlQueryAsync(LlmQueryRequest request);
}