namespace Aptechka.Infrastructure.GitHub;

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(int statusCode, string message, bool isRateLimited = false, TimeSpan? retryAfter = null)
        : base("GitHub API")
    {
        StatusCode = statusCode;
        IsRateLimited = isRateLimited;
        RetryAfter = retryAfter;
    }

    public int StatusCode { get; }

    public bool IsRateLimited { get; }

    public TimeSpan? RetryAfter { get; }
}
