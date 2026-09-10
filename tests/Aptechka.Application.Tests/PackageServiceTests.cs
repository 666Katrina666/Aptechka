using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class PackageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 10);
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string OtherItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string FirstPackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string SecondPackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAY";

    [Fact]
    public async Task CreateAsync_CreatesPackageForExistingItem()
    {
        var context = await CreateContextWithItem();

        var created = await context.Packages.CreateAsync(ItemId, Draft(label: "блистер"));

        Assert.Equal(FirstPackageId, created.Id);
        Assert.Equal(ItemId, created.ItemId);
        Assert.Equal("блистер", created.Label);
        Assert.Equal(StockState.Available, created.StockState);
        Assert.Single(context.Store.Packages);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownAndArchivedItems()
    {
        var context = await CreateContextWithItem();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.CreateAsync(OtherItemId, Draft()));

        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Items.ArchiveAsync(ItemId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.CreateAsync(ItemId, Draft()));
        Assert.Empty(context.Store.Packages);
    }

    [Fact]
    public async Task CreateAsync_AllowsSeveralPackagesForOneItem()
    {
        var context = await CreateContextWithItem();

        var first = await context.Packages.CreateAsync(ItemId, Draft(label: "первая"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var second = await context.Packages.CreateAsync(ItemId, Draft(label: "вторая"));

        var loaded = await context.Packages.GetPackagesForItemAsync(ItemId);
        Assert.Equal(FirstPackageId, first.Id);
        Assert.Equal(SecondPackageId, second.Id);
        Assert.Equal(["первая", "вторая"], loaded.Select(package => package.Label));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesOnlyTheRequestedPackage()
    {
        var context = await CreateContextWithItem();
        var first = await context.Packages.CreateAsync(ItemId, Draft(label: "первая"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var second = await context.Packages.CreateAsync(ItemId, Draft(label: "вторая"));

        context.Clock.UtcNow = Now.AddMinutes(2);
        var updated = await context.Packages.UpdateAsync(
            second.Id,
            Draft(label: "коробка", stockState: StockState.Low));

        Assert.Equal(second.Id, updated.Id);
        Assert.Equal(ItemId, updated.ItemId);
        Assert.Equal("коробка", updated.Label);
        Assert.Equal(StockState.Low, updated.StockState);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("первая", (await context.Packages.GetPackageAsync(first.Id))!.Label);
        Assert.Equal(1, (await context.Packages.GetPackageAsync(first.Id))!.Revision);
    }

    [Fact]
    public async Task MarkLowAndMarkDepleted_UpdateStockState()
    {
        var context = await CreateContextWithItem();
        var created = await context.Packages.CreateAsync(ItemId, Draft());

        context.Clock.UtcNow = Now.AddMinutes(1);
        var low = await context.Packages.MarkLowAsync(created.Id);
        context.Clock.UtcNow = Now.AddMinutes(2);
        var depleted = await context.Packages.MarkDepletedAsync(created.Id);

        Assert.Equal(StockState.Low, low.StockState);
        Assert.Equal(StockState.Depleted, depleted.StockState);
        Assert.Equal(3, depleted.Revision);
        Assert.Equal(ItemId, depleted.ItemId);
    }

    [Fact]
    public async Task UnknownId_DoesNotCreateAPackage()
    {
        var context = await CreateContextWithItem();
        await context.Packages.CreateAsync(ItemId, Draft());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.UpdateAsync(SecondPackageId, Draft(label: "новая")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.MarkLowAsync(SecondPackageId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.MarkDepletedAsync(SecondPackageId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.ArchiveAsync(SecondPackageId));

        Assert.Single(context.Store.Packages);
        Assert.Equal(FirstPackageId, context.Store.Packages[0].Id);
    }

    [Fact]
    public async Task ArchivedPackages_AreHiddenFromUiQueries()
    {
        var context = await CreateContextWithItem();
        var created = await context.Packages.CreateAsync(ItemId, Draft());

        context.Clock.UtcNow = Now.AddMinutes(1);
        var archived = await context.Packages.ArchiveAsync(created.Id);
        var repeated = await context.Packages.ArchiveAsync(created.Id);

        Assert.Equal(archived.DeletedAt, repeated.DeletedAt);
        Assert.Null(await context.Packages.GetPackageAsync(created.Id));
        Assert.Empty(await context.Packages.GetPackagesForItemAsync(ItemId));
        Assert.NotNull(context.Store.Packages.Single().DeletedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.UpdateAsync(created.Id, Draft()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Packages.MarkLowAsync(created.Id));
    }

    [Fact]
    public async Task GetItemStockSummaryAsync_UsesUsablePackages()
    {
        var context = await CreateContextWithItem();
        await context.Packages.CreateAsync(
            ItemId,
            Draft(
                expirationDate: new DateOnly(2026, 10, 1),
                expirationPrecision: ExpirationPrecision.Day));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var expired = await context.Packages.CreateAsync(
            ItemId,
            Draft(
                expirationDate: new DateOnly(2026, 9, 1),
                expirationPrecision: ExpirationPrecision.Day));

        var summary = await context.Packages.GetItemStockSummaryAsync(ItemId);

        Assert.Equal(ItemAvailability.Available, summary.Availability);
        Assert.Equal(1, summary.UsablePackageCount);
        Assert.Equal(new DateOnly(2026, 10, 1), summary.NearestExpirationDate);
        Assert.Equal(1, summary.ExpiredPackageCount);

        context.Clock.UtcNow = Now.AddMinutes(2);
        await context.Packages.MarkDepletedAsync(expired.Id);
        summary = await context.Packages.GetItemStockSummaryAsync(ItemId);
        Assert.Equal(0, summary.ExpiredPackageCount);
    }

    private static async Task<TestContext> CreateContextWithItem()
    {
        var store = new InMemoryStore();
        var clock = new StubClock(Now, Today);
        var ids = new StubIdGenerator();
        var items = new InventoryService(store, store, clock, ids);
        var packages = new PackageService(store, store, clock, ids);
        await items.CreateAsync(new InventoryItemDraft(
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            false));
        return new TestContext(items, packages, store, clock);
    }

    private static PackageDraft Draft(
        string? label = null,
        DateOnly? expirationDate = null,
        ExpirationPrecision? expirationPrecision = null,
        StockState stockState = StockState.Available) =>
        new(label, expirationDate, expirationPrecision, null, null, stockState, null);

    private sealed record TestContext(
        InventoryService Items,
        PackageService Packages,
        InMemoryStore Store,
        StubClock Clock);

    private sealed class InMemoryStore : IInventoryRepository, IPackageRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];

        public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>(Items.ToArray());

        public Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(existing => existing.Id == item.Id);
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>(Packages.ToArray());

        public Task SavePackageAsync(Package package, CancellationToken cancellationToken = default)
        {
            Packages.RemoveAll(existing => existing.Id == package.Id);
            Packages.Add(package);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow, DateOnly today) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateOnly Today { get; } = today;
    }

    private sealed class StubIdGenerator : IIdGenerator
    {
        private readonly Queue<string> ids = new(
            [ItemId, FirstPackageId, SecondPackageId, "01ARZ3NDEKTSV4RRFFQ69G5FAZ"]);

        public string Create(DateTimeOffset timestamp) => ids.Dequeue();
    }
}
