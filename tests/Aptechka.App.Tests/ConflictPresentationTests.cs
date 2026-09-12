using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.Tests;

public sealed class ConflictPresentationTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string Path = "shopping/01ARZ3NDEKTSV4RRFFQ69G5FAV.json";
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecognizesShoppingPath()
    {
        Assert.True(ConflictPresentation.TryGetShoppingId(Path, out var id));
        Assert.Equal(ItemId, id);
        Assert.False(ConflictPresentation.TryGetPackageId(Path, out _));
        Assert.False(ConflictPresentation.TryGetItemId(Path, out _));
    }

    [Fact]
    public void FieldCaptions_CoverShoppingFields()
    {
        Assert.Equal("Ручная отметка", ConflictPresentation.FieldCaption(Field("isRequested", "true", "false")));
        Assert.Equal("Заметка", ConflictPresentation.FieldCaption(Field("note", "\"дом\"", "\"дача\"")));
        Assert.Equal("Архивирование", ConflictPresentation.FieldCaption(Field("deletedAt", "null", "\"2026-09-12T08:00:00+00:00\"")));
    }

    [Fact]
    public void FormatsRequestedBooleans()
    {
        var conflict = Field("isRequested", "true", "false");
        Assert.Equal("Добавлено вручную", ConflictPresentation.FormatSide(conflict, SyncConflictSide.Local));
        Assert.Equal("Не отмечено вручную", ConflictPresentation.FormatSide(conflict, SyncConflictSide.Remote));
    }

    [Fact]
    public void WholeShoppingItem_IsNotDescribedAsPackage()
    {
        var conflict = new SyncConflict(
            $"{Path}||{SyncConflictKind.FileChangedBoth}",
            Path,
            "",
            SyncConflictKind.FileChangedBoth,
            null,
            ShoppingJson(requested: true, note: "аптека", deletedAt: null),
            ShoppingJson(requested: false, note: null, deletedAt: null));

        var local = ConflictPresentation.FormatSide(conflict, SyncConflictSide.Local);
        var remote = ConflictPresentation.FormatSide(conflict, SyncConflictSide.Remote);
        Assert.DoesNotContain("Упаковка", local, StringComparison.Ordinal);
        Assert.DoesNotContain("Упаковка", remote, StringComparison.Ordinal);
        Assert.Contains("Добавлено вручную", local, StringComparison.Ordinal);
        Assert.Contains("Не отмечено вручную", remote, StringComparison.Ordinal);
        Assert.Contains("аптека", local, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityCaption_UsesLinkedItemName()
    {
        var item = InventoryItem.Create(
            ItemId,
            Now,
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            "таблетки",
            "200 мг",
            null,
            false);
        var conflict = Field("isRequested", "true", "false");

        Assert.Equal(
            "Ибупрофен, таблетки, 200 мг",
            ConflictPresentation.EntityCaption(conflict, item, null, null, null));
    }

    [Fact]
    public void EntityCaption_FallsBackWhenItemIsMissing()
    {
        var conflict = Field("isRequested", "true", "false");
        Assert.Equal(
            "Позиция отсутствует · 9G5FAV",
            ConflictPresentation.EntityCaption(conflict, null, null, null, null));
    }

    [Fact]
    public void DestructiveTombstoneAndDelete_AreDetected()
    {
        var tombstone = new SyncConflict(
            $"{Path}||{SyncConflictKind.FileChangedBoth}",
            Path,
            "",
            SyncConflictKind.FileChangedBoth,
            ShoppingJson(true, null, null),
            ShoppingJson(true, null, "2026-09-12T08:01:00+00:00"),
            ShoppingJson(true, null, null));
        var deleted = new SyncConflict(
            $"{Path}||{SyncConflictKind.FileDeleteVsModify}",
            Path,
            "",
            SyncConflictKind.FileDeleteVsModify,
            ShoppingJson(true, null, null),
            null,
            ShoppingJson(true, null, null));

        Assert.True(ConflictPresentation.IsDestructive(tombstone, SyncConflictSide.Local));
        Assert.False(ConflictPresentation.IsDestructive(tombstone, SyncConflictSide.Remote));
        Assert.True(ConflictPresentation.IsDestructive(deleted, SyncConflictSide.Local));
        Assert.False(ConflictPresentation.IsDestructive(deleted, SyncConflictSide.Remote));
    }

    private static SyncConflict Field(string field, string local, string remote) =>
        new(
            $"{Path}|{field}|{SyncConflictKind.FieldChangedBoth}",
            Path,
            field,
            SyncConflictKind.FieldChangedBoth,
            null,
            local,
            remote);

    private static string ShoppingJson(bool requested, string? note, string? deletedAt)
    {
        var noteJson = note is null ? "null" : $"\"{note}\"";
        var deleted = deletedAt is null ? "null" : $"\"{deletedAt}\"";
        return $$"""
            {"id":"{{ItemId}}","itemId":"{{ItemId}}","revision":1,"createdAt":"2026-09-12T08:00:00+00:00","updatedAt":"2026-09-12T08:00:00+00:00","deletedAt":{{deleted}},"isRequested":{{requested.ToString().ToLowerInvariant()}},"note":{{noteJson}}}
            """;
    }
}
