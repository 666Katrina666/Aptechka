using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;

namespace Aptechka.Application.Tests;

public sealed class SnapshotMergeResolutionTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SecondItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private const string PackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string DatasetId = "01ARZ3NDEKTSV4RRFFQ69G5FAZ";

    private static readonly DateTimeOffset T0 = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly DateTimeOffset MergedAt = T0.AddHours(3);

    private readonly SnapshotMergeEngine engine = new();

    [Fact]
    public void Merge_ChoosesLocalScalar()
    {
        var (conflict, @base, local, remote) = NameConflict();

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local));
        var merged = ReadItem(result, ItemId);

        Assert.False(result.HasConflicts);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_ChoosesRemoteScalar()
    {
        var (conflict, @base, local, remote) = NameConflict();

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Remote));

        Assert.Equal("Ибуфен", ReadItem(result, ItemId).Name);
    }

    [Fact]
    public void Merge_ProducesSnapshotWhenEveryConflictIsResolved()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, "Ибуфен", item.Aliases, item.Category, item.ActiveIngredients, "сироп", item.Strength, item.Description, item.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        var conflicts = Merge(@base, local, remote).Conflicts;
        Assert.Equal(2, conflicts.Count);

        var result = Merge(
            @base,
            local,
            remote,
            Resolve(conflicts.Single(static conflict => conflict.Field == "name"), SyncConflictSide.Local),
            Resolve(conflicts.Single(static conflict => conflict.Field == "form"), SyncConflictSide.Remote));
        var merged = ReadItem(result, ItemId);

        Assert.False(result.HasConflicts);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal("сироп", merged.Form);
        Assert.Equal(3, merged.Revision);
    }

    [Fact]
    public void Merge_ReturnsRemainingConflictsWhenResolutionIsPartial()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = item.Update(T1, "Нурофен", item.Aliases, item.Category, item.ActiveIngredients, "капсулы", item.Strength, item.Description, item.KeepInStock);
        var remoteItem = item.Update(T2, "Ибуфен", item.Aliases, item.Category, item.ActiveIngredients, "сироп", item.Strength, item.Description, item.KeepInStock);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        var nameConflict = Merge(@base, local, remote).Conflicts.Single(static conflict => conflict.Field == "name");

        var result = Merge(@base, local, remote, Resolve(nameConflict, SyncConflictSide.Local));

        Assert.Null(result.MergedSnapshot);
        var remaining = Assert.Single(result.Conflicts);
        Assert.Equal("form", remaining.Field);
    }

    [Fact]
    public void Merge_DoesNotApplyStaleLocalValue()
    {
        var (conflict, @base, local, remote) = NameConflict();
        var stale = conflict with { LocalValueJson = "\"другое\"" };

        var result = Merge(@base, local, remote, Resolve(stale, SyncConflictSide.Local));

        Assert.Null(result.MergedSnapshot);
        Assert.Equal(conflict.Key, Assert.Single(result.Conflicts).Key);
    }

    [Fact]
    public void Merge_DoesNotApplyStaleRemoteValue()
    {
        var (conflict, @base, local, remote) = NameConflict();
        var stale = conflict with { RemoteValueJson = "\"другое\"" };

        var result = Merge(@base, local, remote, Resolve(stale, SyncConflictSide.Remote));

        Assert.Null(result.MergedSnapshot);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public void Merge_DoesNotApplyStaleBaseValue()
    {
        var (conflict, @base, local, remote) = NameConflict();
        var stale = conflict with { BaseValueJson = "\"другое\"" };

        var result = Merge(@base, local, remote, Resolve(stale, SyncConflictSide.Local));

        Assert.Null(result.MergedSnapshot);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public void Merge_DeleteVsModifyChoosesTombstone()
    {
        var item = Item(ItemId, "Ибупрофен");
        var deleted = item.Delete(T1);
        var changed = Rename(item, "Нурофен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(deleted));
        var remote = Snap(File(Manifest()), File(changed));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);
        Assert.Equal("deletedAt", conflict.Field);
        Assert.Equal(SyncConflictKind.DeleteVsModify, conflict.Kind);
        Assert.Contains("\"name\":\"Ибупрофен\"", conflict.BaseValueJson, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Ибупрофен\"", conflict.LocalValueJson, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Нурофен\"", conflict.RemoteValueJson, StringComparison.Ordinal);

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local));
        var merged = ReadItem(result, ItemId);

        Assert.NotNull(merged.DeletedAt);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_DeleteVsModifyChoosesModifiedEntity()
    {
        var item = Item(ItemId, "Ибупрофен");
        var deleted = item.Delete(T1);
        var changed = Rename(item, "Нурофен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(deleted));
        var remote = Snap(File(Manifest()), File(changed));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Remote));
        var merged = ReadItem(result, ItemId);

        Assert.Null(merged.DeletedAt);
        Assert.Equal("Нурофен", merged.Name);
        Assert.Equal(3, merged.Revision);
        Assert.Equal(MergedAt, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_DoesNotApplyStaleDeleteVsModifyWhenRemoteNameChanges()
    {
        var item = Item(ItemId, "Ибупрофен");
        var tombstone = item.Delete(T1);
        var remoteB = Rename(item, "B", T2);
        var remoteC = Rename(item, "C", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(tombstone));
        var shown = Snap(File(Manifest()), File(remoteB));
        var current = Snap(File(Manifest()), File(remoteC));
        var shownConflict = Assert.Single(Merge(@base, local, shown).Conflicts);

        var result = Merge(@base, local, current, Resolve(shownConflict, SyncConflictSide.Remote));
        var returned = Assert.Single(result.Conflicts);

        Assert.Null(result.MergedSnapshot);
        Assert.Equal(shownConflict.Key, returned.Key);
        Assert.Equal("deletedAt", returned.Field);
        Assert.NotEqual(shownConflict.RemoteValueJson, returned.RemoteValueJson);
        Assert.Contains("\"name\":\"C\"", returned.RemoteValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"C\"", shownConflict.RemoteValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_DoesNotApplyStaleDeleteVsModifyWhenTombstoneFieldChanges()
    {
        var item = Item(ItemId, "Ибупрофен");
        var tombstone = item.Delete(T1);
        var renamedTombstone = Rename(tombstone, "Парацетамол", T1.AddMinutes(1));
        var remote = Rename(item, "Нурофен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var shown = Snap(File(Manifest()), File(tombstone));
        var current = Snap(File(Manifest()), File(renamedTombstone));
        var remoteSnap = Snap(File(Manifest()), File(remote));
        var shownConflict = Assert.Single(Merge(@base, shown, remoteSnap).Conflicts);
        Assert.Equal(tombstone.DeletedAt, renamedTombstone.DeletedAt);

        var result = Merge(@base, current, remoteSnap, Resolve(shownConflict, SyncConflictSide.Local));
        var returned = Assert.Single(result.Conflicts);

        Assert.Null(result.MergedSnapshot);
        Assert.NotEqual(shownConflict.LocalValueJson, returned.LocalValueJson);
        Assert.Contains("\"name\":\"Парацетамол\"", returned.LocalValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_DoesNotApplyStalePackageDeleteVsModifyWhenNoteChanges()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var tombstone = package.Delete(T1);
        var remoteNoteB = WithNote(package, "B", T2);
        var remoteNoteC = WithNote(package, "C", T2);
        var @base = Snap(File(Manifest()), File(item), File(package));
        var local = Snap(File(Manifest()), File(item), File(tombstone));
        var shown = Snap(File(Manifest()), File(item), File(remoteNoteB));
        var current = Snap(File(Manifest()), File(item), File(remoteNoteC));
        var shownConflict = Assert.Single(Merge(@base, local, shown).Conflicts);
        Assert.Equal(SyncConflictKind.DeleteVsModify, shownConflict.Kind);
        Assert.Null(remoteNoteB.DeletedAt);
        Assert.Null(remoteNoteC.DeletedAt);

        var result = Merge(@base, local, current, Resolve(shownConflict, SyncConflictSide.Remote));
        var returned = Assert.Single(result.Conflicts);

        Assert.Null(result.MergedSnapshot);
        Assert.NotEqual(shownConflict.RemoteValueJson, returned.RemoteValueJson);
        Assert.Contains("\"note\":\"C\"", returned.RemoteValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_DoesNotApplyStalePackageDeleteVsModifyWhenStockStateChanges()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var tombstone = package.Delete(T1);
        var remoteLow = WithStock(package, StockState.Low, T2);
        var remoteDepleted = WithStock(package, StockState.Depleted, T2);
        var @base = Snap(File(Manifest()), File(item), File(package));
        var local = Snap(File(Manifest()), File(item), File(tombstone));
        var shown = Snap(File(Manifest()), File(item), File(remoteLow));
        var current = Snap(File(Manifest()), File(item), File(remoteDepleted));
        var shownConflict = Assert.Single(Merge(@base, local, shown).Conflicts);

        var result = Merge(@base, local, current, Resolve(shownConflict, SyncConflictSide.Remote));
        var returned = Assert.Single(result.Conflicts);

        Assert.Null(result.MergedSnapshot);
        Assert.NotEqual(shownConflict.RemoteValueJson, returned.RemoteValueJson);
        Assert.Contains("\"stockState\":\"depleted\"", returned.RemoteValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_PackageDeleteVsModifyResolvesWhenEntitiesAreUnchanged()
    {
        var item = Item(ItemId, "Ибупрофен");
        var package = Package(PackageId, ItemId);
        var tombstone = package.Delete(T1);
        var changed = WithNote(package, "дома", T2);
        var @base = Snap(File(Manifest()), File(item), File(package));
        var local = Snap(File(Manifest()), File(item), File(tombstone));
        var remote = Snap(File(Manifest()), File(item), File(changed));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var choseRemote = ReadPackage(
            Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Remote)),
            PackageId);
        var choseLocal = ReadPackage(
            Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local)),
            PackageId);

        Assert.Equal("дома", choseRemote.Note);
        Assert.Null(choseRemote.DeletedAt);
        Assert.Equal(3, choseRemote.Revision);
        Assert.Equal(MergedAt, choseRemote.UpdatedAt);
        Assert.NotNull(choseLocal.DeletedAt);
        Assert.Equal(3, choseLocal.Revision);
    }

    [Fact]
    public void Merge_FileChangedBothChoosesEachSide()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localPackage = Package(PackageId, ItemId).Update(T1, "дома", new DateOnly(2027, 4, 30), ExpirationPrecision.Month, null, null, StockState.Available, null);
        var remotePackage = Package(PackageId, ItemId).Update(T1, "дача", new DateOnly(2027, 4, 30), ExpirationPrecision.Month, null, null, StockState.Available, null);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(item), File(localPackage));
        var remote = Snap(File(Manifest()), File(item), File(remotePackage));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var choseLocal = ReadPackage(
            Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local)),
            PackageId);
        var choseRemote = ReadPackage(
            Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Remote)),
            PackageId);

        Assert.Equal("дома", choseLocal.Label);
        Assert.Equal("дача", choseRemote.Label);
    }

    [Fact]
    public void Merge_FileDeleteVsModifyChoosesDeletion()
    {
        var item = Item(ItemId, "Ибупрофен");
        var updated = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()));
        var remote = Snap(File(Manifest()), File(updated));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local));

        Assert.False(result.HasConflicts);
        Assert.NotNull(result.MergedSnapshot);
        Assert.False(result.MergedSnapshot.Files.ContainsKey(ItemPath(ItemId)));
        Assert.True(result.MergedSnapshot.Files.ContainsKey("aptechka.json"));
    }

    [Fact]
    public void Merge_FileDeleteVsModifyChoosesKeptFile()
    {
        var item = Item(ItemId, "Ибупрофен");
        var updated = Rename(item, "Нурофен", T1);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()));
        var remote = Snap(File(Manifest()), File(updated));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Remote));

        Assert.Equal("Нурофен", ReadItem(result, ItemId).Name);
    }

    [Fact]
    public void Merge_ThrowsWhenDuplicateKeyHasDifferentSides()
    {
        var (conflict, @base, local, remote) = NameConflict();

        var exception = Assert.Throws<ArgumentException>(() =>
            Merge(
                @base,
                local,
                remote,
                Resolve(conflict, SyncConflictSide.Local),
                Resolve(conflict, SyncConflictSide.Remote)));

        Assert.Contains("стороны", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_ThrowsWhenConflictSideIsUndefined()
    {
        var (conflict, @base, local, remote) = NameConflict();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Merge(@base, local, remote, Resolve(conflict, (SyncConflictSide)999)));
    }

    [Fact]
    public void Merge_WithoutResolutionsMatchesFourArgumentOverload()
    {
        var item = Item(ItemId, "Ибупрофен");
        var local = Rename(item, "Нурофен", T1);
        var remote = Rename(item, "Ибуфен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var left = Snap(File(Manifest()), File(local));
        var right = Snap(File(Manifest()), File(remote));

        var withEmpty = engine.Merge(@base, left, right, MergedAt, []);
        var without = engine.Merge(@base, left, right, MergedAt);

        Assert.Equal(without.HasConflicts, withEmpty.HasConflicts);
        Assert.Equal(without.Conflicts.Count, withEmpty.Conflicts.Count);
        Assert.Equal(without.Conflicts[0].Key, withEmpty.Conflicts[0].Key);
        Assert.Null(without.MergedSnapshot);
        Assert.Null(withEmpty.MergedSnapshot);
    }

    [Fact]
    public void Merge_IgnoresResolutionForDatasetIdMismatch()
    {
        var item = Item(ItemId, "Ибупрофен");
        var changed = new DatasetManifest(DatasetManifest.CurrentSchemaVersion, SecondItemId, T0);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(changed), File(item));
        var remote = Snap(File(Manifest()), File(item));
        var conflict = Assert.Single(Merge(@base, local, remote).Conflicts);

        var result = Merge(@base, local, remote, Resolve(conflict, SyncConflictSide.Local));

        Assert.Null(result.MergedSnapshot);
        Assert.Equal("aptechka.json", Assert.Single(result.Conflicts).Path);
    }

    [Fact]
    public void Merge_IgnoresResolutionForAConflictThatDisappeared()
    {
        var (conflict, @base, local, remote) = NameConflict();
        var aligned = Snap(File(Manifest()), File(Rename(Item(ItemId, "Ибупрофен"), "Нурофен", T1)));

        var result = Merge(@base, aligned, aligned, Resolve(conflict, SyncConflictSide.Remote));

        Assert.False(result.HasConflicts);
        Assert.Equal("Нурофен", ReadItem(result, ItemId).Name);
    }

    private (SyncConflict Conflict, DataSnapshot Base, DataSnapshot Local, DataSnapshot Remote) NameConflict()
    {
        var item = Item(ItemId, "Ибупрофен");
        var localItem = Rename(item, "Нурофен", T1);
        var remoteItem = Rename(item, "Ибуфен", T2);
        var @base = Snap(File(Manifest()), File(item));
        var local = Snap(File(Manifest()), File(localItem));
        var remote = Snap(File(Manifest()), File(remoteItem));
        return (Assert.Single(Merge(@base, local, remote).Conflicts), @base, local, remote);
    }

    private SnapshotMergeResult Merge(
        DataSnapshot @base,
        DataSnapshot local,
        DataSnapshot remote,
        params SyncConflictResolution[] resolutions) =>
        engine.Merge(@base, local, remote, MergedAt, resolutions);

    private static SyncConflictResolution Resolve(SyncConflict conflict, SyncConflictSide side) =>
        new(conflict, side);

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

    private static InventoryItem Item(string id, string name) =>
        InventoryItem.Create(
            id,
            T0,
            name,
            [],
            InventoryItemCategory.Medicine,
            ["ибупрофен"],
            "таблетки",
            "200 мг",
            null,
            true);

    private static InventoryItem Rename(InventoryItem item, string name, DateTimeOffset at) =>
        item.Update(
            at,
            name,
            item.Aliases,
            item.Category,
            item.ActiveIngredients,
            item.Form,
            item.Strength,
            item.Description,
            item.KeepInStock);

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

    private static Package WithNote(Package package, string? note, DateTimeOffset at) =>
        package.Update(
            at,
            package.Label,
            package.ExpirationDate,
            package.ExpirationPrecision,
            package.OpenedDate,
            package.ShelfLifeAfterOpeningDays,
            package.StockState,
            note);

    private static Package WithStock(Package package, StockState stockState, DateTimeOffset at) =>
        package.Update(
            at,
            package.Label,
            package.ExpirationDate,
            package.ExpirationPrecision,
            package.OpenedDate,
            package.ShelfLifeAfterOpeningDays,
            stockState,
            package.Note);

    private static DatasetManifest Manifest() =>
        new(DatasetManifest.CurrentSchemaVersion, DatasetId, T0);

    private static DataSnapshot Snap(params (string Path, byte[] Content)[] files) =>
        new(files.ToDictionary(static file => file.Path, static file => file.Content, StringComparer.Ordinal));

    private static (string Path, byte[] Content) File(InventoryItem item) => (ItemPath(item.Id), Bytes(item));

    private static (string Path, byte[] Content) File(Package package) => (PackagePath(package.Id), Bytes(package));

    private static (string Path, byte[] Content) File(DatasetManifest manifest) => ("aptechka.json", Bytes(manifest));

    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, AptechkaJson.Options);

    private static string ItemPath(string id) => $"items/{id}.json";

    private static string PackagePath(string id) => $"packages/{id}.json";
}
