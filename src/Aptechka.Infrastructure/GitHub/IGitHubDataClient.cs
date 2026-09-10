using Aptechka.Application.Sync;

namespace Aptechka.Infrastructure.GitHub;

public interface IGitHubDataClient
{
    Task<GitHubRemoteSnapshot?> GetSnapshotAsync(
        SyncTarget target,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task InitializeRepositoryAsync(
        SyncTarget target,
        string accessToken,
        byte[] manifestContent,
        string deviceName,
        CancellationToken cancellationToken = default);

    Task<string> CommitSnapshotAsync(
        SyncTarget target,
        string accessToken,
        GitHubRemoteSnapshot remote,
        DataSnapshot local,
        string deviceName,
        CancellationToken cancellationToken = default);
}
