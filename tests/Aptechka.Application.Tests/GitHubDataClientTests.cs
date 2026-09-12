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

    [Fact]
    public async Task GetSnapshotAsync_ReadsShoppingAndIgnoresUnknownPaths()
    {
        const string itemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var shoppingPath = $"shopping/{itemId}.json";
        var shoppingJson = """{"id":"01ARZ3NDEKTSV4RRFFQ69G5FAV","itemId":"01ARZ3NDEKTSV4RRFFQ69G5FAV"}""";
        using var http = new HttpClient(new SnapshotHandler(shoppingPath, shoppingJson))
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

        var snapshot = await new GitHubDataClient(http).GetSnapshotAsync(
            new SyncTarget("owner", "repo", "sync-branch"),
            Token);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Data.Files.ContainsKey(shoppingPath));
        Assert.False(snapshot.Data.Files.ContainsKey("README.md"));
        Assert.Equal(shoppingJson, Encoding.UTF8.GetString(snapshot.Data.Files[shoppingPath]));
    }

    [Fact]
    public async Task CommitSnapshotAsync_WritesShoppingJsonIntoTheTree()
    {
        const string itemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var shoppingPath = $"shopping/{itemId}.json";
        var handler = new CommitHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };
        var local = new DataSnapshot(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = Encoding.UTF8.GetBytes("manifest-body"),
            [shoppingPath] = Encoding.UTF8.GetBytes("""{"id":"01ARZ3NDEKTSV4RRFFQ69G5FAV"}"""),
        });

        await new GitHubDataClient(http).CommitSnapshotAsync(
            new SyncTarget("owner", "repo", "sync-branch"),
            Token,
            new GitHubRemoteSnapshot("parent-sha", "tree-sha", DataSnapshot.Empty),
            local,
            "test-device");

        Assert.Contains(shoppingPath, handler.TreeBody, StringComparison.Ordinal);
        Assert.Contains("aptechka.json", handler.TreeBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, handler.TreeBody, StringComparison.Ordinal);
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

    private sealed class SnapshotHandler(string shoppingPath, string shoppingJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;
            if (path.Contains("/git/ref/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("""{"object":{"sha":"commit-sha"}}"""));
            }

            if (path.Contains("/git/commits/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("""{"sha":"commit-sha","tree":{"sha":"tree-sha"}}"""));
            }

            if (path.Contains("/git/trees/", StringComparison.Ordinal) &&
                query.Contains("recursive=1", StringComparison.Ordinal))
            {
                return Task.FromResult(Json($$"""
                    {
                      "sha": "tree-sha",
                      "truncated": false,
                      "tree": [
                        {"path": "README.md", "type": "blob", "sha": "readme-sha"},
                        {"path": "{{shoppingPath}}", "type": "blob", "sha": "shopping-sha"},
                        {"path": "shopping", "type": "tree", "sha": "dir-sha"}
                      ]
                    }
                    """));
            }

            if (path.Contains("/git/blobs/shopping-sha", StringComparison.Ordinal))
            {
                return Task.FromResult(Json($$"""
                    {
                      "content": "{{Convert.ToBase64String(Encoding.UTF8.GetBytes(shoppingJson))}}",
                      "encoding": "base64"
                    }
                    """));
            }

            return Task.FromResult(Json("""{"message":"unexpected"}""", HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class CommitHandler : HttpMessageHandler
    {
        private int objectSerial;

        public string TreeBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/git/trees", StringComparison.Ordinal))
            {
                TreeBody = request.Content is null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (path.Contains("/git/refs/", StringComparison.Ordinal))
            {
                return Json("""{"object":{"sha":"commit-1"}}""");
            }

            objectSerial++;
            return Json($$"""{"sha":"object-{{objectSerial}}"}""", HttpStatusCode.Created);
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }
}
