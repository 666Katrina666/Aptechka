using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class ShoppingListServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 12);
    private const string Alpha = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Beta = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string Gamma = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string PackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAY";

    [Fact]
    public async Task Build_IncludesOnlyManualReason()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: true, "аптека");

        var list = await context.Service.GetListAsync();
        var entry = Assert.Single(list.Active);

        Assert.Empty(list.RecentlyClosed);
        Assert.True(entry.HasManualReason);
        Assert.False(entry.HasKeepInStockMissingReason);
        Assert.True(entry.HasShoppingRecord);
        Assert.Equal("аптека", entry.Note);
        Assert.Equal(ItemAvailability.Missing, entry.Availability);
        Assert.DoesNotContain(list.Addable, candidate => candidate.ItemId == Alpha);
    }

    [Fact]
    public async Task Build_IncludesOnlyKeepInStockMissing()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Вата", keepInStock: true, category: InventoryItemCategory.MedicalSupply);

        var entry = Assert.Single((await context.Service.GetListAsync()).Active);

        Assert.False(entry.HasManualReason);
        Assert.True(entry.HasKeepInStockMissingReason);
        Assert.False(entry.HasShoppingRecord);
        Assert.Null(entry.Note);
        Assert.Equal(ItemAvailability.Missing, entry.Availability);
        Assert.Contains((await context.Service.GetListAsync()).Addable, candidate => candidate.ItemId == Alpha);
    }

    [Fact]
    public async Task Build_CombinesBothReasonsIntoOneRow()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: true, "аптека");

        var list = await context.Service.GetListAsync();
        var entry = Assert.Single(list.Active);

        Assert.Empty(list.RecentlyClosed);
        Assert.True(entry.HasManualReason);
        Assert.True(entry.HasKeepInStockMissingReason);
        Assert.Equal("аптека", entry.Note);
    }

    [Fact]
    public async Task Build_DoesNotCreateAutomaticReasonForAvailableOrLow()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddPackage(Alpha, StockState.Available);
        context.AddItem(Beta, "Нурофен", keepInStock: true);
        context.AddPackage(Beta, StockState.Low, "01ARZ3NDEKTSV4RRFFQ69G5FB0");

        var list = await context.Service.GetListAsync();

        Assert.Empty(list.Active);
        Assert.Equal(ItemAvailability.Available, list.Addable.Single(candidate => candidate.ItemId == Alpha).Availability);
        Assert.Equal(ItemAvailability.Low, list.Addable.Single(candidate => candidate.ItemId == Beta).Availability);
    }

    [Fact]
    public async Task Build_TreatsMissingPackagesAsAutomaticReason()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);

        Assert.True(Assert.Single((await context.Service.GetListAsync()).Active).HasKeepInStockMissingReason);
    }

    [Fact]
    public async Task Build_IgnoresDepletedExpiredAndTombstonePackages()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddPackage(Alpha, StockState.Depleted);
        context.AddItem(Beta, "Нурофен", keepInStock: true);
        context.AddPackage(Beta, StockState.Available, "01ARZ3NDEKTSV4RRFFQ69G5FB0", new DateOnly(2026, 1, 1));
        context.AddItem(Gamma, "Парацетамол", keepInStock: true);
        var tombstone = Package.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FB1",
            Now,
            Gamma,
            null,
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Month,
            null,
            null,
            StockState.Available,
            null);
        context.Store.Packages.Add(tombstone.Delete(Now.AddMinutes(1)));

        var list = await context.Service.GetListAsync();

        Assert.Equal(3, list.Active.Count);
        Assert.All(list.Active, entry =>
        {
            Assert.True(entry.HasKeepInStockMissingReason);
            Assert.Equal(ItemAvailability.Missing, entry.Availability);
        });
    }

    [Fact]
    public async Task Build_RemovesOnlyAutomaticReasonAfterUsablePackageAppears()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: true, "аптека");
        Assert.True((await context.Service.GetListAsync()).Active[0].HasKeepInStockMissingReason);

        context.AddPackage(Alpha, StockState.Available);
        var list = await context.Service.GetListAsync();
        var entry = Assert.Single(list.Active);

        Assert.True(entry.HasManualReason);
        Assert.False(entry.HasKeepInStockMissingReason);
        Assert.Equal(ItemAvailability.Available, entry.Availability);
        Assert.Empty(list.RecentlyClosed);
    }

    [Fact]
    public async Task Build_KeepsClearedManualInRecentlyClosed()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: false, "аптека", Now.AddMinutes(3));

        var list = await context.Service.GetListAsync();

        Assert.Empty(list.Active);
        var closed = Assert.Single(list.RecentlyClosed);
        Assert.False(closed.HasManualReason);
        Assert.False(closed.HasKeepInStockMissingReason);
        Assert.Equal("аптека", closed.Note);
        Assert.True(Assert.Single(list.Addable).IsRecentlyClosed);
    }

    [Fact]
    public async Task Build_DoesNotCloseManualWhileAutomaticReasonRemains()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: false, "аптека");

        var list = await context.Service.GetListAsync();
        var entry = Assert.Single(list.Active);

        Assert.Empty(list.RecentlyClosed);
        Assert.False(entry.HasManualReason);
        Assert.True(entry.HasKeepInStockMissingReason);
        Assert.True(entry.HasShoppingRecord);
    }

    [Fact]
    public async Task Build_IgnoresTombstoneShoppingAndArchivedOrMissingItems()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.Store.ShoppingItems.Add(ShoppingItem.Create(Alpha, Now, true, "аптека").Delete(Now.AddMinutes(1)));
        var archived = InventoryItem.Create(
            Beta,
            Now,
            "Архив",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            true);
        context.Store.Items.Add(archived.Delete(Now.AddMinutes(1)));
        context.AddShopping(Beta, requested: true, "скрыто");
        context.AddShopping(Gamma, requested: true, "нет позиции");

        var list = await context.Service.GetListAsync();
        var entry = Assert.Single(list.Active);

        Assert.Equal(Alpha, entry.ItemId);
        Assert.False(entry.HasManualReason);
        Assert.True(entry.HasKeepInStockMissingReason);
        Assert.False(entry.HasShoppingRecord);
        Assert.Empty(list.RecentlyClosed);
    }

    [Fact]
    public async Task Build_SortsActiveByReasonThenNameThenId()
    {
        var context = CreateContext();
        context.AddItem(Gamma, "Ядран", keepInStock: false);
        context.AddShopping(Gamma, requested: true, null);
        context.AddItem(Beta, "аспирин", keepInStock: true);
        context.AddItem(Alpha, "Аспирин", keepInStock: true);
        context.AddShopping(Alpha, requested: true, null);

        var names = (await context.Service.GetListAsync()).Active.Select(entry => entry.Name).ToArray();

        Assert.Equal(["Аспирин", "аспирин", "Ядран"], names);
        Assert.Equal(Alpha, (await context.Service.GetListAsync()).Active[0].ItemId);
    }

    [Fact]
    public async Task Build_SortsRecentlyClosedByUpdatedAtThenName()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Бетадин", keepInStock: false);
        context.AddItem(Beta, "аспирин", keepInStock: false);
        context.AddShopping(Alpha, requested: false, null, Now.AddMinutes(1));
        context.AddShopping(Beta, requested: false, null, Now.AddMinutes(5));

        var names = (await context.Service.GetListAsync()).RecentlyClosed.Select(entry => entry.Name).ToArray();

        Assert.Equal(["аспирин", "Бетадин"], names);
    }

    [Fact]
    public async Task GetListAsync_ReadsEachRepositoryOnce()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: true, null);

        await context.Service.GetListAsync();

        Assert.Equal(1, context.Store.ItemReads);
        Assert.Equal(1, context.Store.PackageReads);
        Assert.Equal(1, context.Store.ShoppingReads);
    }

    private static TestContext CreateContext()
    {
        var store = new CountingStore();
        var clock = new StubClock();
        return new TestContext(new ShoppingListService(store, store, store, clock), store);
    }

    private sealed record TestContext(ShoppingListService Service, CountingStore Store)
    {
        public void AddItem(
            string id,
            string name,
            bool keepInStock,
            InventoryItemCategory category = InventoryItemCategory.Medicine) =>
            Store.Items.Add(InventoryItem.Create(
                id,
                Now,
                name,
                [],
                category,
                [],
                category == InventoryItemCategory.Medicine ? "таблетки" : null,
                category == InventoryItemCategory.Medicine ? "200 мг" : null,
                null,
                keepInStock));

        public void AddPackage(
            string itemId,
            StockState stockState,
            string? packageId = null,
            DateOnly? expiration = null) =>
            Store.Packages.Add(Package.Create(
                packageId ?? PackageId,
                Now,
                itemId,
                null,
                expiration ?? new DateOnly(2027, 4, 30),
                ExpirationPrecision.Day,
                null,
                null,
                stockState,
                null));

        public void AddShopping(
            string itemId,
            bool requested,
            string? note,
            DateTimeOffset? updatedAt = null)
        {
            var created = ShoppingItem.Create(itemId, Now, requested, note);
            Store.ShoppingItems.Add(updatedAt is { } at && at != Now
                ? created.Update(at, requested, note)
                : created);
        }
    }

    private sealed class CountingStore : IInventoryRepository, IPackageRepository, IShoppingItemRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];
        public List<ShoppingItem> ShoppingItems { get; } = [];
        public int ItemReads { get; private set; }
        public int PackageReads { get; private set; }
        public int ShoppingReads { get; private set; }

        public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default)
        {
            ItemReads++;
            return Task.FromResult<IReadOnlyList<InventoryItem>>(Items.ToArray());
        }

        public Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default)
        {
            PackageReads++;
            return Task.FromResult<IReadOnlyList<Package>>(Packages.ToArray());
        }

        public Task SavePackageAsync(Package package, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ShoppingItem>> GetShoppingItemsAsync(
            CancellationToken cancellationToken = default)
        {
            ShoppingReads++;
            return Task.FromResult<IReadOnlyList<ShoppingItem>>(ShoppingItems.ToArray());
        }

        public Task SaveShoppingItemAsync(ShoppingItem shoppingItem, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public DateOnly Today { get; set; } = ShoppingListServiceTests.Today;
    }
}
