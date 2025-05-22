public class RateLimitException : Exception
{
    public int RetryAfterMs { get; }

    public RateLimitException(string message, int retryAfterMs) : base(message)
    {
        RetryAfterMs = retryAfterMs;
    }
}