using Microsoft.Extensions.Logging;
using NLQueryApp.Core;

public class LlmRetryHandler
{
    private readonly ILogger _logger;
    private static readonly Dictionary<string, DateTime> _lastRequestTimes = new();
    private static readonly object _lock = new object();

    public LlmRetryHandler(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<LlmQueryResponse> ExecuteWithRetryAsync(
        Func<Task<LlmQueryResponse>> operation, 
        string serviceName)
    {
        const int maxRetries = 3;
        var baseDelay = serviceName.ToLower() == "anthropic" ? 1500 : 500; // 1.5s for Anthropic, 0.5s for Ollama

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Rate limiting: ensure minimum time between requests
                await EnforceRateLimitAsync(serviceName);
                
                return await operation();
            }
            catch (RateLimitException ex)
            {
                _logger.LogWarning("Rate limited on attempt {Attempt}: {Message}", attempt + 1, ex.Message);
                
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(ex.RetryAfterMs);
                    continue;
                }
                
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = $"Rate limited after {maxRetries} attempts. Please try again later."
                };
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                _logger.LogWarning("HTTP 429 on attempt {Attempt}", attempt + 1);
                
                if (attempt < maxRetries - 1)
                {
                    var delay = baseDelay * (int)Math.Pow(2, attempt + 1); // 3s, 6s, 12s for Anthropic
                    await Task.Delay(delay);
                    continue;
                }
                
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = $"Rate limited after {maxRetries} attempts: {ex.Message}"
                };
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning("Timeout on attempt {Attempt}", attempt + 1);
                
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(2000);
                    continue;
                }
                
                return new LlmQueryResponse
                {
                    SqlQuery = "",
                    Explanation = "Request timed out"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on attempt {Attempt}", attempt + 1);
                
                if (attempt >= maxRetries - 1)
                {
                    return new LlmQueryResponse
                    {
                        SqlQuery = "",
                        Explanation = $"Failed after {maxRetries} attempts: {ex.Message}"
                    };
                }
                
                await Task.Delay(1000);
            }
        }

        return new LlmQueryResponse
        {
            SqlQuery = "",
            Explanation = "Failed after all retry attempts"
        };
    }

    private async Task EnforceRateLimitAsync(string serviceName)
    {
        // Minimum intervals: Anthropic = 1.2s, Ollama = 0ms (local)
        var minInterval = serviceName.ToLower() == "anthropic" ? 1200 : 0;
        
        if (minInterval == 0) return;

        int delayRequired;
        lock (_lock)
        {
            if (_lastRequestTimes.TryGetValue(serviceName, out var lastRequest))
            {
                var timeSinceLastRequest = (DateTime.UtcNow - lastRequest).TotalMilliseconds;
                if (timeSinceLastRequest < minInterval)
                {
                    delayRequired = (int)(minInterval - timeSinceLastRequest);
                }
                else
                {
                    delayRequired = 0;
                }
            }
            else
            {
                delayRequired = 0;
            }
            
            _lastRequestTimes[serviceName] = DateTime.UtcNow;
        }

        if (delayRequired > 0)
        {
            await Task.Delay(delayRequired);
        }
    }
}