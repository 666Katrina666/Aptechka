using System.Text;
using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Application.Tests;

public sealed class FileInventoryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private const string FirstId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";

    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "aptechka-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveItem_WritesManifestAndCamelCaseItemAtomically()
    {
        var repository = CreateRepository();
        var item = CreateItem(FirstId, "Ибупрофен");

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

    [Fact]
    public async Task SaveItem_WritesEachItemToItsOwnFile()
    {
        var repository = CreateRepository();
        await repository.SaveItemAsync(CreateItem(FirstId, "Ибупрофен"));
        await repository.SaveItemAsync(CreateItem(
            SecondId,
            "Вата",
            InventoryItemCategory.MedicalSupply));

        Assert.True(File.Exists(Path.Combine(rootPath, "items", $"{FirstId}.json")));
        Assert.True(File.Exists(Path.Combine(rootPath, "items", $"{SecondId}.json")));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        var loaded = await repository.GetItemsAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, item => item.Id == FirstId && item.Name == "Ибупрофен");
        Assert.Contains(loaded, item => item.Id == SecondId && item.Name == "Вата");
    }

    [Fact]
    public async Task GetItems_ReadsExistingP1JsonWithoutMigration()
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "items"));
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "aptechka.json"),
            """
            {
              "schemaVersion": 1,
              "datasetId": "01ARZ3NDEKTSV4RRFFQ69G5FAZ",
              "createdAt": "2026-08-31T08:00:00+00:00"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "items", $"{FirstId}.json"),
            """
            {
              "id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
              "revision": 1,
              "createdAt": "2026-08-31T08:00:00+00:00",
              "updatedAt": "2026-08-31T08:00:00+00:00",
              "deletedAt": null,
              "name": "Ибупрофен",
              "aliases": [],
              "category": "medicine",
              "activeIngredients": ["ибупрофен"],
              "form": "таблетки",
              "strength": "200 мг",
              "description": null,
              "keepInStock": true,
              "coverPhotoId": null
            }
            """);

        var loaded = await CreateRepository().GetItemsAsync();
        var item = Assert.Single(loaded);

        Assert.Equal(FirstId, item.Id);
        Assert.Equal("Ибупрофен", item.Name);
        Assert.Empty(item.Aliases);
        Assert.Equal(InventoryItemCategory.Medicine, item.Category);
        Assert.Equal(["ибупрофен"], item.ActiveIngredients);
        Assert.Equal("таблетки", item.Form);
        Assert.Equal("200 мг", item.Strength);
        Assert.True(item.KeepInStock);
        Assert.Null(item.DeletedAt);
    }

    [Fact]
    public async Task ReadAsync_KeepsTombstoneInSnapshot()
    {
        var repository = CreateRepository();
        var item = CreateItem(FirstId, "Ибупрофен");
        await repository.SaveItemAsync(item);
        await repository.SaveItemAsync(item.Delete(Now.AddMinutes(3)));

        var snapshot = await repository.ReadAsync();
        var relativePath = $"items/{FirstId}.json";

        Assert.True(snapshot.Files.ContainsKey(relativePath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(snapshot.Files[relativePath]));
        Assert.Equal(2, document.RootElement.GetProperty("revision").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("deletedAt").ValueKind);
        Assert.True(File.Exists(Path.Combine(rootPath, "items", $"{FirstId}.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, true);
        }
    }

    private FileInventoryRepository CreateRepository() =>
        new(rootPath, new StubClock(Now), new SequenceIdGenerator());

    private static InventoryItem CreateItem(
        string id,
        string name,
        InventoryItemCategory category = InventoryItemCategory.Medicine) =>
        InventoryItem.Create(
            id,
            Now,
            name,
            [],
            category,
            category == InventoryItemCategory.Medicine ? ["ибупрофен"] : [],
            category == InventoryItemCategory.Medicine ? "таблетки" : null,
            category == InventoryItemCategory.Medicine ? "200 мг" : null,
            null,
            category == InventoryItemCategory.Medicine);

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public string Create(DateTimeOffset timestamp) => "01ARZ3NDEKTSV4RRFFQ69G5FAZ";
    }
}
