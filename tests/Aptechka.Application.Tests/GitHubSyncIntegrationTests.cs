using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

[Trait("Category", "Integration")]
public sealed class GitHubSyncIntegrationTests
{
    [Fact]
    public async Task InitializeOrPull_LeavesValidLocalManifest()
    {
        var settings = IntegrationSettings.TryRead();
        if (settings is null)
        {
            return;
        }

        await using var context = CreateContext();
        var result = await context.SyncService.SyncAsync(
            settings.Target,
            settings.Token,
            "integration-main");

        var snapshot = await context.Repository.ReadAsync();
        Assert.Contains(result.Outcome, new[]
        {
            SyncOutcome.Initialized,
            SyncOutcome.Pulled,
            SyncOutcome.UpToDate,
        });
        Assert.True(snapshot.Files.ContainsKey("aptechka.json"));
    }

    [Fact]
    public async Task RoundTrip_PushesThenPullsOneItem()
    {
        var settings = IntegrationSettings.TryRead();
        if (settings is null)
        {
            return;
        }

        await using var first = CreateContext();
        await first.SyncService.SyncAsync(settings.Target, settings.Token, "integration-first");
        await first.InventoryService.CreateAsync(new InventoryItemDraft(
            "P1 integration check",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            "Temporary branch data",
            false));

        var pushed = await first.SyncService.SyncAsync(
            settings.Target,
            settings.Token,
            "integration-first");
        Assert.Equal(SyncOutcome.Pushed, pushed.Outcome);

        await using var second = CreateContext();
        var pulled = await second.SyncService.SyncAsync(
            settings.Target,
            settings.Token,
            "integration-second");
        var item = Assert.Single(await second.InventoryService.GetCatalogAsync());

        Assert.Equal(SyncOutcome.Pulled, pulled.Outcome);
        Assert.Equal("P1 integration check", item?.Name);
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
        var packages = new InMemoryPackageRepository();
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
            new InventoryService(repository, packages, clock, idGenerator),
            syncService);
    }

    private sealed record IntegrationSettings(SyncTarget Target, string Token)
    {
        public static IntegrationSettings? TryRead()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("APTECHKA_RUN_GITHUB_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                return null;
            }

            var token = Environment.GetEnvironmentVariable("APTECHKA_GITHUB_TOKEN");
            var branch = Environment.GetEnvironmentVariable("APTECHKA_GITHUB_BRANCH") ?? "main";
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("APTECHKA_GITHUB_TOKEN is required.");
            }

            return new IntegrationSettings(
                new SyncTarget("666Katrina666", "aptechka-data", branch),
                token);
        }
    }

    private sealed class IntegrationContext(
        string rootPath,
        FileInventoryRepository repository,
        InventoryService inventoryService,
        GitHubSyncService syncService) : IAsyncDisposable
    {
        public FileInventoryRepository Repository { get; } = repository;
        public InventoryService InventoryService { get; } = inventoryService;
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

    private sealed class InMemoryPackageRepository : IPackageRepository
    {
        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>([]);

        public Task SavePackageAsync(
            Package package,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
