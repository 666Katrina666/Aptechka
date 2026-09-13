using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class InventoryOverviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 13);
    private const string Alpha = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Beta = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string Gamma = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string Delta = "01ARZ3NDEKTSV4RRFFQ69G5FAY";
    private const string Epsilon = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string Zeta = "01ARZ3NDEKTSV4RRFFQ69G5FB1";
    private const string PackageA = "01ARZ3NDEKTSV4RRFFQ69G5FB2";
    private const string PackageB = "01ARZ3NDEKTSV4RRFFQ69G5FB3";
    private const string PackageC = "01ARZ3NDEKTSV4RRFFQ69G5FB4";
    private const string PackageD = "01ARZ3NDEKTSV4RRFFQ69G5FB5";

    [Fact]
    public async Task GetAsync_ReadsItemsAndPackagesOnceAndDoesNotWriteOrTouchShopping()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddItem(Beta, "Бинт", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddPackage(PackageA, Alpha, StockState.Low, Today.AddDays(10));
        context.Store.ShoppingItems.Add(ShoppingItem.Create(Alpha, Now, true, "аптека"));

        var overview = await context.Service.GetAsync(null);

        Assert.Equal(2, overview.Catalog.Count);
        Assert.Equal(1, context.Store.ItemReads);
        Assert.Equal(1, context.Store.PackageReads);
        Assert.Equal(0, context.Store.ShoppingReads);
        Assert.Equal(0, context.Store.ItemWrites);
        Assert.Equal(0, context.Store.PackageWrites);
        Assert.Equal(0, context.Store.ShoppingWrites);
        Assert.Single(context.Store.ShoppingItems);
        Assert.True(context.Store.ShoppingItems[0].IsRequested);
    }

    [Fact]
    public async Task GetAsync_ExcludesArchivedItemsFromCatalogAndAttention()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true, deleted: true);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-1));

        var overview = await context.Service.GetAsync(null);

        Assert.Empty(overview.Catalog);
        Assert.Empty(overview.Attention);
    }

    [Fact]
    public async Task GetAsync_IgnoresTombstoneAndDepletedPackagesForExpiry()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-2), deleted: true);
        context.AddPackage(PackageB, Alpha, StockState.Depleted, Today.AddDays(-3));

        var overview = await context.Service.GetAsync(null);

        Assert.Equal(ItemAvailability.Missing, Assert.Single(overview.Catalog).Summary.Availability);
        Assert.Empty(overview.Attention);
    }

    [Fact]
    public async Task GetAsync_ExcludesOrdinaryMissingWithoutKeepInStock()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Бинт", keepInStock: false, InventoryItemCategory.MedicalSupply);

        var overview = await context.Service.GetAsync(null);

        Assert.Single(overview.Catalog);
        Assert.Empty(overview.Attention);
    }

    [Fact]
    public async Task GetAsync_IncludesKeepInStockMissing()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(Alpha, entry.ItemId);
        Assert.True(entry.HasKeepInStockMissing);
        Assert.Equal(ItemAvailability.Missing, entry.Availability);
        Assert.False(entry.HasLowStock);
        Assert.Equal(0, entry.ExpiredPackageCount);
        Assert.Null(entry.ExpirationWindow);
    }

    [Fact]
    public async Task GetAsync_IncludesLowButNotWhenASecondAvailablePackageExists()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Low, Today.AddDays(200));
        context.AddItem(Beta, "Нурофен", keepInStock: false);
        context.AddPackage(PackageB, Beta, StockState.Low, Today.AddDays(200));
        context.AddPackage(PackageC, Beta, StockState.Available, Today.AddDays(200));

        var overview = await context.Service.GetAsync(null);

        Assert.Equal(ItemAvailability.Low, overview.Catalog.Single(entry => entry.Item.Id == Alpha).Summary.Availability);
        Assert.Equal(ItemAvailability.Available, overview.Catalog.Single(entry => entry.Item.Id == Beta).Summary.Availability);
        var attention = Assert.Single(overview.Attention);
        Assert.Equal(Alpha, attention.ItemId);
        Assert.True(attention.HasLowStock);
        Assert.Null(attention.ExpirationWindow);
    }

    [Fact]
    public async Task GetAsync_CombinesExpiredAndKeepInStockMissing()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-1));

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(1, entry.ExpiredPackageCount);
        Assert.True(entry.HasKeepInStockMissing);
        Assert.False(entry.HasLowStock);
        Assert.Null(entry.ExpirationWindow);
        Assert.Equal(ItemAvailability.Missing, entry.Availability);
    }

    [Fact]
    public async Task GetAsync_IncludesExpiredPackages()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-1));
        context.AddPackage(PackageB, Alpha, StockState.Available, Today.AddDays(-4));

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(2, entry.ExpiredPackageCount);
        Assert.Equal(ItemAvailability.Missing, entry.Availability);
        Assert.Null(entry.NearestExpirationDate);
        Assert.Null(entry.ExpirationWindow);
    }

    [Fact]
    public async Task GetAsync_UsesEffectiveExpirationAfterOpening()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(
            PackageA,
            Alpha,
            StockState.Available,
            Today.AddDays(200),
            opened: Today.AddDays(-3),
            shelfLifeDays: 10);

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(Today.AddDays(7), entry.NearestExpirationDate);
        Assert.Equal(ExpirationAttentionWindow.Within7, entry.ExpirationWindow);
        Assert.Equal(0, entry.ExpiredPackageCount);
    }

    [Theory]
    [InlineData(0, ExpirationAttentionWindow.Within7)]
    [InlineData(7, ExpirationAttentionWindow.Within7)]
    [InlineData(8, ExpirationAttentionWindow.Within30)]
    [InlineData(30, ExpirationAttentionWindow.Within30)]
    [InlineData(31, ExpirationAttentionWindow.Within90)]
    [InlineData(90, ExpirationAttentionWindow.Within90)]
    public async Task GetAsync_ClassifiesExpirationBoundaries(int days, ExpirationAttentionWindow window)
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(days));

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(window, entry.ExpirationWindow);
        Assert.Equal(Today.AddDays(days), entry.NearestExpirationDate);
        Assert.Equal(0, entry.ExpiredPackageCount);
    }

    [Fact]
    public async Task GetAsync_ExcludesExpirationAfterNinetyOneDays()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(91));

        var overview = await context.Service.GetAsync(null);

        Assert.Single(overview.Catalog);
        Assert.Empty(overview.Attention);
    }

    [Fact]
    public async Task GetAsync_MergesSeveralReasonsIntoOneRow()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true, form: "таблетки", strength: "200 мг");
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(-2));
        context.AddPackage(PackageB, Alpha, StockState.Low, Today.AddDays(5));

        var entry = Assert.Single((await context.Service.GetAsync(null)).Attention);

        Assert.Equal(1, entry.ExpiredPackageCount);
        Assert.False(entry.HasKeepInStockMissing);
        Assert.Equal(ExpirationAttentionWindow.Within7, entry.ExpirationWindow);
        Assert.True(entry.HasLowStock);
        Assert.Equal(ItemAvailability.Low, entry.Availability);
        Assert.Equal("таблетки", entry.Form);
        Assert.Equal("200 мг", entry.Strength);
        Assert.Equal(Today.AddDays(5), entry.NearestExpirationDate);
    }

    [Fact]
    public async Task GetAsync_SortsByPriorityDateNameAndItemId()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Янтарная", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(90));
        context.AddItem(Beta, "Вата", keepInStock: false, InventoryItemCategory.MedicalSupply);
        context.AddPackage(PackageB, Beta, StockState.Low);
        context.AddItem(Gamma, "Нурофен", keepInStock: true);
        context.AddItem(Delta, "Аспирин", keepInStock: false);
        context.AddPackage(PackageC, Delta, StockState.Available, Today.AddDays(3));
        context.AddItem(Epsilon, "Перекись", keepInStock: false);
        context.AddPackage(PackageD, Epsilon, StockState.Available, Today.AddDays(-1));
        context.AddItem(Zeta, "Йод", keepInStock: false);
        context.AddPackage("01ARZ3NDEKTSV4RRFFQ69G5FB6", Zeta, StockState.Available, Today.AddDays(20));

        var names = (await context.Service.GetAsync(null)).Attention.Select(entry => entry.Name).ToArray();

        Assert.Equal(["Перекись", "Нурофен", "Аспирин", "Вата", "Йод", "Янтарная"], names);
    }

    [Fact]
    public async Task GetAsync_SortsSamePriorityByDateThenNameThenItemId()
    {
        var context = CreateContext();
        context.AddItem(Gamma, "Бета", keepInStock: false);
        context.AddPackage(PackageC, Gamma, StockState.Available, Today.AddDays(2));
        context.AddItem(Alpha, "Альфа", keepInStock: false);
        context.AddPackage(PackageA, Alpha, StockState.Available, Today.AddDays(6));
        context.AddItem(Beta, "Альфа", keepInStock: false);
        context.AddPackage(PackageB, Beta, StockState.Available, Today.AddDays(6));

        var ids = (await context.Service.GetAsync(null)).Attention.Select(entry => entry.ItemId).ToArray();

        Assert.Equal([Gamma, Alpha, Beta], ids);
    }

    [Fact]
    public async Task GetAsync_FiltersCatalogByQueryButKeepsFullAttention()
    {
        var context = CreateContext();
        context.AddItem(Alpha, "Ибупрофен", keepInStock: true, aliases: ["Нурофен"], ingredients: ["ибупрофен"]);
        context.AddItem(Beta, "Бинт", keepInStock: false, InventoryItemCategory.MedicalSupply);

        var overview = await context.Service.GetAsync("БИНТ");

        Assert.Equal("Бинт", Assert.Single(overview.Catalog).Item.Name);
        Assert.Equal("Ибупрофен", Assert.Single(overview.Attention).Name);

        var byAlias = await context.Service.GetAsync("нурофен");
        Assert.Equal("Ибупрофен", Assert.Single(byAlias.Catalog).Item.Name);
        Assert.Equal("Ибупрофен", Assert.Single(byAlias.Attention).Name);
        Assert.Equal(2, context.Store.ItemReads);
        Assert.Equal(2, context.Store.PackageReads);
    }

    private static TestContext CreateContext()
    {
        var store = new CountingStore();
        return new TestContext(new InventoryOverviewService(store, store, new StubClock()), store);
    }

    private sealed record TestContext(InventoryOverviewService Service, CountingStore Store)
    {
        public void AddItem(
            string id,
            string name,
            bool keepInStock,
            InventoryItemCategory category = InventoryItemCategory.Medicine,
            bool deleted = false,
            string? form = null,
            string? strength = null,
            IReadOnlyList<string>? aliases = null,
            IReadOnlyList<string>? ingredients = null)
        {
            var item = InventoryItem.Create(
                id,
                Now,
                name,
                aliases ?? [],
                category,
                ingredients ?? [],
                form ?? (category == InventoryItemCategory.Medicine ? "таблетки" : null),
                strength ?? (category == InventoryItemCategory.Medicine ? "200 мг" : null),
                null,
                keepInStock);
            Store.Items.Add(deleted ? item.Delete(Now.AddMinutes(1)) : item);
        }

        public void AddPackage(
            string id,
            string itemId,
            StockState stockState,
            DateOnly? expiration = null,
            bool deleted = false,
            DateOnly? opened = null,
            int? shelfLifeDays = null)
        {
            var package = Package.Create(
                id,
                Now,
                itemId,
                null,
                expiration,
                expiration is null ? null : ExpirationPrecision.Day,
                opened,
                shelfLifeDays,
                stockState,
                null);
            Store.Packages.Add(deleted ? package.Delete(Now.AddMinutes(1)) : package);
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
        public int ItemWrites { get; private set; }
        public int PackageWrites { get; private set; }
        public int ShoppingWrites { get; private set; }

        public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default)
        {
            ItemReads++;
            return Task.FromResult<IReadOnlyList<InventoryItem>>(Items.ToArray());
        }

        public Task SaveItemAsync(InventoryItem item, CancellationToken cancellationToken = default)
        {
            ItemWrites++;
            throw new InvalidOperationException("Overview must not write items.");
        }

        public Task<IReadOnlyList<Package>> GetPackagesAsync(
            CancellationToken cancellationToken = default)
        {
            PackageReads++;
            return Task.FromResult<IReadOnlyList<Package>>(Packages.ToArray());
        }

        public Task SavePackageAsync(Package package, CancellationToken cancellationToken = default)
        {
            PackageWrites++;
            throw new InvalidOperationException("Overview must not write packages.");
        }

        public Task<IReadOnlyList<ShoppingItem>> GetShoppingItemsAsync(
            CancellationToken cancellationToken = default)
        {
            ShoppingReads++;
            return Task.FromResult<IReadOnlyList<ShoppingItem>>(ShoppingItems.ToArray());
        }

        public Task SaveShoppingItemAsync(
            ShoppingItem shoppingItem,
            CancellationToken cancellationToken = default)
        {
            ShoppingWrites++;
            throw new InvalidOperationException("Overview must not write shopping items.");
        }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public DateOnly Today { get; set; } = InventoryOverviewServiceTests.Today;
    }
}
