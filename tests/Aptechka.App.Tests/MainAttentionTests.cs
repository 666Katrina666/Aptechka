using Aptechka.App.Services;
using Aptechka.App.ViewModels;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.Tests;

public sealed class MainAttentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 13);
    private const string Alpha = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Beta = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string Gamma = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string Delta = "01ARZ3NDEKTSV4RRFFQ69G5FAY";
    private const string PackageA = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string PackageB = "01ARZ3NDEKTSV4RRFFQ69G5FB1";

    [Fact]
    public async Task Refresh_ShowsAllReasonsInFixedOrder()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true, form: "таблетки", strength: "200 мг");
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-2));
        context.AddPackage(PackageB, Alpha, StockState.Low, Today.AddDays(5));

        await context.ViewModel.RefreshItemsAsync();

        var row = Assert.Single(context.ViewModel.AttentionPreview);
        Assert.Equal("Ибупрофен", row.Name);
        Assert.Equal("таблетки, 200 мг", row.Details);
        Assert.Equal(
            [
                "Есть просроченная упаковка",
                "Срок в ближайшие 7 дней · до 18.09.2026",
                "Запас скоро закончится",
            ],
            row.Reasons);
        Assert.Equal("Требует внимания · 1", context.ViewModel.AttentionTitle);
        Assert.False(context.ViewModel.HasMoreAttention);
    }

    [Fact]
    public async Task Refresh_UsesPluralExpiredAndKeepInStockMissing()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-1));
        context.AddPackage(PackageB, Alpha, StockState.Available, Today.AddDays(-8));

        await context.ViewModel.RefreshItemsAsync();

        Assert.Equal(
            ["Есть просроченные упаковки: 2", "Обязательный запас закончился"],
            Assert.Single(context.ViewModel.AttentionPreview).Reasons);
    }

    [Fact]
    public async Task Refresh_LimitsPreviewAndShowsMoreCount()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Аспирин", keepInStock: true);
        context.AddItem(Beta, "Бинт", keepInStock: true, InventoryItemCategory.MedicalSupply);
        context.AddItem(Gamma, "Вата", keepInStock: true, InventoryItemCategory.MedicalSupply);
        context.AddItem(Delta, "Йод", keepInStock: true);

        await context.ViewModel.RefreshItemsAsync();

        Assert.Equal(4, context.ViewModel.AttentionCount);
        Assert.True(context.ViewModel.HasAttention);
        Assert.Equal("Требует внимания · 4", context.ViewModel.AttentionTitle);
        Assert.Equal(["Аспирин", "Бинт", "Вата"], context.ViewModel.AttentionPreview.Select(row => row.Name));
        Assert.True(context.ViewModel.HasMoreAttention);
        Assert.Equal("Ещё 1 в каталоге", context.ViewModel.AttentionMoreText);
    }

    [Fact]
    public async Task Refresh_HidesAttentionWhenNothingNeedsIt()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Бинт", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(120));

        await context.ViewModel.RefreshItemsAsync();

        Assert.False(context.ViewModel.HasAttention);
        Assert.Equal(0, context.ViewModel.AttentionCount);
        Assert.Empty(context.ViewModel.AttentionPreview);
        Assert.False(context.ViewModel.HasMoreAttention);
        Assert.Single(context.ViewModel.Items);
    }

    [Fact]
    public async Task Search_FiltersCatalogButKeepsAttention()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddItem(Beta, "Бинт", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddPackage(PackageA, Beta, StockState.Available, Today.AddDays(120));
        await context.ViewModel.RefreshItemsAsync();

        context.ViewModel.SearchQuery = "бинт";
        await context.ViewModel.RefreshItemsAsync();

        Assert.Equal("Бинт", Assert.Single(context.ViewModel.Items).Name);
        Assert.True(context.ViewModel.HasAttention);
        Assert.Equal("Ибупрофен", Assert.Single(context.ViewModel.AttentionPreview).Name);
        Assert.Equal("Требует внимания · 1", context.ViewModel.AttentionTitle);
    }

    [Fact]
    public async Task StaleRefresh_DoesNotOverwriteFreshResult()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.Store.FirstRead = new TaskCompletionSource();
        var stale = context.ViewModel.RefreshItemsAsync();
        await context.Store.EnteredFirstRead.Task;

        context.AddItem(Beta, "Нурофен", keepInStock: true);
        await context.ViewModel.RefreshItemsAsync();
        Assert.Equal(["Ибупрофен", "Нурофен"], context.ViewModel.AttentionPreview.Select(row => row.Name));
        Assert.Equal(2, context.ViewModel.AttentionCount);

        context.Store.FirstRead.SetResult();
        await stale;
        Assert.Equal(["Ибупрофен", "Нурофен"], context.ViewModel.AttentionPreview.Select(row => row.Name));
        Assert.Equal(2, context.ViewModel.AttentionCount);
    }

    private static TestContext CreateContext()
    {
        var store = new MemoryStore();
        var viewModel = new MainViewModel(
            new InventoryOverviewService(store, store, new StubClock()),
            new UnusedSync(),
            new UnusedInspector(),
            new NullTokenStore(),
            new AutoSyncScheduler(),
            new AppSyncLifetime(),
            "owner",
            "repo",
            "main");
        return new TestContext(viewModel, store);
    }

    private sealed record TestContext(MainViewModel ViewModel, MemoryStore Store)
    {
        public void AddItem(
            string id,
            string name,
            bool keepInStock,
            InventoryItemCategory category = InventoryItemCategory.Medicine,
            string? form = null,
            string? strength = null) =>
            Store.Items.Add(InventoryItem.Create(
                id,
                Now,
                name,
                [],
                category,
                [],
                form ?? (category == InventoryItemCategory.Medicine ? "таблетки" : null),
                strength ?? (category == InventoryItemCategory.Medicine ? "200 мг" : null),
                null,
                keepInStock));

        public void AddPackage(
            string id,
            string itemId,
            StockState stockState,
            DateOnly? expiration = null) =>
            Store.Packages.Add(Package.Create(
                id,
                Now,
                itemId,
                null,
                expiration,
                expiration is null ? null : ExpirationPrecision.Day,
                null,
                null,
                stockState,
                null));
    }

    private sealed class MemoryStore : IInventoryRepository, IPackageRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];
        public TaskCompletionSource? FirstRead { get; set; }
        public TaskCompletionSource EnteredFirstRead { get; } = new();
        private int firstReadStarted;

        public async Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default)
        {
            if (FirstRead is not { } gate || Interlocked.Exchange(ref firstReadStarted, 1) != 0)
            {
                return Items.ToArray();
            }

            var snapshot = Items.ToArray();
            EnteredFirstRead.SetResult();
            await gate.Task;
            return snapshot;
        }

        public Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>(Packages.ToArray());

        public Task SavePackageAsync(Package package, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public DateOnly Today { get; set; } = MainAttentionTests.Today;
    }

    private sealed class NullTokenStore : ISecureTokenStore
    {
        public Task<bool> HasTokenAsync() => Task.FromResult(false);

        public Task<string?> GetTokenAsync() => Task.FromResult<string?>(null);

        public Task SaveTokenAsync(string token) => Task.CompletedTask;
    }

    private sealed class UnusedSync : ISyncService
    {
        public Task<SyncResult> SyncAsync(
            SyncTarget target,
            string accessToken,
            string deviceName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncResult> ResolveConflictsAsync(
            SyncTarget target,
            string accessToken,
            string deviceName,
            IReadOnlyList<SyncConflictResolution> resolutions,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedInspector : ISyncStateInspector
    {
        public Task<SyncInspection> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncInspection(SyncInspectionCondition.NoSuccessfulSync, null, null));
    }
}
