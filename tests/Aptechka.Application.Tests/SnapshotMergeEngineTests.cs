using System.Text;
using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class SnapshotMergeEngineTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string PackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string ProblemId = "01ARZ3NDEKTSV4RRFFQ69G5FB0";
    private const string DatasetId = "01ARZ3NDEKTSV4RRFFQ69G5FAZ";

    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly DateTimeOffset MergedAt = T0.AddHours(3);

    private readonly SnapshotMergeEngine engine = new();

    [Fact]
    public void Merge_CombinesChangesToDifferentFiles()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remoteOther = other.Update(T2, "Вата стерильная", other.Aliases, other.Category, other.ActiveIngredients, other.Form, other.Strength, other.Description, other.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item), File(other)),
            Snap(File(Manifest()), File(localItem), File(other)),
            Snap(File(Manifest()), File(item), File(remoteOther)));

        Assert.False(result.HasConflicts);
        var merged = ReadItem(result, ItemId);
        var mergedOther = ReadItem(result, SecondItemId);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal("Вата стерильная", mergedOther.Name);
        Assert.Equal(2, merged.Revision);
        Assert.Equal(2, mergedOther.Revision);
        Assert.Equal(T1, merged.UpdatedAt);
        Assert.Equal(T2, mergedOther.UpdatedAt);
    }

    [Fact]
    public void Merge_TakesOneSidedChange()
    {
        var item = Item(ItemId, "Ибупрофен");
        var updated = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));

        var localResult = Merge(@base, Snap(File(Manifest()), File(updated)), @base);
        Assert.Equal("Нурофен", ReadItem(localResult, ItemId).Name);
        Assert.Equal(2, ReadItem(localResult, ItemId).Revision);

        var remoteResult = Merge(@base, @base, Snap(File(Manifest()), File(updated)));
        Assert.Equal("Нурофен", ReadItem(remoteResult, ItemId).Name);
    }

    [Fact]
    public void Merge_MergesDifferentScalarsOfOneItem()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remote = item.Update(T2, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(local)),
            Snap(File(Manifest()), File(remote)));

        var merged = ReadItem(result, ItemId);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal("капсулы", merged.Form);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
        Assert.Equal(T0, merged.CreatedAt);
    }

    [Fact]
    public void Merge_AcceptsIdenticalScalarChangeOnBothSides()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remote = item.Update(T2, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(local)),
            Snap(File(Manifest()), File(remote)));

        var merged = ReadItem(result, ItemId);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_ConflictsWhenTheSameScalarChangesDifferently()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remote = item.Update(T2, "Ибуфен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(local)),
            Snap(File(Manifest()), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Null(result.MergedSnapshot);
        Assert.Equal(SyncConflictKind.FieldChangedBoth, conflict.Kind);
        Assert.Equal(ItemPath(ItemId), conflict.Path);
        Assert.Equal("name", conflict.Field);
        Assert.Equal($"{ItemPath(ItemId)}|name|{SyncConflictKind.FieldChangedBoth}", conflict.Key);
        Assert.Equal("\"Ибупрофен\"", conflict.BaseValueJson);
        Assert.Equal("\"Нурофен\"", conflict.LocalValueJson);
        Assert.Equal("\"Ибуфен\"", conflict.RemoteValueJson);
    }

    [Fact]
    public void Merge_UnionsIndependentAliasAdditions()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Update(T1, item.Name, ["Нурофен"], item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remote = item.Update(T2, item.Name, ["Ибуфен"], item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var merged = ReadItem(
            Merge(Snap(File(Manifest()), File(item)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ItemId);

        Assert.Equal(["Нурофен", "Ибуфен"], merged.Aliases);
    }

    [Fact]
    public void Merge_KeepsAliasDeletionFromOneSide()
    {
        var item = Item(ItemId, "Ибупрофен").Update(
            T0,
            "Ибупрофен",
            ["Нурофен", "Ибуфен"],
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "200 мг",
            null,
            true);
        var local = item.Update(T1, item.Name, ["Нурофен"], item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var merged = ReadItem(
            Merge(Snap(File(Manifest()), File(item)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(item))),
            ItemId);

        Assert.Equal(["Нурофен"], merged.Aliases);
        Assert.Equal(3, merged.Revision);
    }

    [Fact]
    public void Merge_DeduplicatesAliasesIgnoringCase()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Update(T1, item.Name, ["Нурофен"], item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remote = item.Update(T2, item.Name, ["нурофен"], item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var merged = ReadItem(
            Merge(Snap(File(Manifest()), File(item)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ItemId);

        Assert.Equal(["Нурофен"], merged.Aliases);
    }

    [Fact]
    public void Merge_MergesDifferentPackageFields()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var local = package.Update(T1, "домашняя", package.ExpirationDate, package.ExpirationPrecision, package.OpenedDate, package.ShelfLifeAfterOpeningDays, package.StockState, package.Note);
        var remote = package.Update(T2, package.Label, package.ExpirationDate, package.ExpirationPrecision, package.OpenedDate, package.ShelfLifeAfterOpeningDays, StockState.Low, package.Note);

        var merged = ReadPackage(
            Merge(
                Snap(File(Manifest()), File(item), File(package)),
                Snap(File(Manifest()), File(item), File(local)),
                Snap(File(Manifest()), File(item), File(remote))),
            PackageId);

        Assert.Equal("домашняя", merged.Label);
        Assert.Equal(StockState.Low, merged.StockState);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_TreatsExpirationDateAndPrecisionAsOneField()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var local = package.Update(
            T1,
            package.Label,
            new DateOnly(2027, 5, 31),
            ExpirationPrecision.Month,
            package.OpenedDate,
            package.ShelfLifeAfterOpeningDays,
            package.StockState,
            package.Note);
        var remote = package.Update(
            T2,
            package.Label,
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Day,
            package.OpenedDate,
            package.ShelfLifeAfterOpeningDays,
            package.StockState,
            package.Note);

        var result = Merge(
            Snap(File(Manifest()), File(item), File(package)),
            Snap(File(Manifest()), File(item), File(local)),
            Snap(File(Manifest()), File(item), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("expiration", conflict.Field);
        Assert.Equal(SyncConflictKind.FieldChangedBoth, conflict.Kind);
        Assert.Contains("expirationDate", conflict.LocalValueJson, StringComparison.Ordinal);
        Assert.Contains("expirationPrecision", conflict.RemoteValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_AcceptsTombstoneWhenTheOtherSideEqualsBase()
    {
        var item = Item(ItemId, "Ибупрофен");
        var tombstone = item.Delete(T1);
        var remote = item with { Revision = 4, UpdatedAt = T2 };

        var merged = ReadItem(
            Merge(Snap(File(Manifest()), File(item)), Snap(File(Manifest()), File(tombstone)), Snap(File(Manifest()), File(remote))),
            ItemId);

        Assert.Equal(T1, merged.DeletedAt);
        Assert.Equal("Ибупрофен", merged.Name);
        Assert.Equal(2, merged.Revision);
    }

    [Fact]
    public void Merge_ConflictsWhenTombstoneCompetesWithAUserChange()
    {
        var item = Item(ItemId, "Ибупрофен");
        var tombstone = item.Delete(T1);
        var remote = item.Update(T2, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(tombstone)),
            Snap(File(Manifest()), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.DeleteVsModify, conflict.Kind);
        Assert.Equal("deletedAt", conflict.Field);
        Assert.Null(result.MergedSnapshot);
    }

    [Fact]
    public void Merge_MergesTombstonesFromBothSides()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = item.Delete(T1);
        var remote = item.Delete(T2);

        var merged = ReadItem(
            Merge(Snap(File(Manifest()), File(item)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ItemId);

        Assert.Equal(T1, merged.DeletedAt);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_ConflictsWhenRestoreCompetesWithAChange()
    {
        var tombstone = Item(ItemId, "Ибупрофен").Delete(T1);
        var restored = tombstone with { DeletedAt = null, Revision = 3, UpdatedAt = T2 };
        restored.EnsureValid();
        var remote = tombstone.Update(
            T2,
            "Нурофен",
            tombstone.Aliases,
            tombstone.Category,
            tombstone.ActiveIngredients,
            tombstone.Form,
            tombstone.Strength,
            tombstone.Description,
            tombstone.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(tombstone)),
            Snap(File(Manifest()), File(restored)),
            Snap(File(Manifest()), File(remote)));

        Assert.Equal(SyncConflictKind.DeleteVsModify, Assert.Single(result.Conflicts).Kind);
    }

    [Fact]
    public void Merge_AcceptsNewFileOnOneSide()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var @base = Snap(File(Manifest()), File(item));

        var merged = ReadPackage(
            Merge(@base, Snap(File(Manifest()), File(item), File(package)), @base),
            PackageId);

        Assert.Equal(PackageId, merged.Id);
        Assert.Equal(1, merged.Revision);
    }

    [Fact]
    public void Merge_AcceptsIdenticalNewFileOnBothSides()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var @base = Snap(File(Manifest()), File(item));
        var added = Snap(File(Manifest()), File(item), File(package));

        var merged = ReadPackage(Merge(@base, added, added), PackageId);
        Assert.Equal(PackageId, merged.Id);
        Assert.Null(merged.Label);
    }

    [Fact]
    public void Merge_ConflictsWhenNewFilesDiffer()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = Package(PackageId, ItemId).Update(T1, "дома", new DateOnly(2027, 4, 30), ExpirationPrecision.Month, null, null, StockState.Available, null);
        var remote = Package(PackageId, ItemId).Update(T1, "дача", new DateOnly(2027, 4, 30), ExpirationPrecision.Month, null, null, StockState.Available, null);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(item), File(local)),
            Snap(File(Manifest()), File(item), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FileChangedBoth, conflict.Kind);
        Assert.Equal(PackagePath(PackageId), conflict.Path);
        Assert.Equal("", conflict.Field);
    }

    [Fact]
    public void Merge_ConflictsWhenPhysicalDeleteCompetesWithAChange()
    {
        var item = Item(ItemId, "Ибупрофен");
        var updated = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest())),
            Snap(File(Manifest()), File(updated)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FileDeleteVsModify, conflict.Kind);
        Assert.Equal(ItemPath(ItemId), conflict.Path);
        Assert.Null(conflict.LocalValueJson);
        Assert.NotNull(conflict.RemoteValueJson);
    }

    [Fact]
    public void Merge_ThrowsOnMalformedJson()
    {
        var item = Item(ItemId, "Ибупрофен");
        var path = ItemPath(ItemId);
        var malformed = Snap(
            File(Manifest()),
            (path, Encoding.UTF8.GetBytes("{ not-json")));

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(Snap(File(Manifest()), File(item)), malformed, Snap(File(Manifest()), File(item))));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ибупрофен", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenJsonIdDoesNotMatchPath()
    {
        var item = Item(ItemId, "Ибупрофен");
        var mismatch = Bytes(item with { Id = SecondItemId });

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item)),
                Snap(File(Manifest()), (ItemPath(ItemId), mismatch)),
                Snap(File(Manifest()), File(item))));

        Assert.Contains(ItemPath(ItemId), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ибупрофен", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_IgnoresJsonPropertyReorder()
    {
        var item = Item(ItemId, "Ибупрофен");
        var reordered = Encoding.UTF8.GetBytes(
            """
            {
              "keepInStock": true,
              "name": "Ибупрофен",
              "coverPhotoId": null,
              "id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
              "aliases": [],
              "category": "medicine",
              "activeIngredients": ["ибупрофен"],
              "form": "таблетки",
              "strength": "200 мг",
              "description": null,
              "revision": 1,
              "createdAt": "2026-08-31T08:00:00+00:00",
              "updatedAt": "2026-08-31T08:00:00+00:00",
              "deletedAt": null
            }
            """);
        var remote = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var merged = ReadItem(
            Merge(
                Snap(File(Manifest()), File(item)),
                Snap(File(Manifest()), (ItemPath(ItemId), reordered)),
                Snap(File(Manifest()), File(remote))),
            ItemId);

        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal(2, merged.Revision);
    }

    [Fact]
    public void Merge_ReturnsConflictsInStableOrder()
    {
        var first = Item(ItemId, "Ибупрофен");
        var second = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var localFirst = first.Update(T1, "Нурофен", first.Aliases, first.Category, first.ActiveIngredients, first.Form, first.Strength, "локально", first.KeepInStock);
        var remoteFirst = first.Update(T1, "Ибуфен", first.Aliases, first.Category, first.ActiveIngredients, first.Form, first.Strength, "удалённо", first.KeepInStock);
        var localSecond = second.Update(T1, "Марля", second.Aliases, second.Category, second.ActiveIngredients, second.Form, second.Strength, second.Description, second.KeepInStock);
        var remoteSecond = second.Update(T1, "Бинт", second.Aliases, second.Category, second.ActiveIngredients, second.Form, second.Strength, second.Description, second.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(first), File(second)),
            Snap(File(Manifest()), File(localFirst), File(localSecond)),
            Snap(File(Manifest()), File(remoteFirst), File(remoteSecond)));

        Assert.Equal(
            [
                $"{ItemPath(ItemId)}|description|{SyncConflictKind.FieldChangedBoth}",
                $"{ItemPath(ItemId)}|name|{SyncConflictKind.FieldChangedBoth}",
                $"{ItemPath(SecondItemId)}|name|{SyncConflictKind.FieldChangedBoth}",
            ],
            result.Conflicts.Select(static conflict => conflict.Key).ToArray());
    }

    [Fact]
    public void Merge_DoesNotMutateInputSnapshotsOrBytes()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, item.Name, item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var baseBytes = Bytes(item);
        var localBytes = Bytes(localItem);
        var remoteBytes = Bytes(remoteItem);
        var baseCopy = baseBytes.ToArray();
        var localCopy = localBytes.ToArray();
        var remoteCopy = remoteBytes.ToArray();
        var @base = new DataSnapshot(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = Bytes(Manifest()),
            [ItemPath(ItemId)] = baseBytes,
        });
        var local = new DataSnapshot(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = Bytes(Manifest()),
            [ItemPath(ItemId)] = localBytes,
        });
        var remote = new DataSnapshot(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["aptechka.json"] = Bytes(Manifest()),
            [ItemPath(ItemId)] = remoteBytes,
        });

        var result = engine.Merge(@base, local, remote, MergedAt);

        Assert.False(result.HasConflicts);
        Assert.True(baseBytes.AsSpan().SequenceEqual(baseCopy));
        Assert.True(localBytes.AsSpan().SequenceEqual(localCopy));
        Assert.True(remoteBytes.AsSpan().SequenceEqual(remoteCopy));
        Assert.Equal(2, @base.Files.Count);
        Assert.Same(baseBytes, @base.Files[ItemPath(ItemId)]);
        result.MergedSnapshot!.Files[ItemPath(ItemId)][0] ^= 0xFF;
        Assert.True(localBytes.AsSpan().SequenceEqual(localCopy));
        Assert.True(remoteBytes.AsSpan().SequenceEqual(remoteCopy));
    }

    [Fact]
    public void Merge_DoesNotUseLastWriteWinsOnConflictingNames()
    {
        var item = Item(ItemId, "Ибупрофен");
        var earlier = item.Update(T2, "Позже локально", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var later = item.Update(T1, "Раньше удалённо", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(earlier)),
            Snap(File(Manifest()), File(later)));

        Assert.True(result.HasConflicts);
        Assert.Null(result.MergedSnapshot);
    }

    [Fact]
    public void Merge_ThrowsWhenLocalChangesExistingPackageItemId()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var package = Package(PackageId, ItemId);
        var local = package with { ItemId = SecondItemId, Revision = 2, UpdatedAt = T1 };
        local.EnsureValid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item), File(other), File(package)),
                Snap(File(Manifest()), File(item), File(other), File(local)),
                Snap(File(Manifest()), File(item), File(other), File(package))));

        Assert.Contains(PackagePath(PackageId), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondItemId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenRemoteChangesExistingPackageItemId()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var package = Package(PackageId, ItemId);
        var remote = package with { ItemId = SecondItemId, Revision = 2, UpdatedAt = T1 };
        remote.EnsureValid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item), File(other), File(package)),
                Snap(File(Manifest()), File(item), File(other), File(package)),
                Snap(File(Manifest()), File(item), File(other), File(remote))));

        Assert.Contains(PackagePath(PackageId), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenBothSidesChangeExistingPackageItemIdIdentically()
    {
        var item = Item(ItemId, "Ибупрофен");
        var other = Item(SecondItemId, "Вата", InventoryItemCategory.MedicalSupply);
        var package = Package(PackageId, ItemId);
        var changed = package with { ItemId = SecondItemId, Revision = 2, UpdatedAt = T1 };
        changed.EnsureValid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item), File(other), File(package)),
                Snap(File(Manifest()), File(item), File(other), File(changed)),
                Snap(File(Manifest()), File(item), File(other), File(changed))));

        Assert.Contains(PackagePath(PackageId), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenLocalDeletesManifest()
    {
        var item = Item(ItemId, "Ибупрофен");
        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item)),
                Snap(File(item)),
                Snap(File(Manifest()), File(item))));

        Assert.Contains("aptechka.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenRemoteDeletesManifest()
    {
        var item = Item(ItemId, "Ибупрофен");
        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item)),
                Snap(File(Manifest()), File(item)),
                Snap(File(item))));

        Assert.Contains("aptechka.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenBothSidesDeleteManifest()
    {
        var item = Item(ItemId, "Ибупрофен");
        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(item)),
                Snap(File(item)),
                Snap(File(item))));

        Assert.Contains("aptechka.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ConflictsWhenLocalChangesDatasetId()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localManifest = new DatasetManifest(DatasetManifest.CurrentSchemaVersion, SecondItemId, T0);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(localManifest), File(item)),
            Snap(File(Manifest()), File(item)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FileChangedBoth, conflict.Kind);
        Assert.Equal("aptechka.json", conflict.Path);
        Assert.Null(result.MergedSnapshot);
    }

    [Fact]
    public void Merge_ConflictsWhenRemoteChangesDatasetId()
    {
        var item = Item(ItemId, "Ибупрофен");
        var remoteManifest = new DatasetManifest(DatasetManifest.CurrentSchemaVersion, SecondItemId, T0);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(item)),
            Snap(File(remoteManifest), File(item)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FileChangedBoth, conflict.Kind);
        Assert.Equal("aptechka.json", conflict.Path);
    }

    [Fact]
    public void Merge_ConflictsWhenBothSidesChangeDatasetIdIdentically()
    {
        var item = Item(ItemId, "Ибупрофен");
        var changed = new DatasetManifest(DatasetManifest.CurrentSchemaVersion, SecondItemId, T0);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(changed), File(item)),
            Snap(File(changed), File(item)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.FileChangedBoth, conflict.Kind);
        Assert.Equal("aptechka.json", conflict.Path);
        Assert.Null(result.MergedSnapshot);
    }

    [Fact]
    public void Merge_AcceptsUnchangedIdenticalManifest()
    {
        var item = Item(ItemId, "Ибупрофен");
        var updated = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);

        var result = Merge(
            Snap(File(Manifest()), File(item)),
            Snap(File(Manifest()), File(updated)),
            Snap(File(Manifest()), File(item)));

        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        Assert.True(result.MergedSnapshot.Files.ContainsKey("aptechka.json"));
        var manifest = JsonSerializer.Deserialize<DatasetManifest>(
            result.MergedSnapshot.Files["aptechka.json"],
            AptechkaJson.Options)!;
        Assert.Equal(DatasetId, manifest.DatasetId);
    }

    [Fact]
    public void Merge_MergesIndependentProblemScalars()
    {
        var item = Item(ItemId, "Ибупрофен");
        var problem = Problem(ProblemId, "Головная боль");
        var local = problem.Update(T1, "Мигрень", problem.Aliases, problem.ItemIds, problem.Note);
        var remote = problem.Update(T2, problem.Name, problem.Aliases, problem.ItemIds, "дома");

        var merged = ReadProblem(
            Merge(
                Snap(File(Manifest()), File(item), File(problem)),
                Snap(File(Manifest()), File(item), File(local)),
                Snap(File(Manifest()), File(item), File(remote))),
            ProblemId);

        Assert.Equal("Мигрень", merged.Name);
        Assert.Equal("дома", merged.Note);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
        Assert.Equal(T0, merged.CreatedAt);
    }

    [Fact]
    public void Merge_ConflictsWhenTheSameProblemScalarChangesDifferently()
    {
        var item = Item(ItemId, "Ибупрофен");
        var problem = Problem(ProblemId, "Головная боль");
        var local = problem.Update(T1, "Мигрень", problem.Aliases, problem.ItemIds, problem.Note);
        var remote = problem.Update(T2, "Давление", problem.Aliases, problem.ItemIds, problem.Note);

        var result = Merge(
            Snap(File(Manifest()), File(item), File(problem)),
            Snap(File(Manifest()), File(item), File(local)),
            Snap(File(Manifest()), File(item), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Null(result.MergedSnapshot);
        Assert.Equal(SyncConflictKind.FieldChangedBoth, conflict.Kind);
        Assert.Equal("name", conflict.Field);
        Assert.Equal(ProblemPath(ProblemId), conflict.Path);
        Assert.Equal("\"Головная боль\"", conflict.BaseValueJson);
        Assert.Equal("\"Мигрень\"", conflict.LocalValueJson);
        Assert.Equal("\"Давление\"", conflict.RemoteValueJson);
    }

    [Fact]
    public void Merge_UnionsIndependentProblemAliasAdditions()
    {
        var problem = Problem(ProblemId, "Головная боль");
        var local = problem.Update(T1, problem.Name, ["болит голова"], problem.ItemIds, problem.Note);
        var remote = problem.Update(T2, problem.Name, ["мигрень"], problem.ItemIds, problem.Note);

        var merged = ReadProblem(
            Merge(Snap(File(Manifest()), File(problem)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ProblemId);

        Assert.Equal(["болит голова", "мигрень"], merged.Aliases);
    }

    [Fact]
    public void Merge_MergesIndependentProblemItemIdAdditionsAndRemovals()
    {
        var problem = Problem(ProblemId, "Головная боль").Update(T0, "Головная боль", [], [ItemId, SecondItemId], null);
        var local = problem.Update(T1, problem.Name, problem.Aliases, [ItemId, PackageId], problem.Note);
        var remote = problem.Update(T2, problem.Name, problem.Aliases, [SecondItemId], problem.Note);

        var merged = ReadProblem(
            Merge(Snap(File(Manifest()), File(problem)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ProblemId);

        Assert.Equal([PackageId], merged.ItemIds);
    }

    [Fact]
    public void Merge_ConflictsWhenProblemTombstoneCompetesWithAUserChange()
    {
        var problem = Problem(ProblemId, "Головная боль");
        var tombstone = problem.Delete(T1);
        var remote = problem.Update(T2, "Мигрень", problem.Aliases, problem.ItemIds, problem.Note);

        var result = Merge(
            Snap(File(Manifest()), File(problem)),
            Snap(File(Manifest()), File(tombstone)),
            Snap(File(Manifest()), File(remote)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(SyncConflictKind.DeleteVsModify, conflict.Kind);
        Assert.Equal("deletedAt", conflict.Field);
    }

    [Fact]
    public void Merge_MergesProblemTombstonesFromBothSides()
    {
        var problem = Problem(ProblemId, "Головная боль");
        var local = problem.Delete(T1);
        var remote = problem.Delete(T2);

        var merged = ReadProblem(
            Merge(Snap(File(Manifest()), File(problem)), Snap(File(Manifest()), File(local)), Snap(File(Manifest()), File(remote))),
            ProblemId);

        Assert.Equal(T1, merged.DeletedAt);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_ThrowsWhenProblemJsonIdDoesNotMatchPath()
    {
        var problem = Problem(ProblemId, "Головная боль");
        var mismatch = Bytes(problem with { Id = ItemId });

        var exception = Assert.Throws<InvalidDataException>(() =>
            Merge(
                Snap(File(Manifest()), File(problem)),
                Snap(File(Manifest()), (ProblemPath(ProblemId), mismatch)),
                Snap(File(Manifest()), File(problem))));

        Assert.Contains(ProblemPath(ProblemId), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Головная боль", exception.Message, StringComparison.Ordinal);
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
    public void Merge_ClassifiesMalformedProblemAsInvalidData(string kind)
    {
        var problem = Problem(ProblemId, "Головная боль");
        var damaged = (ProblemPath(ProblemId), Encoding.UTF8.GetBytes(MalformedProblemJson(kind)));
        var valid = Snap(File(Manifest()), File(problem));

        var local = Assert.Throws<InvalidDataException>(() =>
            Merge(valid, Snap(File(Manifest()), damaged), valid));
        var remote = Assert.Throws<InvalidDataException>(() =>
            Merge(valid, valid, Snap(File(Manifest()), damaged)));
        var @base = Assert.Throws<InvalidDataException>(() =>
            Merge(Snap(File(Manifest()), damaged), valid, valid));

        Assert.IsNotType<NullReferenceException>(local.InnerException);
        Assert.IsNotType<NullReferenceException>(remote.InnerException);
        Assert.IsNotType<NullReferenceException>(@base.InnerException);
        Assert.DoesNotContain("Головная боль", local.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_KeepsItemsPackagesAndProblemsTogether()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var problem = Problem(ProblemId, "Головная боль");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, item.Form, item.Strength, item.Description, item.KeepInStock);
        var remotePackage = package.Update(T2, "блистер", package.ExpirationDate, package.ExpirationPrecision, package.OpenedDate, package.ShelfLifeAfterOpeningDays, package.StockState, package.Note);
        var remoteProblem = problem.Update(T2, problem.Name, ["болит голова"], [ItemId], problem.Note);

        var merged = Merge(
            Snap(File(Manifest()), File(item), File(package), File(problem)),
            Snap(File(Manifest()), File(localItem), File(package), File(problem)),
            Snap(File(Manifest()), File(item), File(remotePackage), File(remoteProblem)));

        Assert.False(merged.HasConflicts);
        Assert.Equal("Нурофен", ReadItem(merged, ItemId).Name);
        Assert.Equal("блистер", ReadPackage(merged, PackageId).Label);
        Assert.Equal(["болит голова"], ReadProblem(merged, ProblemId).Aliases);
        Assert.True(merged.MergedSnapshot!.Files.ContainsKey(ItemPath(ItemId)));
        Assert.True(merged.MergedSnapshot.Files.ContainsKey(PackagePath(PackageId)));
        Assert.True(merged.MergedSnapshot.Files.ContainsKey(ProblemPath(ProblemId)));
    }

    private SnapshotMergeResult Merge(DataSnapshot @base, DataSnapshot local, DataSnapshot remote) =>
        engine.Merge(@base, local, remote, MergedAt);

    private static InventoryItem ReadItem(SnapshotMergeResult result, string id)
    {
        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        return JsonSerializer.Deserialize<InventoryItem>(
            result.MergedSnapshot.Files[ItemPath(id)],
            AptechkaJson.Options)!;
    }

    private static Package ReadPackage(SnapshotMergeResult result, string id)
    {
        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        return JsonSerializer.Deserialize<Package>(
            result.MergedSnapshot.Files[PackagePath(id)],
            AptechkaJson.Options)!;
    }

    private static Problem ReadProblem(SnapshotMergeResult result, string id)
    {
        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        return JsonSerializer.Deserialize<Problem>(
            result.MergedSnapshot.Files[ProblemPath(id)],
            AptechkaJson.Options)!;
    }

    private static InventoryItem Item(
        string id,
        string name,
        InventoryItemCategory category = InventoryItemCategory.Medicine) =>
        InventoryItem.Create(
            id,
            T0,
            name,
            [],
            category,
            category == InventoryItemCategory.Medicine ? ["ибупрофен"] : [],
            category == InventoryItemCategory.Medicine ? "таблетки" : null,
            category == InventoryItemCategory.Medicine ? "200 мг" : null,
            null,
            category == InventoryItemCategory.Medicine);

    private static Package Package(string id, string itemId) =>
        Domain.Inventory.Package.Create(
            id,
            T0,
            itemId,
            null,
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Month,
            null,
            null,
            StockState.Available,
            null);

    private static Problem Problem(string id, string name) =>
        Domain.Inventory.Problem.Create(id, T0, name, [], [], null);

    private static DatasetManifest Manifest() =>
        new(DatasetManifest.CurrentSchemaVersion, DatasetId, T0);

    private static DataSnapshot Snap(params (string Path, byte[] Content)[] files) =>
        new(files.ToDictionary(static file => file.Path, static file => file.Content, StringComparer.Ordinal));

    private static (string Path, byte[] Content) File(InventoryItem item) => (ItemPath(item.Id), Bytes(item));

    private static (string Path, byte[] Content) File(Package package) => (PackagePath(package.Id), Bytes(package));

    private static (string Path, byte[] Content) File(Problem problem) => (ProblemPath(problem.Id), Bytes(problem));

    private static (string Path, byte[] Content) File(DatasetManifest manifest) => ("aptechka.json", Bytes(manifest));

    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, AptechkaJson.Options);

    private static string ItemPath(string id) => $"items/{id}.json";

    private static string PackagePath(string id) => $"packages/{id}.json";

    private static string ProblemPath(string id) => $"problems/{id}.json";

    private static string MalformedProblemJson(string kind) => kind switch
    {
        "missing-aliases" => ProblemJsonWith(aliases: null, itemIds: $"[\"{ItemId}\"]"),
        "null-aliases" => ProblemJsonWith(aliases: "null", itemIds: $"[\"{ItemId}\"]"),
        "missing-itemIds" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: null),
        "null-itemIds" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: "null"),
        "null-alias" => ProblemJsonWith(aliases: "[null]", itemIds: $"[\"{ItemId}\"]"),
        "null-itemId" => ProblemJsonWith(aliases: "[\"болит голова\"]", itemIds: "[null]"),
        "empty-alias" => ProblemJsonWith(aliases: "[\"\"]", itemIds: $"[\"{ItemId}\"]"),
        "untrimmed-alias" => ProblemJsonWith(aliases: "[\" болит голова \"]", itemIds: $"[\"{ItemId}\"]"),
        "duplicate-aliases" => ProblemJsonWith(aliases: "[\"болит голова\", \"Болит Голова\"]", itemIds: $"[\"{ItemId}\"]"),
        "empty-itemId" => ProblemJsonWith(aliases: "[]", itemIds: "[\"\"]"),
        "untrimmed-itemId" => ProblemJsonWith(aliases: "[]", itemIds: $"[\" {ItemId} \"]"),
        "duplicate-itemIds" => ProblemJsonWith(aliases: "[]", itemIds: $"[\"{ItemId}\", \"{ItemId}\"]"),
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
          "createdAt": "2026-08-31T08:00:00+00:00",
          "updatedAt": "2026-08-31T08:00:00+00:00",
          "deletedAt": null,
          "name": "{{name}}",
        {{aliasesLine}}
        {{itemIdsLine}}
          "note": {{note}}
        }
        """;
    }
}
