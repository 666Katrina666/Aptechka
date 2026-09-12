using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class ShoppingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string MissingItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";

    [Fact]
    public async Task RequestPurchase_CreatesOneRecordWithIdEqualToItemId()
    {
        var context = await CreateContextWithItemAsync();
        var created = await context.Service.RequestPurchaseAsync(ItemId, "  купить упаковку  ");

        Assert.Equal(ItemId, created.Id);
        Assert.Equal(ItemId, created.ItemId);
        Assert.True(created.IsRequested);
        Assert.Equal("купить упаковку", created.Note);
        Assert.Equal(1, created.Revision);
        Assert.Equal(created.Id, Assert.Single(await context.Service.GetActiveAsync()).Id);
        Assert.Equal(created.Id, (await context.Service.GetByItemIdAsync(ItemId))!.Id);
        Assert.Single(context.Repository.ShoppingItems);
    }

    [Fact]
    public async Task RequestAndClear_KeepTheRecordWithIsRequestedFalse()
    {
        var context = await CreateContextWithItemAsync();
        await context.Service.RequestPurchaseAsync(ItemId, "аптека");
        context.Clock.UtcNow = Now.AddMinutes(1);
        var cleared = await context.Service.ClearRequestAsync(ItemId);

        Assert.False(cleared.IsRequested);
        Assert.Equal("аптека", cleared.Note);
        Assert.Equal(2, cleared.Revision);
        Assert.Null(cleared.DeletedAt);
        Assert.Equal(cleared.Id, Assert.Single(await context.Service.GetActiveAsync()).Id);
    }

    [Fact]
    public async Task RepeatedCommands_DoNotBumpRevision()
    {
        var context = await CreateContextWithItemAsync();
        var requested = await context.Service.RequestPurchaseAsync(ItemId, "аптека");
        context.Clock.UtcNow = Now.AddMinutes(1);
        var requestedAgain = await context.Service.RequestPurchaseAsync(ItemId, "  аптека  ");
        context.Clock.UtcNow = Now.AddMinutes(2);
        var cleared = await context.Service.ClearRequestAsync(ItemId);
        context.Clock.UtcNow = Now.AddMinutes(3);
        var clearedAgain = await context.Service.ClearRequestAsync(ItemId);
        context.Clock.UtcNow = Now.AddMinutes(4);
        var sameNote = await context.Service.UpdateNoteAsync(ItemId, "аптека");

        Assert.Same(requested, requestedAgain);
        Assert.Equal(1, requestedAgain.Revision);
        Assert.Equal(2, cleared.Revision);
        Assert.Same(cleared, clearedAgain);
        Assert.Same(cleared, sameNote);
        Assert.Equal(Now, requestedAgain.UpdatedAt);
        Assert.Equal(Now.AddMinutes(2), clearedAgain.UpdatedAt);
    }

    [Fact]
    public async Task UpdateNote_ChangesOnlyTheNote()
    {
        var context = await CreateContextWithItemAsync();
        await context.Service.RequestPurchaseAsync(ItemId, null);
        context.Clock.UtcNow = Now.AddMinutes(1);
        var updated = await context.Service.UpdateNoteAsync(ItemId, "  вечером  ");

        Assert.True(updated.IsRequested);
        Assert.Equal("вечером", updated.Note);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(Now.AddMinutes(1), updated.UpdatedAt);
    }

    [Fact]
    public async Task UnknownAndArchivedItems_AreNotCreatedOrChanged()
    {
        var context = await CreateContextWithItemAsync();
        await context.Service.RequestPurchaseAsync(ItemId, "аптека");
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Inventory.ArchiveAsync(ItemId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.RequestPurchaseAsync(MissingItemId, "секретная заметка"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.ClearRequestAsync(MissingItemId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.UpdateNoteAsync(MissingItemId, "секретная заметка"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.RequestPurchaseAsync(ItemId, "новая заметка"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.ClearRequestAsync(ItemId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.UpdateNoteAsync(ItemId, "новая заметка"));

        var loaded = Assert.Single(context.Repository.ShoppingItems);
        Assert.Equal(ItemId, loaded.ItemId);
        Assert.True(loaded.IsRequested);
        Assert.Equal("аптека", loaded.Note);
        Assert.Equal(1, loaded.Revision);
        Assert.Null(loaded.DeletedAt);
    }

    [Fact]
    public async Task ArchiveItem_DoesNotCascadeToShoppingItem()
    {
        var context = await CreateContextWithItemAsync();
        var requested = await context.Service.RequestPurchaseAsync(ItemId, "аптека");
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Inventory.ArchiveAsync(ItemId);

        var loaded = await context.Service.GetByItemIdAsync(ItemId);
        Assert.Equal(requested.Revision, loaded!.Revision);
        Assert.True(loaded.IsRequested);
        Assert.Null(loaded.DeletedAt);
        Assert.Equal("аптека", loaded.Note);
        Assert.NotNull((await context.Inventory.GetItemAsync(ItemId))!.DeletedAt);
    }

    [Fact]
    public async Task KeepInStockMissing_IsNotMaterializedAsShoppingItem()
    {
        var context = await CreateContextWithItemAsync(keepInStock: true);

        Assert.Empty(await context.Service.GetActiveAsync());
        Assert.Null(await context.Service.GetByItemIdAsync(ItemId));
        Assert.Empty(context.Repository.ShoppingItems);
    }

    [Fact]
    public async Task GetActive_ExcludesTombstonesAndKeepsClosedRequests()
    {
        var context = await CreateContextWithItemAsync();
        var requested = await context.Service.RequestPurchaseAsync(ItemId, null);
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.ClearRequestAsync(ItemId);
        context.Repository.ShoppingItems[0] = requested.Delete(Now.AddMinutes(2));

        Assert.Empty(await context.Service.GetActiveAsync());
        Assert.NotNull((await context.Service.GetByItemIdAsync(ItemId))!.DeletedAt);
    }

    private static async Task<TestContext> CreateContextWithItemAsync(bool keepInStock = false)
    {
        var repository = new InMemoryStore();
        var clock = new StubClock(Now);
        var ids = new StubIdGenerator(ItemId);
        var inventory = new InventoryService(repository, repository, clock, ids);
        await inventory.CreateAsync(new InventoryItemDraft(
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            keepInStock));
        return new TestContext(
            new ShoppingService(repository, repository, clock),
            inventory,
            repository,
            clock);
    }

    private sealed record TestContext(
        ShoppingService Service,
        InventoryService Inventory,
        InMemoryStore Repository,
        StubClock Clock);

    private sealed class InMemoryStore : IInventoryRepository, IPackageRepository, IShoppingItemRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];
        public List<ShoppingItem> ShoppingItems { get; } = [];

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

        public Task<IReadOnlyList<ShoppingItem>> GetShoppingItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ShoppingItem>>(ShoppingItems.ToArray());

        public Task SaveShoppingItemAsync(ShoppingItem shoppingItem, CancellationToken cancellationToken = default)
        {
            ShoppingItems.RemoveAll(existing => existing.ItemId == shoppingItem.ItemId);
            ShoppingItems.Add(shoppingItem);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateOnly Today { get; set; } = DateOnly.FromDateTime(utcNow.Date);
    }

    private sealed class StubIdGenerator(params string[] values) : IIdGenerator
    {
        private readonly Queue<string> ids = new(values);

        public string Create(DateTimeOffset timestamp) => ids.Dequeue();
    }
}
