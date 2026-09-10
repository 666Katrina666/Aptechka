using System.Text.RegularExpressions;

namespace Aptechka.Application.Sync;

public sealed partial record SyncTarget(string Owner, string Repository, string Branch)
{
    public void EnsureValid()
    {
        if (!GitHubNamePattern().IsMatch(Owner))
        {
            throw new ArgumentException("Некорректный владелец GitHub-репозитория.", nameof(Owner));
        }

        if (!GitHubNamePattern().IsMatch(Repository))
        {
            throw new ArgumentException("Некорректное имя GitHub-репозитория.", nameof(Repository));
        }

        if (!BranchPattern().IsMatch(Branch))
        {
            throw new ArgumentException("Некорректное имя GitHub-ветки.", nameof(Branch));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex GitHubNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$")]
    private static partial Regex BranchPattern();
}
