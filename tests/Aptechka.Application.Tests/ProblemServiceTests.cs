using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class ProblemServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 30, 0, TimeSpan.Zero);
    private const string FirstId = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string SecondId = "01ARZ3NDEKTSV4RRFFQ69G5FB1";
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string MissingItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";

    [Fact]
    public async Task CreateUpdateAndArchive_RoundTripActiveCatalog()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Головная боль", ["болит голова"], [ItemId]));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var updated = await context.Service.UpdateAsync(
            created.Id,
            Draft("Мигрень", ["головная боль"], [ItemId], "дома"));

        Assert.Equal(FirstId, created.Id);
        Assert.Equal("Мигрень", updated.Name);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("дома", updated.Note);
        Assert.Equal("Мигрень", Assert.Single(await context.Service.GetCatalogAsync()).Name);

        context.Clock.UtcNow = Now.AddMinutes(2);
        var archived = await context.Service.ArchiveAsync(created.Id);
        Assert.Equal(Now.AddMinutes(2), archived.DeletedAt);
        Assert.Empty(await context.Service.GetCatalogAsync());
        Assert.NotNull((await context.Service.GetProblemAsync(created.Id))!.DeletedAt);
    }

    [Fact]
    public async Task SearchAsync_MatchesNameAndAliasIgnoringCaseAndIgnoresNote()
    {
        var context = CreateContext();
        await context.Service.CreateAsync(Draft("Головная боль", ["болит голова"], note: "ибупрофен дома"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.CreateAsync(Draft("Кашель", ["саднит горло"]));

        Assert.Equal("Головная боль", Assert.Single(await context.Service.SearchAsync("головн")).Name);
        Assert.Equal("Головная боль", Assert.Single(await context.Service.SearchAsync("БОЛИТ")).Name);
        Assert.Empty(await context.Service.SearchAsync("ибупрофен"));
        Assert.Equal("Кашель", Assert.Single(await context.Service.SearchAsync("горло")).Name);
    }

    [Fact]
    public async Task SearchAndCatalog_ExcludeTombstones()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Головная боль", ["болит голова"]));
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Service.ArchiveAsync(created.Id);

        Assert.Empty(await context.Service.GetCatalogAsync());
        Assert.Empty(await context.Service.SearchAsync("голова"));
        Assert.Empty(await context.Service.FindNameMatchesAsync("Головная боль"));
    }

    [Fact]
    public async Task FindNameMatchesAsync_ReturnsEveryMatchIgnoringCase()
    {
        var context = CreateContext();
        var named = await context.Service.CreateAsync(Draft("Головная боль"));
        context.Clock.UtcNow = Now.AddMinutes(1);
        var aliased = await context.Service.CreateAsync(Draft("Мигрень", ["головная боль"]));

        var matches = await context.Service.FindNameMatchesAsync("  ГОЛОВНАЯ БОЛЬ  ");

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, problem => problem.Id == named.Id);
        Assert.Contains(matches, problem => problem.Id == aliased.Id);
    }

    [Fact]
    public async Task LinkAndUnlink_KeepExactItemIdsEvenWhenItemIsMissing()
    {
        var context = CreateContext();
        var created = await context.Service.CreateAsync(Draft("Головная боль"));

        context.Clock.UtcNow = Now.AddMinutes(1);
        var linked = await context.Service.LinkItemAsync(created.Id, MissingItemId);
        context.Clock.UtcNow = Now.AddMinutes(2);
        var stillLinked = await context.Service.LinkItemAsync(created.Id, MissingItemId);
        context.Clock.UtcNow = Now.AddMinutes(3);
        var unlinked = await context.Service.UnlinkItemAsync(created.Id, MissingItemId);

        Assert.Equal([MissingItemId], linked.ItemIds);
        Assert.Equal(2, linked.Revision);
        Assert.Same(linked, stillLinked);
        Assert.Empty(unlinked.ItemIds);
        Assert.Equal(3, unlinked.Revision);
    }

    [Fact]
    public async Task UnknownId_DoesNotCreateAProblem()
    {
        var context = CreateContext();
        await context.Service.CreateAsync(Draft("Головная боль"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.UpdateAsync(SecondId, Draft("Новая")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.ArchiveAsync(SecondId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.LinkItemAsync(SecondId, ItemId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.UnlinkItemAsync(SecondId, ItemId));

        Assert.Single(context.Repository.Problems);
        Assert.Equal(FirstId, context.Repository.Problems[0].Id);
    }

    [Fact]
    public async Task ArchiveItem_DoesNotRemoveProblemLinks()
    {
        var store = new InMemoryStore();
        var clock = new StubClock(Now);
        var ids = new StubIdGenerator(ItemId, FirstId);
        var inventory = new InventoryService(store, store, clock, ids);
        var problems = new ProblemService(store, clock, ids);
        var item = await inventory.CreateAsync(new InventoryItemDraft(
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            false));
        clock.UtcNow = Now.AddMinutes(1);
        var problem = await problems.CreateAsync(Draft("Головная боль", itemIds: [item.Id]));

        clock.UtcNow = Now.AddMinutes(2);
        await inventory.ArchiveAsync(item.Id);

        var loaded = await problems.GetProblemAsync(problem.Id);
        Assert.Equal([item.Id], loaded!.ItemIds);
        Assert.Null(loaded.DeletedAt);
        Assert.NotNull((await inventory.GetItemAsync(item.Id))!.DeletedAt);
    }

    private static TestContext CreateContext()
    {
        var repository = new InMemoryStore();
        var clock = new StubClock(Now);
        return new TestContext(new ProblemService(repository, clock, new StubIdGenerator()), repository, clock);
    }

    private static ProblemDraft Draft(
        string name,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? itemIds = null,
        string? note = null) =>
        new(name, aliases ?? [], itemIds ?? [], note);

    private sealed record TestContext(
        ProblemService Service,
        InMemoryStore Repository,
        StubClock Clock);

    private sealed class InMemoryStore : IInventoryRepository, IPackageRepository, IProblemRepository
    {
        public List<InventoryItem> Items { get; } = [];
        public List<Package> Packages { get; } = [];
        public List<Problem> Problems { get; } = [];

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

        public Task<IReadOnlyList<Problem>> GetProblemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Problem>>(Problems.ToArray());

        public Task SaveProblemAsync(Problem problem, CancellationToken cancellationToken = default)
        {
            Problems.RemoveAll(existing => existing.Id == problem.Id);
            Problems.Add(problem);
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
        private readonly Queue<string> ids = new(
            values.Length == 0 ? [FirstId, SecondId, MissingItemId] : values);

        public string Create(DateTimeOffset timestamp) => ids.Dequeue();
    }
}
