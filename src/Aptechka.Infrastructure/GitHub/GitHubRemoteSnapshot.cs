using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.GitHub;

public sealed record GitHubRemoteSnapshot(
    string CommitSha,
    string TreeSha,
    DataSnapshot Data);
