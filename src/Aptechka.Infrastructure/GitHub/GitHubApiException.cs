namespace Aptechka.Infrastructure.GitHub;

public sealed class GitHubApiException(
    int statusCode,
    string message) : Exception($"GitHub API ({statusCode}): {message}")
{
    public int StatusCode { get; } = statusCode;
}
