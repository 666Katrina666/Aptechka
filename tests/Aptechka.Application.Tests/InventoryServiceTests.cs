using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.Application.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task SavePrototypeItem_CreatesThenUpdatesSingleItem()
    {
        var repository = new InMemoryRepository();
        var clock = new StubClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero));
        var service = new InventoryService(repository, clock, new StubIdGenerator());

        var created = await service.SavePrototypeItemAsync(new InventoryItemDraft(
            "Ибупрофен",
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "200 мг",
            null,
            true));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var updated = await service.SavePrototypeItemAsync(new InventoryItemDraft(
            "Ибупрофен",
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "400 мг",
            null,
            true));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("400 мг", updated.Strength);
        Assert.Single(repository.Items);
    }

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
        public string Create(DateTimeOffset timestamp) => "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    }
}
