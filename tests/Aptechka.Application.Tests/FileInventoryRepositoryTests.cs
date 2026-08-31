using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Application.Tests;

public sealed class FileInventoryRepositoryTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "aptechka-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveItem_WritesManifestAndCamelCaseItemAtomically()
    {
        var now = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        var repository = new FileInventoryRepository(
            rootPath,
            new StubClock(now),
            new SequenceIdGenerator());
        var item = InventoryItem.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            now,
            "Ибупрофен",
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "200 мг",
            null,
            true);

        await repository.SaveItemAsync(item);

        var manifestPath = Path.Combine(rootPath, "aptechka.json");
        var itemPath = Path.Combine(rootPath, "items", $"{item.Id}.json");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(itemPath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(itemPath));
        Assert.Equal("Ибупрофен", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("medicine", document.RootElement.GetProperty("category").GetString());

        var loaded = await repository.GetItemsAsync();
        var loadedItem = Assert.Single(loaded);
        Assert.Equal(item.Id, loadedItem.Id);
        Assert.Equal(item.Name, loadedItem.Name);
        Assert.Equal(item.ActiveIngredients, loadedItem.ActiveIngredients);
        Assert.Equal(item.Category, loadedItem.Category);
        Assert.Equal(item.KeepInStock, loadedItem.KeepInStock);
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, true);
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public string Create(DateTimeOffset timestamp) => "01ARZ3NDEKTSV4RRFFQ69G5FAZ";
    }
}
