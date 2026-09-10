using System.Text;
using System.Text.Json;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;

namespace Aptechka.Application.Tests;

public sealed class FileInventoryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private const string FirstId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string FirstPackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string SecondPackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAY";

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

    [Fact]
    public async Task SavePackage_WritesEachPackageToItsOwnFileWithDataModelContract()
    {
        var repository = CreateRepository();
        await repository.SaveItemAsync(CreateItem(FirstId, "Ибупрофен"));
        await repository.SavePackageAsync(CreatePackage(FirstPackageId, FirstId, "блистер"));
        await repository.SavePackageAsync(CreatePackage(SecondPackageId, FirstId, "коробка"));

        var firstPath = Path.Combine(rootPath, "packages", $"{FirstPackageId}.json");
        var secondPath = Path.Combine(rootPath, "packages", $"{SecondPackageId}.json");
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(firstPath));
        Assert.Equal(FirstPackageId, document.RootElement.GetProperty("id").GetString());
        Assert.Equal(FirstId, document.RootElement.GetProperty("itemId").GetString());
        Assert.Equal("блистер", document.RootElement.GetProperty("label").GetString());
        Assert.Equal("2027-04-30", document.RootElement.GetProperty("expirationDate").GetString());
        Assert.Equal("month", document.RootElement.GetProperty("expirationPrecision").GetString());
        Assert.Equal("available", document.RootElement.GetProperty("stockState").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("openedDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("note").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("availability", out _));
    }

    [Fact]
    public async Task GetPackages_ReadsSavedPackagesAfterRestart()
    {
        var repository = CreateRepository();
        await repository.SaveItemAsync(CreateItem(FirstId, "Ибупрофен"));
        await repository.SavePackageAsync(CreatePackage(FirstPackageId, FirstId, "блистер"));

        var reloaded = CreateRepository();
        var loaded = Assert.Single(await reloaded.GetPackagesAsync());
        Assert.Equal(FirstPackageId, loaded.Id);
        Assert.Equal(FirstId, loaded.ItemId);
        Assert.Equal("блистер", loaded.Label);
        Assert.Equal(new DateOnly(2027, 4, 30), loaded.ExpirationDate);
        Assert.Equal(ExpirationPrecision.Month, loaded.ExpirationPrecision);
        Assert.Equal(StockState.Available, loaded.StockState);
    }

    [Fact]
    public async Task ReadAsync_KeepsPackageTombstoneInSnapshot()
    {
        var repository = CreateRepository();
        await repository.SaveItemAsync(CreateItem(FirstId, "Ибупрофен"));
        var package = CreatePackage(FirstPackageId, FirstId, "блистер");
        await repository.SavePackageAsync(package);
        await repository.SavePackageAsync(package.Delete(Now.AddMinutes(3)));

        var snapshot = await repository.ReadAsync();
        var relativePath = $"packages/{FirstPackageId}.json";
        Assert.True(snapshot.Files.ContainsKey(relativePath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(snapshot.Files[relativePath]));
        Assert.Equal(2, document.RootElement.GetProperty("revision").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("deletedAt").ValueKind);
        Assert.True(File.Exists(Path.Combine(rootPath, "packages", $"{FirstPackageId}.json")));
    }

    [Fact]
    public async Task ReplaceAsync_AcceptsP2SnapshotWithoutPackages()
    {
        var repository = CreateRepository();

        await repository.ReplaceAsync(Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false))));

        var loaded = Assert.Single(await repository.GetItemsAsync());
        Assert.Equal("Ибупрофен", loaded.Name);
        Assert.Empty(await repository.GetPackagesAsync());
        Assert.False(Directory.Exists(Path.Combine(rootPath, "packages")));
    }

    [Fact]
    public async Task ReplaceAsync_RejectsPackageWhenFileNameDoesNotMatchId()
    {
        var repository = CreateRepository();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)),
                ($"packages/{SecondPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)))));

        Assert.Contains("упаковки", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsActivePackageWithoutActiveItem()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: true)),
                ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)))));
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

    private static Package CreatePackage(string id, string itemId, string label) =>
        Package.Create(
            id,
            Now,
            itemId,
            label,
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Month,
            null,
            null,
            StockState.Available,
            null);

    private static DataSnapshot Snapshot(params (string Path, string Content)[] files) => new(
        files.ToDictionary(
            static file => file.Path,
            static file => Encoding.UTF8.GetBytes(file.Content.ReplaceLineEndings("\n")),
            StringComparer.Ordinal));

    private const string ManifestJson =
        """
        {
          "schemaVersion": 1,
          "datasetId": "01ARZ3NDEKTSV4RRFFQ69G5FAZ",
          "createdAt": "2026-08-31T08:00:00+00:00"
        }
        """;

    private static string ItemJson(string id, string name, bool deleted) =>
        $$"""
        {
          "id": "{{id}}",
          "revision": 1,
          "createdAt": "2026-08-31T08:00:00+00:00",
          "updatedAt": "2026-08-31T08:00:00+00:00",
          "deletedAt": {{(deleted ? "\"2026-08-31T09:00:00+00:00\"" : "null")}},
          "name": "{{name}}",
          "aliases": [],
          "category": "medicine",
          "activeIngredients": ["ибупрофен"],
          "form": "таблетки",
          "strength": "200 мг",
          "description": null,
          "keepInStock": true,
          "coverPhotoId": null
        }
        """;

    private static string PackageJson(string id, string itemId, bool deleted) =>
        $$"""
        {
          "id": "{{id}}",
          "revision": 1,
          "createdAt": "2026-08-31T18:25:00+00:00",
          "updatedAt": "2026-08-31T18:25:00+00:00",
          "deletedAt": {{(deleted ? "\"2026-08-31T19:00:00+00:00\"" : "null")}},
          "itemId": "{{itemId}}",
          "label": null,
          "expirationDate": "2027-04-30",
          "expirationPrecision": "month",
          "openedDate": null,
          "shelfLifeAfterOpeningDays": null,
          "stockState": "available",
          "note": null
        }
        """;

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
        public DateOnly Today => DateOnly.FromDateTime(utcNow.Date);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public string Create(DateTimeOffset timestamp) => "01ARZ3NDEKTSV4RRFFQ69G5FAZ";
    }
}
