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
    private const string ProblemId = "01ARZ3NDEKTSV4RRFFQ69G5FB0";

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

    [Fact]
    public async Task TryReplaceAsync_ReplacesWhenExpectedMatchesCurrent()
    {
        var repository = CreateRepository();
        var original = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)));
        var replacement = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Нурофен", deleted: false)));
        await repository.ReplaceAsync(original);

        var replaced = await repository.TryReplaceAsync(original, replacement);
        var loaded = await repository.ReadAsync();

        Assert.True(replaced);
        Assert.True(loaded.HasSameFiles(replacement));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.GetDirectoryName(rootPath)!,
            Path.GetFileName(rootPath) + ".staging-*"));
    }

    [Fact]
    public async Task TryReplaceAsync_KeepsCurrentWhenExpectedDiffers()
    {
        var repository = CreateRepository();
        var original = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)));
        var stale = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Нурофен", deleted: false)));
        var attempted = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{SecondId}.json", ItemJson(SecondId, "Вата", deleted: false)));
        await repository.ReplaceAsync(original);

        var replaced = await repository.TryReplaceAsync(stale, attempted);
        var loaded = await repository.ReadAsync();

        Assert.False(replaced);
        Assert.True(loaded.HasSameFiles(original));
        Assert.False(File.Exists(Path.Combine(rootPath, "items", $"{SecondId}.json")));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.GetDirectoryName(rootPath)!,
            Path.GetFileName(rootPath) + ".staging-*"));
    }

    [Fact]
    public async Task TryReplaceAsync_SecondCallWithStaleExpectedFails()
    {
        var repository = CreateRepository();
        var first = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)));
        var second = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Нурофен", deleted: false)));
        await repository.ReplaceAsync(first);

        Assert.True(await repository.TryReplaceAsync(first, second));
        Assert.False(await repository.TryReplaceAsync(first, first));
        Assert.True((await repository.ReadAsync()).HasSameFiles(second));
    }

    [Fact]
    public async Task SaveProblem_WritesEachProblemToItsOwnFile()
    {
        var repository = CreateRepository();
        var problem = CreateProblem(ProblemId, "Головная боль", [FirstId], "дома");

        await repository.SaveProblemAsync(problem);

        var problemPath = Path.Combine(rootPath, "problems", $"{ProblemId}.json");
        Assert.True(File.Exists(problemPath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(problemPath));
        Assert.Equal(ProblemId, document.RootElement.GetProperty("id").GetString());
        Assert.Equal("Головная боль", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("болит голова", document.RootElement.GetProperty("aliases")[0].GetString());
        Assert.Equal(FirstId, document.RootElement.GetProperty("itemIds")[0].GetString());
        Assert.Equal("дома", document.RootElement.GetProperty("note").GetString());

        var loaded = Assert.Single(await repository.GetProblemsAsync());
        Assert.Equal(problem.Name, loaded.Name);
        Assert.Equal(problem.Aliases, loaded.Aliases);
        Assert.Equal(problem.ItemIds, loaded.ItemIds);
        Assert.Equal(problem.Note, loaded.Note);
    }

    [Fact]
    public async Task ReadAsync_KeepsProblemTombstoneInSnapshot()
    {
        var repository = CreateRepository();
        var problem = CreateProblem(ProblemId, "Головная боль", [FirstId], null);
        await repository.SaveProblemAsync(problem);
        await repository.SaveProblemAsync(problem.Delete(Now.AddMinutes(3)));

        var snapshot = await repository.ReadAsync();
        var relativePath = $"problems/{ProblemId}.json";
        Assert.True(snapshot.Files.ContainsKey(relativePath));
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(snapshot.Files[relativePath]));
        Assert.Equal(2, document.RootElement.GetProperty("revision").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("deletedAt").ValueKind);
        Assert.True(File.Exists(Path.Combine(rootPath, "problems", $"{ProblemId}.json")));
    }

    [Fact]
    public async Task ReplaceAsync_AcceptsP4SnapshotWithoutProblems()
    {
        var repository = CreateRepository();

        await repository.ReplaceAsync(Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)),
            ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false))));

        Assert.Equal("Ибупрофен", Assert.Single(await repository.GetItemsAsync()).Name);
        Assert.Single(await repository.GetPackagesAsync());
        Assert.Empty(await repository.GetProblemsAsync());
        Assert.False(Directory.Exists(Path.Combine(rootPath, "problems")));
    }

    [Fact]
    public async Task TryReplaceAsync_DoesNotLoseProblems()
    {
        var repository = CreateRepository();
        var original = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)),
            ($"problems/{ProblemId}.json", ProblemJson(ProblemId, "Головная боль", FirstId, deleted: false)));
        var replacement = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Нурофен", deleted: false)),
            ($"problems/{ProblemId}.json", ProblemJson(ProblemId, "Головная боль", FirstId, deleted: false)));
        await repository.ReplaceAsync(original);

        Assert.True(await repository.TryReplaceAsync(original, replacement));
        var loaded = await repository.ReadAsync();
        Assert.True(loaded.HasSameFiles(replacement));
        Assert.True(loaded.Files.ContainsKey($"problems/{ProblemId}.json"));
        Assert.Equal("Головная боль", Assert.Single(await repository.GetProblemsAsync()).Name);
    }

    [Fact]
    public async Task SaveShoppingItem_WritesEachRecordToItsOwnFile()
    {
        var repository = CreateRepository();
        var shopping = CreateShopping(FirstId, true, "аптека");

        await repository.SaveShoppingItemAsync(shopping);

        var shoppingPath = Path.Combine(rootPath, "shopping", $"{FirstId}.json");
        Assert.True(File.Exists(shoppingPath));
        Assert.Empty(Directory.EnumerateFiles(rootPath, "*.tmp", SearchOption.AllDirectories));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(shoppingPath));
        Assert.Equal(FirstId, document.RootElement.GetProperty("id").GetString());
        Assert.Equal(FirstId, document.RootElement.GetProperty("itemId").GetString());
        Assert.True(document.RootElement.GetProperty("isRequested").GetBoolean());
        Assert.Equal("аптека", document.RootElement.GetProperty("note").GetString());
        Assert.False(document.RootElement.TryGetProperty("quantity", out _));
        Assert.False(document.RootElement.TryGetProperty("price", out _));
        Assert.False(document.RootElement.TryGetProperty("keepInStock", out _));

        var loaded = Assert.Single(await repository.GetShoppingItemsAsync());
        Assert.Equal(shopping.Id, loaded.Id);
        Assert.Equal(shopping.ItemId, loaded.ItemId);
        Assert.Equal(shopping.IsRequested, loaded.IsRequested);
        Assert.Equal(shopping.Note, loaded.Note);
    }

    [Fact]
    public async Task GetShoppingItems_TreatsMissingDirectoryAsEmpty()
    {
        Assert.Empty(await CreateRepository().GetShoppingItemsAsync());
        Assert.False(Directory.Exists(Path.Combine(rootPath, "shopping")));
    }

    [Fact]
    public async Task ReadAsync_KeepsShoppingTombstoneInSnapshot()
    {
        var repository = CreateRepository();
        var shopping = CreateShopping(FirstId, true, null);
        await repository.SaveShoppingItemAsync(shopping);
        await repository.SaveShoppingItemAsync(shopping.Delete(Now.AddMinutes(3)));

        var snapshot = await repository.ReadAsync();
        var relativePath = $"shopping/{FirstId}.json";
        Assert.True(snapshot.Files.ContainsKey(relativePath));
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(snapshot.Files[relativePath]));
        Assert.Equal(2, document.RootElement.GetProperty("revision").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("deletedAt").ValueKind);
        Assert.True(File.Exists(Path.Combine(rootPath, "shopping", $"{FirstId}.json")));
    }

    [Fact]
    public async Task ReplaceAsync_AcceptsP5SnapshotWithoutShopping()
    {
        var repository = CreateRepository();

        await repository.ReplaceAsync(Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)),
            ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)),
            ($"problems/{ProblemId}.json", ProblemJson(ProblemId, "Головная боль", FirstId, deleted: false))));

        Assert.Equal("Ибупрофен", Assert.Single(await repository.GetItemsAsync()).Name);
        Assert.Single(await repository.GetPackagesAsync());
        Assert.Equal("Головная боль", Assert.Single(await repository.GetProblemsAsync()).Name);
        Assert.Empty(await repository.GetShoppingItemsAsync());
        Assert.False(Directory.Exists(Path.Combine(rootPath, "shopping")));
    }

    [Fact]
    public async Task ReplaceAsync_RejectsShoppingWhenFileNameIdOrItemIdDiffer()
    {
        var repository = CreateRepository();

        var fileName = await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"shopping/{SecondId}.json", ShoppingJson(FirstId, FirstId, requested: true, deleted: false)))));
        var itemId = await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"shopping/{FirstId}.json", ShoppingJson(FirstId, SecondId, requested: true, deleted: false)))));

        Assert.Contains("покупки", fileName.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(itemId);
        Assert.DoesNotContain("секретная заметка", itemId.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryReplaceAsync_DoesNotLoseItemsPackagesProblemsOrShopping()
    {
        var repository = CreateRepository();
        var original = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Ибупрофен", deleted: false)),
            ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)),
            ($"problems/{ProblemId}.json", ProblemJson(ProblemId, "Головная боль", FirstId, deleted: false)),
            ($"shopping/{FirstId}.json", ShoppingJson(FirstId, FirstId, requested: true, deleted: false)));
        var replacement = Snapshot(
            ("aptechka.json", ManifestJson),
            ($"items/{FirstId}.json", ItemJson(FirstId, "Нурофен", deleted: false)),
            ($"packages/{FirstPackageId}.json", PackageJson(FirstPackageId, FirstId, deleted: false)),
            ($"problems/{ProblemId}.json", ProblemJson(ProblemId, "Головная боль", FirstId, deleted: false)),
            ($"shopping/{FirstId}.json", ShoppingJson(FirstId, FirstId, requested: false, deleted: false)));
        await repository.ReplaceAsync(original);

        Assert.True(await repository.TryReplaceAsync(original, replacement));
        var loaded = await repository.ReadAsync();
        Assert.True(loaded.HasSameFiles(replacement));
        Assert.True(loaded.Files.ContainsKey($"items/{FirstId}.json"));
        Assert.True(loaded.Files.ContainsKey($"packages/{FirstPackageId}.json"));
        Assert.True(loaded.Files.ContainsKey($"problems/{ProblemId}.json"));
        Assert.True(loaded.Files.ContainsKey($"shopping/{FirstId}.json"));
        Assert.False(Assert.Single(await repository.GetShoppingItemsAsync()).IsRequested);
    }

    [Theory]
    [InlineData("mismatched-itemId")]
    [InlineData("untrimmed-note")]
    [InlineData("blank-note")]
    [InlineData("invalid-id")]
    public async Task ReplaceAsync_ClassifiesMalformedShoppingAsInvalidData(string kind)
    {
        var repository = CreateRepository();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"shopping/{FirstId}.json", MalformedShoppingJson(kind)))));

        Assert.IsType<InvalidDataException>(exception);
        Assert.IsNotType<InvalidOperationException>(exception);
        Assert.DoesNotContain("секретная заметка", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-aliases")]
    [InlineData("null-aliases")]
    [InlineData("missing-itemIds")]
    [InlineData("null-itemIds")]
    [InlineData("null-alias")]
    [InlineData("null-itemId")]
    [InlineData("empty-alias")]
    [InlineData("untrimmed-alias")]
    [InlineData("duplicate-aliases")]
    [InlineData("empty-itemId")]
    [InlineData("untrimmed-itemId")]
    [InlineData("duplicate-itemIds")]
    [InlineData("invalid-itemId")]
    [InlineData("untrimmed-name")]
    [InlineData("untrimmed-note")]
    [InlineData("blank-note")]
    public async Task ReplaceAsync_ClassifiesMalformedProblemAsInvalidData(string kind)
    {
        var repository = CreateRepository();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.ReplaceAsync(Snapshot(
                ("aptechka.json", ManifestJson),
                ($"problems/{ProblemId}.json", MalformedProblemJson(kind)))));

        Assert.IsType<InvalidDataException>(exception);
        Assert.IsNotType<InvalidOperationException>(exception);
        Assert.IsNotType<NullReferenceException>(exception.InnerException);
    }

    [Fact]
    public async Task GetProblemsAsync_ClassifiesMalformedProblemAsInvalidData()
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "problems"));
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "problems", $"{ProblemId}.json"),
            MalformedProblemJson("null-aliases"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateRepository().GetProblemsAsync());

        Assert.IsNotType<InvalidOperationException>(exception);
        Assert.IsNotType<NullReferenceException>(exception.InnerException);
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

    private static Problem CreateProblem(string id, string name, IReadOnlyList<string> itemIds, string? note) =>
        Problem.Create(id, Now, name, ["болит голова"], itemIds, note);

    private static ShoppingItem CreateShopping(string itemId, bool isRequested, string? note) =>
        ShoppingItem.Create(itemId, Now, isRequested, note);

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

    private static string ProblemJson(string id, string name, string itemId, bool deleted) =>
        $$"""
        {
          "id": "{{id}}",
          "revision": 1,
          "createdAt": "2026-08-31T18:30:00+00:00",
          "updatedAt": "2026-08-31T18:30:00+00:00",
          "deletedAt": {{(deleted ? "\"2026-08-31T19:00:00+00:00\"" : "null")}},
          "name": "{{name}}",
          "aliases": ["болит голова"],
          "itemIds": ["{{itemId}}"],
          "note": null
        }
        """;

    private static string ShoppingJson(string id, string itemId, bool requested, bool deleted) =>
        $$"""
        {
          "id": "{{id}}",
          "revision": 1,
          "createdAt": "2026-08-31T18:35:00+00:00",
          "updatedAt": "2026-08-31T18:35:00+00:00",
          "deletedAt": {{(deleted ? "\"2026-08-31T19:00:00+00:00\"" : "null")}},
          "itemId": "{{itemId}}",
          "isRequested": {{(requested ? "true" : "false")}},
          "note": null
        }
        """;

    private static string MalformedShoppingJson(string kind) => kind switch
    {
        "mismatched-itemId" => ShoppingJson(FirstId, SecondId, requested: true, deleted: false)
            .Replace("\"note\": null", "\"note\": \"секретная заметка\"", StringComparison.Ordinal),
        "untrimmed-note" => ShoppingJson(FirstId, FirstId, requested: true, deleted: false)
            .Replace("\"note\": null", "\"note\": \" секретная заметка \"", StringComparison.Ordinal),
        "blank-note" => ShoppingJson(FirstId, FirstId, requested: true, deleted: false)
            .Replace("\"note\": null", "\"note\": \"   \"", StringComparison.Ordinal),
        "invalid-id" => ShoppingJson("not-a-ulid", "not-a-ulid", requested: true, deleted: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string MalformedProblemJson(string kind) => kind switch
    {
        "missing-aliases" => ProblemJsonWith(aliases: null, itemIds: $"[\"{FirstId}\"]"),
        "null-aliases" => ProblemJsonWith(aliases: "null", itemIds: $"[\"{FirstId}\"]"),
        "missing-itemIds" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: null),
        "null-itemIds" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: "null"),
        "null-alias" => ProblemJsonWith(aliases: "[null]", itemIds: $"[\"{FirstId}\"]"),
        "null-itemId" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: "[null]"),
        "empty-alias" => ProblemJsonWith(aliases: "[\"\"]", itemIds: $"[\"{FirstId}\"]"),
        "untrimmed-alias" => ProblemJsonWith(aliases: "[\" болит голова \"]", itemIds: $"[\"{FirstId}\"]"),
        "duplicate-aliases" => ProblemJsonWith(aliases: "[\"болит голова\", \"Болит Голова\"]", itemIds: $"[\"{FirstId}\"]"),
        "empty-itemId" => ProblemJsonWith(aliases: "[]", itemIds: "[\"\"]"),
        "untrimmed-itemId" => ProblemJsonWith(aliases: "[]", itemIds: $"[\" {FirstId} \"]"),
        "duplicate-itemIds" => ProblemJsonWith(aliases: "[]", itemIds: $"[\"{FirstId}\", \"{FirstId}\"]"),
        "invalid-itemId" => ProblemJsonWith(aliases: "[]", itemIds: "[\"not-a-ulid\"]"),
        "untrimmed-name" => ProblemJsonWith(aliases: "[]", itemIds: "[]", name: " Головная боль "),
        "untrimmed-note" => ProblemJsonWith(aliases: "[]", itemIds: "[]", note: "\" домашняя \""),
        "blank-note" => ProblemJsonWith(aliases: "[]", itemIds: "[]", note: "\"   \""),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string ProblemJsonWith(
        string? aliases,
        string? itemIds,
        string name = "Головная боль",
        string note = "null")
    {
        var aliasesLine = aliases is null ? null : $"          \"aliases\": {aliases},";
        var itemIdsLine = itemIds is null ? null : $"          \"itemIds\": {itemIds},";
        return $$"""
        {
          "id": "{{ProblemId}}",
          "revision": 1,
          "createdAt": "2026-08-31T18:30:00+00:00",
          "updatedAt": "2026-08-31T18:30:00+00:00",
          "deletedAt": null,
          "name": "{{name}}",
        {{aliasesLine}}
        {{itemIdsLine}}
          "note": {{note}}
        }
        """;
    }

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
