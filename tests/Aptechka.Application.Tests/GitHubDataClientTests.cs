using System.Net;
using System.Text;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.GitHub;

namespace Aptechka.Application.Tests;

public sealed class GitHubDataClientTests
{
    private const string Token = "secret-token-xyz";

    [Fact]
    public async Task CommitSnapshotAsync_ConvertsRefConflictToHeadChangedException()
    {
        using var http = CreateClient(HttpStatusCode.UnprocessableEntity, """{"message":"Update is not a fast forward"}""");
        var client = new GitHubDataClient(http);

        var exception = await Assert.ThrowsAsync<GitHubHeadChangedException>(() =>
            client.CommitSnapshotAsync(
                new SyncTarget("owner", "repo", "sync-branch"),
                Token,
                new GitHubRemoteSnapshot("parent-sha", "tree-sha", DataSnapshot.Empty),
                Snapshot(),
                "test-device"));

        Assert.DoesNotContain(Token, exception.Message);
        Assert.DoesNotContain("manifest-body", exception.Message);
        Assert.DoesNotContain("parent-sha", exception.Message);
    }

    [Fact]
    public async Task CommitSnapshotAsync_DoesNotTreatUnauthorizedAsHeadRace()
    {
        using var http = CreateClient(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        var client = new GitHubDataClient(http);

        var exception = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.CommitSnapshotAsync(
                new SyncTarget("owner", "repo", "sync-branch"),
                Token,
                new GitHubRemoteSnapshot("parent-sha", "tree-sha", DataSnapshot.Empty),
                Snapshot(),
                "test-device"));

        Assert.Equal(401, exception.StatusCode);
        Assert.DoesNotContain(Token, exception.Message);
        Assert.DoesNotContain("manifest-body", exception.Message);
    }

    private static HttpClient CreateClient(HttpStatusCode refStatus, string refBody) =>
        new(new ScriptedHandler(refStatus, refBody))
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

    private static DataSnapshot Snapshot() => new(
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = Encoding.UTF8.GetBytes("manifest-body"),
        });

    private sealed class ScriptedHandler(HttpStatusCode refStatus, string refBody) : HttpMessageHandler
    {
        private int objectSerial;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/git/refs/", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(refStatus, refBody));
            }

            objectSerial++;
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, $$"""{"sha":"object-{{objectSerial}}"}"""));
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }
}
