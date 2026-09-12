using Aptechka.App.ViewModels;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.Tests;

public sealed class ShoppingViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
    private const string Alpha = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Beta = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string Gamma = "01ARZ3NDEKTSV4RRFFQ69G5FAX";

    [Fact]
    public async Task Load_SplitsActiveAndRecentlyClosed()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: true, "аптека");
        context.AddItem(Beta, "Вата", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddShopping(Beta, requested: false, "закрыто");

        await context.ViewModel.LoadAsync();

        var active = Assert.Single(context.ViewModel.Active);
        Assert.Equal("Ибупрофен", active.Name);
        Assert.True(active.HasManualReason);
        Assert.Equal("аптека", active.Note);
        var recent = Assert.Single(context.ViewModel.RecentlyClosed);
        Assert.Equal("Вата", recent.Name);
        Assert.Equal("закрыто", recent.Note);
    }

    [Fact]
    public async Task Search_FiltersRowsWithoutChangingReasons()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: true, "вечерняя аптека");
        context.AddItem(Beta, "Вата", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddShopping(Beta, requested: false, "склад");
        await context.ViewModel.LoadAsync();

        context.ViewModel.SearchQuery = "АПТЕКА";

        Assert.Equal("Ибупрофен", Assert.Single(context.ViewModel.Active).Name);
        Assert.Empty(context.ViewModel.RecentlyClosed);
        Assert.True(context.ViewModel.Active[0].HasManualReason);
        Assert.True(context.ViewModel.Active[0].HasKeepInStockMissingReason);
        context.ViewModel.SearchQuery = "вата";
        Assert.Empty(context.ViewModel.Active);
        Assert.Equal("Вата", Assert.Single(context.ViewModel.RecentlyClosed).Name);
    }

    [Fact]
    public async Task AddMode_ListsCandidatesAndCancelDoesNotWrite()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: true, null);
        context.AddItem(Beta, "Вата", keepInStock: true, InventoryItemCategory.MedicalSupply);
        context.AddItem(Gamma, "Нурофен", keepInStock: false);
        context.AddShopping(Gamma, requested: false, "вернуть");
        await context.ViewModel.LoadAsync();

        context.ViewModel.EnterAddMode();
        Assert.True(context.ViewModel.IsAdding);
        Assert.Equal(["Вата", "Нурофен"], context.ViewModel.Candidates.Select(row => row.Caption).ToArray());

        context.ViewModel.CancelAddMode();
        Assert.False(context.ViewModel.IsAdding);
        Assert.Single(context.Store.ShoppingItems, item => item.IsRequested);
    }

    [Fact]
    public async Task AddManual_CreatesRequestAndLeavesAddMode()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        await context.ViewModel.LoadAsync();
        context.ViewModel.EnterAddMode();

        Assert.True(await context.ViewModel.AddManualAsync(Alpha));

        Assert.False(context.ViewModel.IsAdding);
        var row = Assert.Single(context.ViewModel.Active);
        Assert.True(row.HasManualReason);
        Assert.True(Assert.Single(context.Store.ShoppingItems).IsRequested);
    }

    [Fact]
    public async Task RestoreRecentlyClosed_KeepsNote()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: false, "аптека на углу");
        await context.ViewModel.LoadAsync();

        Assert.True(await context.ViewModel.AddManualAsync(Alpha));

        var row = Assert.Single(context.ViewModel.Active);
        Assert.Equal("аптека на углу", row.Note);
        Assert.Empty(context.ViewModel.RecentlyClosed);
        Assert.Equal("аптека на углу", Assert.Single(context.Store.ShoppingItems).Note);
    }

    [Fact]
    public async Task ClearManual_MovesToRecentlyClosedWhenAutoIsAbsent()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: true, "аптека");
        await context.ViewModel.LoadAsync();

        Assert.Equal(
            ShoppingClearManualResult.Closed,
            await context.ViewModel.ClearManualAsync(Alpha));

        Assert.Empty(context.ViewModel.Active);
        Assert.Equal("аптека", Assert.Single(context.ViewModel.RecentlyClosed).Note);
        Assert.False(Assert.Single(context.Store.ShoppingItems).IsRequested);
        Assert.Null(context.Store.ShoppingItems[0].DeletedAt);
    }

    [Fact]
    public async Task ClearManual_KeepsRowWhenKeepInStockMissingRemains()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddShopping(Alpha, requested: true, "аптека");
        await context.ViewModel.LoadAsync();

        Assert.Equal(
            ShoppingClearManualResult.RemainsDueToKeepInStock,
            await context.ViewModel.ClearManualAsync(Alpha));

        var row = Assert.Single(context.ViewModel.Active);
        Assert.False(row.HasManualReason);
        Assert.True(row.HasKeepInStockMissingReason);
        Assert.Empty(context.ViewModel.RecentlyClosed);
        Assert.Equal("аптека", row.Note);
    }

    [Fact]
    public async Task UpdateNote_ChangesAndClearsWithoutCreatingHiddenRecord()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddItem(Beta, "Нурофен", keepInStock: false);
        context.AddShopping(Beta, requested: true, "было");
        await context.ViewModel.LoadAsync();

        Assert.False(await context.ViewModel.UpdateNoteAsync(Alpha, "скрытая"));
        Assert.DoesNotContain(context.Store.ShoppingItems, item => item.ItemId == Alpha);
        Assert.True(await context.ViewModel.UpdateNoteAsync(Beta, "  вечером  "));
        Assert.Equal("вечером", context.ViewModel.Active.Single(row => row.ItemId == Beta).Note);
        Assert.True(await context.ViewModel.UpdateNoteAsync(Beta, "   "));
        Assert.Null(context.ViewModel.Active.Single(row => row.ItemId == Beta).Note);
    }

    [Fact]
    public async Task Busy_BlocksRepeatedMutation()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        await context.ViewModel.LoadAsync();
        context.Store.SaveGate = new TaskCompletionSource();

        var first = context.ViewModel.AddManualAsync(Alpha);
        await context.Store.EnteredSave.Task;
        var second = await context.ViewModel.AddManualAsync(Alpha);
        context.Store.SaveGate.SetResult();
        Assert.True(await first);
        Assert.False(second);
        Assert.Single(context.Store.ShoppingItems);
    }

    [Fact]
    public async Task LoadError_KeepsPageUsable()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.Store.ReadError = new InvalidOperationException("C:\\secret\\data\\items.json");
        await context.ViewModel.LoadAsync();

        Assert.True(context.ViewModel.HasError);
        Assert.Equal("Не удалось загрузить список покупок.", context.ViewModel.ErrorMessage);
        Assert.DoesNotContain("secret", context.ViewModel.ErrorMessage, StringComparison.Ordinal);
        context.Store.ReadError = null;
        await context.ViewModel.LoadAsync();
        Assert.False(context.ViewModel.HasError);
        context.ViewModel.EnterAddMode();
        Assert.True(context.ViewModel.IsAdding);
        Assert.Equal("Ибупрофен", Assert.Single(context.ViewModel.Candidates).Caption);
    }

    [Fact]
    public async Task StaleLoad_DoesNotOverwriteFreshResult()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddShopping(Alpha, requested: true, null);
        context.Store.FirstRead = new TaskCompletionSource();
        var stale = context.ViewModel.LoadAsync();
        await context.Store.EnteredFirstRead.Task;

        context.AddItem(Beta, "Нурофен", keepInStock: false);
        context.AddShopping(Beta, requested: true, null);
        await context.ViewModel.LoadAsync();
        Assert.Equal(["Ибупрофен", "Нурофен"], context.ViewModel.Active.Select(row => row.Name).ToArray());

        context.Store.FirstRead.SetResult();
        await stale;
        Assert.Equal(["Ибупрофен", "Нурофен"], context.ViewModel.Active.Select(row => row.Name).ToArray());
    }

    private static TestContext CreateContext()
    {
        var store = new MemoryStore();
        var clock = new StubClock();
        return new TestContext(
            new ShoppingViewModel(
                new ShoppingListService(store, store, store, clock),
                new ShoppingService(store, store, clock)),
            store);
    }

    private sealed record TestContext(ShoppingViewModel ViewModel, MemoryStore Store)
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

        public void AddShopping(string itemId, bool requested, string? note) =>
            Store.ShoppingItems.Add(ShoppingItem.Create(itemId, Now, requested, note));
    }

    private sealed class MemoryStore : IInventoryRepository, IPackageRepository, IShoppingItemRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];
        public List<ShoppingItem> ShoppingItems { get; } = [];
        public Exception? ReadError { get; set; }
        public TaskCompletionSource? FirstRead { get; set; }
        public TaskCompletionSource EnteredFirstRead { get; } = new();
        public TaskCompletionSource? SaveGate { get; set; }
        public TaskCompletionSource EnteredSave { get; } = new();

        public async Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default)
        {
            if (ReadError is not null)
            {
                throw ReadError;
            }

            if (FirstRead is not null && EnteredFirstRead.TrySetResult())
            {
                await FirstRead.Task;
            }

            return Items.ToArray();
        }

        public Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>(Packages.ToArray());

        public Task SavePackageAsync(Package package, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ShoppingItem>> GetShoppingItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ShoppingItem>>(ShoppingItems.ToArray());

        public async Task SaveShoppingItemAsync(
            ShoppingItem shoppingItem,
            CancellationToken cancellationToken = default)
        {
            EnteredSave.TrySetResult();
            if (SaveGate is not null)
            {
                await SaveGate.Task;
            }

            ShoppingItems.RemoveAll(existing => existing.ItemId == shoppingItem.ItemId);
            ShoppingItems.Add(shoppingItem);
        }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public DateOnly Today { get; set; } = DateOnly.FromDateTime(Now.Date);
    }
}
