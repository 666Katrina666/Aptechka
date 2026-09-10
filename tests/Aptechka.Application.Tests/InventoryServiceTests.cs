using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class InventoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private const string FirstId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";

    [Fact]
    public async Task CreateAsync_CreatesIndependentItems()
    {
        var context = CreateContext();

        var first = await context.Service.CreateAsync(Draft("Ибупрофен"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var second = await context.Service.CreateAsync(Draft("Вата", InventoryItemCategory.MedicalSupply));

        Assert.Equal(FirstId, first.Id);
        Assert.Equal(SecondId, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, context.Repository.Items.Count);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesOnlyTheRequestedItem()
    {
        var context = CreateContext();
        var first = await context.Service.CreateAsync(Draft("Ибупрофен"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var second = await context.Service.CreateAsync(Draft("Вата", InventoryItemCategory.MedicalSupply));

        context.Clock.UtcNow = Now.AddMinutes(2);
        var updated = await context.Service.UpdateAsync(
            second.Id,
            Draft("Вата медицинская", InventoryItemCategory.MedicalSupply, ["вата"]));

        Assert.Equal(second.Id, updated.Id);
        Assert.Equal("Вата медицинская", updated.Name);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("Ибупрофен", (await context.Service.GetItemAsync(first.Id))!.Name);
        Assert.Equal(1, (await context.Service.GetItemAsync(first.Id))!.Revision);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_DoesNotCreateAnItem()
    {
        var context = CreateContext();
        await context.Service.CreateAsync(Draft("Ибупрофен"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.UpdateAsync(SecondId, Draft("Новая позиция")));

        Assert.Single(context.Repository.Items);
        Assert.Equal(FirstId, context.Repository.Items[0].Id);
    }

    [Fact]
    public async Task GetCatalogAsync_ExcludesArchivedItems()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Ибупрофен"));

        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.ArchiveAsync(created.Id);

        Assert.Empty(await context.Service.GetCatalogAsync());
        Assert.NotNull((await context.Service.GetItemAsync(created.Id))!.DeletedAt);
    }

    [Theory]
    [InlineData("ибупрофен", "Ибупрофен")]
    [InlineData("нурофен", "Ибупрофен")]
    [InlineData("хлоргекс", "Хлоргексидин")]
    [InlineData("НУРОФЕН", "Ибупрофен")]
    public async Task SearchAsync_MatchesNameAliasAndIngredientIgnoringCase(
        string query,
        string expectedName)
    {
        var context = await CreatePopulatedContext();

        var results = await context.Service.SearchAsync(query);

        Assert.Equal(expectedName, Assert.Single(results).Name);
    }

    [Fact]
    public async Task FindNameConflictsAsync_MatchesExistingNameIgnoringCase()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Ибупрофен", aliases: ["Нурофен"]));

        var conflicts = await context.Service.FindNameConflictsAsync("  ибупрофен  ");

        var match = Assert.Single(conflicts);
        Assert.Equal(created.Id, match.Id);
    }

    [Fact]
    public async Task FindNameConflictsAsync_MatchesExistingAliasIgnoringCase()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Ибупрофен", aliases: ["Нурофен"]));

        var conflicts = await context.Service.FindNameConflictsAsync("НУРОФЕН");

        var match = Assert.Single(conflicts);
        Assert.Equal(created.Id, match.Id);
        Assert.Equal("Ибупрофен", match.Name);
    }

    [Fact]
    public async Task FindNameConflictsAsync_IgnoresArchivedItems()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Ибупрофен", aliases: ["Нурофен"]));
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.ArchiveAsync(created.Id);

        Assert.Empty(await context.Service.FindNameConflictsAsync("Ибупрофен"));
        Assert.Empty(await context.Service.FindNameConflictsAsync("Нурофен"));
    }

    [Fact]
    public async Task FindNameConflictsAsync_ReturnsEveryMatchingItem()
    {
        var context = CreateContext();
        var named = await context.Service.CreateAsync(Draft("Ибупрофен"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var aliased = await context.Service.CreateAsync(Draft("Нурофен", aliases: ["ибупрофен"]));

        var conflicts = await context.Service.FindNameConflictsAsync("Ибупрофен");

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, item => item.Id == named.Id);
        Assert.Contains(conflicts, item => item.Id == aliased.Id);
    }

    [Fact]
    public void NameConflictChoice_ResolveOpenedItemId_SelectsTheChosenItem()
    {
        var first = Item(FirstId, "Ибупрофен", "200 мг");
        var second = Item(SecondId, "Ибупрофен", "400 мг");
        var conflicts = new[] { first, second };

        Assert.Equal(
            second.Id,
            NameConflictChoice.ResolveOpenedItemId(conflicts, NameConflictChoice.OpenLabel(second)));
        Assert.Null(NameConflictChoice.ResolveOpenedItemId(conflicts, NameConflictChoice.CreateAnyway));
        Assert.Null(NameConflictChoice.ResolveOpenedItemId(conflicts, "Открыть существующую"));
        Assert.Null(NameConflictChoice.ResolveOpenedItemId(conflicts, null));

        var identical = new[] { first, Item(SecondId, "Ибупрофен", "200 мг") };
        var secondLabel = NameConflictChoice.OpenLabels(identical)[1];
        Assert.Equal(SecondId, NameConflictChoice.ResolveOpenedItemId(identical, secondLabel));
        Assert.NotEqual(NameConflictChoice.OpenLabel(identical[0]), secondLabel);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsCatalogSortedByName()
    {
        var context = await CreatePopulatedContext();

        var results = await context.Service.SearchAsync("   ");

        Assert.Equal(["Бинт", "Ибупрофен", "Хлоргексидин"], results.Select(item => item.Name));
    }

    private static async Task<TestContext> CreatePopulatedContext()
    {
        var context = CreateContext();
        await context.Service.CreateAsync(new InventoryItemDraft(
            "Ибупрофен",
            ["Нурофен"],
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "200 мг",
            null,
            true));
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.CreateAsync(new InventoryItemDraft(
            "Хлоргексидин",
            [],
            InventoryItemCategory.Medicine,
            ["хлоргексидин"],
            "раствор",
            "0.05%",
            null,
            false));
        context.Clock.UtcNow = Now.AddMinutes(2);
        await context.Service.CreateAsync(new InventoryItemDraft(
            "Бинт",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            false));
        return context;
    }

    private static TestContext CreateContext()
    {
        var repository = new InMemoryRepository();
        var clock = new StubClock(Now);
        var service = new InventoryService(repository, clock, new StubIdGenerator());
        return new TestContext(service, repository, clock);
    }

    private static InventoryItem Item(
        string id,
        string name,
        string? strength) =>
        InventoryItem.Create(
            id,
            Now,
            name,
            [],
            InventoryItemCategory.Medicine,
            [],
            "таблетки",
            strength,
            null,
            false);

    private static InventoryItemDraft Draft(
        string name,
        InventoryItemCategory category = InventoryItemCategory.Medicine,
        IReadOnlyList<string>? aliases = null) =>
        new(name, aliases ?? [], category, [], null, null, null, false);

    private sealed record TestContext(
        InventoryService Service,
        InMemoryRepository Repository,
        StubClock Clock);

    private sealed class InMemoryRepository : IInventoryRepository
    {
        public List<InventoryItem> Items { get; } = [];

        public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>(Items.ToArray());

        public Task SaveItemAsync(
            InventoryItem item,
            CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(existing => existing.Id == item.Id);
            Items.Add(item);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class StubIdGenerator : IIdGenerator
    {
        private readonly Queue<string> ids = new([FirstId, SecondId, "01ARZ3NDEKTSV4RRFFQ69G5FAX"]);

        public string Create(DateTimeOffset timestamp) => ids.Dequeue();
    }
}
