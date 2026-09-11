using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Infrastructure.GitHub;

public sealed class GitHubDataClient(HttpClient httpClient) : IGitHubDataClient
{
    private const string ApiVersion = "2026-03-10";

    public async Task<GitHubRemoteSnapshot?> GetSnapshotAsync(
        SyncTarget target,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var prefix = GetRepositoryPrefix(target);
        var referenceResponse = await SendAsync(
            HttpMethod.Get,
            $"{prefix}/git/ref/heads/{Escape(target.Branch)}",
            accessToken,
            null,
            allowMissingRepository: true,
            cancellationToken);

        if (referenceResponse is null)
        {
            return null;
        }

        var reference = await DeserializeAsync<GitReferenceResponse>(
            referenceResponse,
            cancellationToken);
        var commitResponse = await SendRequiredAsync(
            HttpMethod.Get,
            $"{prefix}/git/commits/{Escape(reference.Object.Sha)}",
            accessToken,
            null,
            cancellationToken);
        var commit = await DeserializeAsync<GitCommitResponse>(commitResponse, cancellationToken);

        var treeResponse = await SendRequiredAsync(
            HttpMethod.Get,
            $"{prefix}/git/trees/{Escape(commit.Tree.Sha)}?recursive=1",
            accessToken,
            null,
            cancellationToken);
        var tree = await DeserializeAsync<GitTreeResponse>(treeResponse, cancellationToken);
        if (tree.Truncated)
        {
            throw new GitHubApiException(409, "Снимок репозитория слишком велик для P1.");
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in tree.Tree.Where(static entry =>
                     entry.Type == "blob" && IsDataPath(entry.Path)))
        {
            var blobResponse = await SendRequiredAsync(
                HttpMethod.Get,
                $"{prefix}/git/blobs/{Escape(entry.Sha)}",
                accessToken,
                null,
                cancellationToken);
            var blob = await DeserializeAsync<GitBlobResponse>(blobResponse, cancellationToken);
            if (!string.Equals(blob.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new GitHubApiException(409, $"Неизвестная кодировка файла {entry.Path}.");
            }

            files.Add(entry.Path, Convert.FromBase64String(RemoveWhitespace(blob.Content)));
        }

        return new GitHubRemoteSnapshot(
            reference.Object.Sha,
            commit.Tree.Sha,
            new DataSnapshot(files));
    }

    public async Task InitializeRepositoryAsync(
        SyncTarget target,
        string accessToken,
        byte[] manifestContent,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            message = $"data(sync): initialize from {deviceName}",
            content = Convert.ToBase64String(manifestContent),
            branch = target.Branch,
        };

        await SendRequiredAsync(
            HttpMethod.Put,
            $"{GetRepositoryPrefix(target)}/contents/aptechka.json",
            accessToken,
            body,
            cancellationToken);
    }

    public async Task<string> CommitSnapshotAsync(
        SyncTarget target,
        string accessToken,
        GitHubRemoteSnapshot remote,
        DataSnapshot local,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var prefix = GetRepositoryPrefix(target);
        var treeEntries = new List<object>();

        foreach (var (path, content) in local.Files.OrderBy(static pair => pair.Key))
        {
            var blobResponse = await SendRequiredAsync(
                HttpMethod.Post,
                $"{prefix}/git/blobs",
                accessToken,
                new
                {
                    content = Convert.ToBase64String(content),
                    encoding = "base64",
                },
                cancellationToken);
            var blob = await DeserializeAsync<GitObjectResponse>(blobResponse, cancellationToken);
            treeEntries.Add(new { path, mode = "100644", type = "blob", sha = blob.Sha });
        }

        foreach (var path in remote.Data.Files.Keys.Except(local.Files.Keys, StringComparer.Ordinal))
        {
            treeEntries.Add(new { path, mode = "100644", type = "blob", sha = (string?)null });
        }

        var treeResponse = await SendRequiredAsync(
            HttpMethod.Post,
            $"{prefix}/git/trees",
            accessToken,
            new { base_tree = remote.TreeSha, tree = treeEntries },
            cancellationToken);
        var tree = await DeserializeAsync<GitObjectResponse>(treeResponse, cancellationToken);

        var commitResponse = await SendRequiredAsync(
            HttpMethod.Post,
            $"{prefix}/git/commits",
            accessToken,
            new
            {
                message = $"data(sync): update from {deviceName}",
                tree = tree.Sha,
                parents = new[] { remote.CommitSha },
            },
            cancellationToken);
        var commit = await DeserializeAsync<GitObjectResponse>(commitResponse, cancellationToken);

        using var referenceResponse = await SendRequiredAsync(
            HttpMethod.Patch,
            $"{prefix}/git/refs/heads/{Escape(target.Branch)}",
            accessToken,
            new { sha = commit.Sha, force = false },
            cancellationToken,
            headRaceOnConflict: true);

        return commit.Sha;
    }

    private async Task<HttpResponseMessage> SendRequiredAsync(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        CancellationToken cancellationToken,
        bool headRaceOnConflict = false) =>
        await SendAsync(
            method,
            path,
            accessToken,
            body,
            false,
            cancellationToken,
            headRaceOnConflict)
        ?? throw new GitHubApiException(404, "Репозиторий или ветка не найдены.");

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        bool allowMissingRepository,
        CancellationToken cancellationToken,
        bool headRaceOnConflict = false)
    {
        using var request = new HttpRequestMessage(method, path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("Aptechka/0.1");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: AptechkaJson.Options);
        }

        var response = await httpClient.SendAsync(request, timeout.Token);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (allowMissingRepository && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            response.Dispose();
            return null;
        }

        var statusCode = (int)response.StatusCode;
        if (headRaceOnConflict && statusCode is 409 or 422)
        {
            response.Dispose();
            throw new GitHubHeadChangedException();
        }

        var rateLimited = IsRateLimited(response);
        var retryAfter = ReadRetryAfter(response);
        response.Dispose();
        throw new GitHubApiException(statusCode, "GitHub API", rateLimited, retryAfter);
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(
                stream,
                AptechkaJson.Options,
                cancellationToken)
                ?? throw new GitHubApiException((int)response.StatusCode, "GitHub API");
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.Any(static value => value.Trim() == "0"))
        {
            return true;
        }

        return response.Headers.RetryAfter is not null;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static string GetRepositoryPrefix(SyncTarget target) =>
        $"repos/{Escape(target.Owner)}/{Escape(target.Repository)}";

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static bool IsDataPath(string path) =>
        string.Equals(path, "aptechka.json", StringComparison.Ordinal) ||
        IsEntityJsonPath(path, "items/") ||
        IsEntityJsonPath(path, "packages/");

    private static bool IsEntityJsonPath(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal) &&
        path.EndsWith(".json", StringComparison.Ordinal) &&
        !path[prefix.Length..^".json".Length].Contains('/');

    private static string RemoveWhitespace(string value) =>
        string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));

    private sealed record GitReferenceResponse(GitObjectReference Object);
    private sealed record GitObjectReference(string Sha);
    private sealed record GitCommitResponse(string Sha, GitObjectReference Tree);
    private sealed record GitTreeResponse(string Sha, IReadOnlyList<GitTreeEntry> Tree, bool Truncated);
    private sealed record GitTreeEntry(string Path, string Type, string Sha);
    private sealed record GitBlobResponse(string Content, string Encoding);
    private sealed record GitObjectResponse(string Sha);
}
