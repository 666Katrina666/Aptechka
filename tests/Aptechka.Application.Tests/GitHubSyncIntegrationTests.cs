using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class GitHubSyncIntegrationTests
{
    private const string Owner = "666Katrina666";
    private const string Repository = "aptechka-data";

    [Fact]
    public void MutatingTarget_RejectsMainAndMasterBeforeAnyGitHubCall()
    {
        Assert.Throws<InvalidOperationException>(() => EnsureMutableBranch("main"));
        Assert.Throws<InvalidOperationException>(() => EnsureMutableBranch("master"));
        Assert.Throws<InvalidOperationException>(() => EnsureMutableBranch("MAIN"));
        EnsureMutableBranch("aptechka-integration/0123456789abcdef0123456789abcdef");
        new SyncTarget(Owner, Repository, "aptechka-integration/0123456789abcdef0123456789abcdef")
            .EnsureValid();
    }

    [GitHubIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task InitializeOrPull_LeavesValidLocalManifest()
    {
        await using var session = await IsolatedBranch.CreateAsync();
        await using var context = CreateContext();

        var result = await session.SyncAsync(context, "integration-pull");
        var snapshot = await context.Repository.ReadAsync();

        Assert.Contains(result.Outcome, new[]
        {
            SyncOutcome.Initialized,
            SyncOutcome.Pulled,
            SyncOutcome.UpToDate,
        });
        Assert.True(snapshot.Files.ContainsKey("aptechka.json"));
    }

    [GitHubIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task RoundTrip_PushesThenPullsOneItem()
    {
        await using var session = await IsolatedBranch.CreateAsync();
        await using var first = CreateContext();
        await session.SyncAsync(first, "integration-first");
        var created = await first.InventoryService.CreateAsync(new InventoryItemDraft(
            $"P1 integration check {Guid.NewGuid():N}",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            "Temporary branch data",
            false));

        var pushed = await session.SyncAsync(first, "integration-first");
        Assert.Equal(SyncOutcome.Pushed, pushed.Outcome);

        await using var second = CreateContext();
        var pulled = await session.SyncAsync(second, "integration-second");
        var item = await second.InventoryService.GetItemAsync(created.Id);

        Assert.Equal(SyncOutcome.Pulled, pulled.Outcome);
        Assert.NotNull(item);
        Assert.Null(item.DeletedAt);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Name, item.Name);
    }

    [GitHubIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task RoundTrip_PushesThenPullsOnePackage()
    {
        await using var session = await IsolatedBranch.CreateAsync();
        await using var first = CreateContext();
        await session.SyncAsync(first, "integration-package-first");
        var item = await first.InventoryService.CreateAsync(new InventoryItemDraft(
            $"P3A package item {Guid.NewGuid():N}",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            "Temporary package roundtrip",
            false));
        var created = await first.PackageService.CreateAsync(
            item.Id,
            new PackageDraft(
                "blister",
                new DateOnly(2027, 4, 30),
                ExpirationPrecision.Month,
                null,
                null,
                StockState.Available,
                null));

        var pushed = await session.SyncAsync(first, "integration-package-first");
        Assert.Equal(SyncOutcome.Pushed, pushed.Outcome);
        Assert.True((await first.Repository.ReadAsync()).Files.ContainsKey($"packages/{created.Id}.json"));

        await using var second = CreateContext();
        var pulled = await session.SyncAsync(second, "integration-package-second");
        var loadedItem = await second.InventoryService.GetItemAsync(item.Id);
        var loadedPackage = await second.PackageService.GetPackageAsync(created.Id);
        var snapshot = await second.Repository.ReadAsync();

        Assert.Equal(SyncOutcome.Pulled, pulled.Outcome);
        Assert.NotNull(loadedItem);
        Assert.Null(loadedItem.DeletedAt);
        Assert.Equal(item.Id, loadedItem.Id);
        Assert.NotNull(loadedPackage);
        Assert.Equal(created.Id, loadedPackage.Id);
        Assert.Equal(item.Id, loadedPackage.ItemId);
        Assert.Equal("blister", loadedPackage.Label);
        Assert.Equal(new DateOnly(2027, 4, 30), loadedPackage.ExpirationDate);
        Assert.Equal(ExpirationPrecision.Month, loadedPackage.ExpirationPrecision);
        Assert.Equal(StockState.Available, loadedPackage.StockState);
        Assert.True(snapshot.Files.ContainsKey($"packages/{created.Id}.json"));
    }

    private static IntegrationContext CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "aptechka-integration", Guid.NewGuid().ToString("N"));
        var clock = new SystemClock();
        var idGenerator = new UlidIdGenerator();
        var repository = new FileInventoryRepository(
            Path.Combine(root, "data"),
            clock,
            idGenerator);
        var syncService = new GitHubSyncService(
            repository,
            new SyncStateStore(Path.Combine(root, "sync", "state.json")),
            new GitHubDataClient(new HttpClient
            {
                BaseAddress = new Uri("https://api.github.com/"),
                Timeout = TimeSpan.FromSeconds(30),
            }));

        return new IntegrationContext(
            root,
            repository,
            new InventoryService(repository, repository, clock, idGenerator),
            new PackageService(repository, repository, clock, idGenerator),
            syncService);
    }

    public sealed class GitHubIntegrationFactAttribute : FactAttribute
    {
        public GitHubIntegrationFactAttribute()
        {
            if (!IsConfigured())
            {
                Skip = "GitHub integration tests are not configured.";
            }
        }
    }

    private static bool IsConfigured() =>
        string.Equals(
            Environment.GetEnvironmentVariable("APTECHKA_RUN_GITHUB_TESTS"),
            "1",
            StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APTECHKA_GITHUB_TOKEN"));

    private static void SkipIfNotConfigured()
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("$XunitDynamicSkip$GitHub integration tests are not configured.");
        }
    }

    private static void EnsureMutableBranch(string branch)
    {
        if (string.Equals(branch, "main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(branch, "master", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "GitHub integration tests must not modify main or master.");
        }
    }

    private sealed class IsolatedBranch : IAsyncDisposable
    {
        private readonly HttpClient http;
        private readonly string token;
        private readonly string branch;

        private IsolatedBranch(HttpClient http, string token, SyncTarget target)
        {
            this.http = http;
            this.token = token;
            branch = target.Branch;
            Target = target;
        }

        public SyncTarget Target { get; }

        public static async Task<IsolatedBranch> CreateAsync()
        {
            SkipIfNotConfigured();
            var token = Environment.GetEnvironmentVariable("APTECHKA_GITHUB_TOKEN")!;
            var http = CreateHttp();
            var branch = $"aptechka-integration/{Guid.NewGuid():N}";
            EnsureMutableBranch(branch);
            var target = new SyncTarget(Owner, Repository, branch);
            target.EnsureValid();

            try
            {
                var mainSha = await GetMainShaAsync(http, token);
                await CreateBranchAsync(http, token, branch, mainSha);
            }
            catch
            {
                http.Dispose();
                throw;
            }

            return new IsolatedBranch(http, token, target);
        }

        public Task<SyncResult> SyncAsync(IntegrationContext context, string deviceName)
        {
            EnsureMutableBranch(Target.Branch);
            return context.SyncService.SyncAsync(Target, token, deviceName);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DeleteBranchAsync(http, token, branch);
            }
            finally
            {
                http.Dispose();
            }
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri("https://api.github.com/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Aptechka/0.1");
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return http;
        }

        private static async Task<string> GetMainShaAsync(HttpClient http, string token)
        {
            using var response = await SendAsync(
                http,
                HttpMethod.Get,
                $"repos/{Owner}/{Repository}/git/ref/heads/main",
                token,
                null);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать SHA ветки main для integration-теста.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var reference = await JsonSerializer.DeserializeAsync<GitRefResponse>(
                stream,
                JsonOptions());
            var sha = reference?.Object.Sha;
            if (string.IsNullOrWhiteSpace(sha))
            {
                throw new InvalidOperationException("GitHub не вернул SHA ветки main.");
            }

            return sha;
        }

        private static async Task CreateBranchAsync(
            HttpClient http,
            string token,
            string branch,
            string sha)
        {
            using var content = new StringContent(
                $$"""{"ref":"refs/heads/{{branch}}","sha":"{{sha}}"}""",
                Encoding.UTF8,
                "application/json");
            using var response = await SendAsync(
                http,
                HttpMethod.Post,
                $"repos/{Owner}/{Repository}/git/refs",
                token,
                content);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Не удалось создать временную integration-ветку.");
            }
        }

        private static async Task DeleteBranchAsync(HttpClient http, string token, string branch)
        {
            using var response = await SendAsync(
                http,
                HttpMethod.Delete,
                $"repos/{Owner}/{Repository}/git/refs/heads/{Uri.EscapeDataString(branch)}",
                token,
                null);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return;
            }

            throw new InvalidOperationException("Не удалось удалить временную integration-ветку.");
        }

        private static async Task<HttpResponseMessage> SendAsync(
            HttpClient http,
            HttpMethod method,
            string path,
            string token,
            HttpContent? content)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;
            return await http.SendAsync(request);
        }

        private static JsonSerializerOptions JsonOptions() => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private sealed record GitRefResponse(GitRefObject Object);

        private sealed record GitRefObject(string Sha);
    }

    private sealed class IntegrationContext(
        string rootPath,
        FileInventoryRepository repository,
        InventoryService inventoryService,
        PackageService packageService,
        GitHubSyncService syncService) : IAsyncDisposable
    {
        public FileInventoryRepository Repository { get; } = repository;
        public InventoryService InventoryService { get; } = inventoryService;
        public PackageService PackageService { get; } = packageService;
        public GitHubSyncService SyncService { get; } = syncService;

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
