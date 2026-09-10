namespace Aptechka.Infrastructure.GitHub;

public sealed class GitHubHeadChangedException : Exception
{
    public GitHubHeadChangedException()
        : this("Удалённая ветка изменилась во время отправки.")
    {
    }

    public GitHubHeadChangedException(string message)
        : base(message)
    {
    }
}
