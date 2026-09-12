using Aptechka.Domain.Inventory;

namespace Aptechka.Domain.Tests;

public sealed class ShoppingItemTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string OtherId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_UsesItemIdAsIdentity()
    {
        var shopping = ShoppingItem.Create(ItemId, Now, true, "  купить упаковку  ");

        Assert.Equal(ItemId, shopping.Id);
        Assert.Equal(ItemId, shopping.ItemId);
        Assert.Equal(1, shopping.Revision);
        Assert.Equal(Now, shopping.CreatedAt);
        Assert.Equal(Now, shopping.UpdatedAt);
        Assert.Null(shopping.DeletedAt);
        Assert.True(shopping.IsRequested);
        Assert.Equal("купить упаковку", shopping.Note);
    }

    [Fact]
    public void Create_TurnsBlankNoteIntoNull()
    {
        Assert.Null(ShoppingItem.Create(ItemId, Now, false, "   ").Note);
        Assert.Null(ShoppingItem.Create(ItemId, Now, false, null).Note);
        Assert.False(ShoppingItem.Create(ItemId, Now, false, null).IsRequested);
    }

    [Fact]
    public void Create_RejectsInvalidUlid()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ShoppingItem.Create("not-a-ulid", Now, true, null));
    }

    [Fact]
    public void EnsureValid_RejectsMismatchedIdAndItemId()
    {
        AssertInvalid(Valid() with { ItemId = OtherId });
        AssertInvalid(Valid() with { Id = OtherId });
    }

    [Fact]
    public void EnsureValid_RejectsInvalidUlidAndNonCanonicalNote()
    {
        AssertInvalid(Valid() with { Id = "not-a-ulid", ItemId = "not-a-ulid" });
        AssertInvalid(Valid() with { Note = "" });
        AssertInvalid(Valid() with { Note = "   " });
        AssertInvalid(Valid() with { Note = " купить " });
        AssertInvalid(Valid() with { Revision = 0 });
    }

    [Fact]
    public void Update_IncrementsRevisionAndTimestamp()
    {
        var later = Now.AddMinutes(5);
        var updated = Valid().Update(later, false, "аптека");

        Assert.Equal(2, updated.Revision);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal(Now, updated.CreatedAt);
        Assert.False(updated.IsRequested);
        Assert.Equal("аптека", updated.Note);
        Assert.Equal(ItemId, updated.Id);
        Assert.Equal(ItemId, updated.ItemId);
    }

    [Fact]
    public void Update_IsIdempotentForTheSameValues()
    {
        var original = Valid();
        var repeated = original.Update(Now.AddHours(1), original.IsRequested, "  купить упаковку  ");

        Assert.Same(original, repeated);
        Assert.Equal(1, repeated.Revision);
        Assert.Equal(Now, repeated.UpdatedAt);
    }

    [Fact]
    public void Delete_SetsTombstoneWithoutClearingTheManualFlag()
    {
        var original = Valid();
        var deletedAt = Now.AddMinutes(10);
        var deleted = original.Delete(deletedAt);
        var repeated = deleted.Delete(deletedAt.AddHours(1));

        Assert.Equal(2, deleted.Revision);
        Assert.Equal(deletedAt, deleted.UpdatedAt);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.True(deleted.IsRequested);
        Assert.Equal("купить упаковку", deleted.Note);
        Assert.Same(deleted, repeated);
    }

    [Fact]
    public void ArchiveOfInventoryItem_DoesNotMutateShoppingItem()
    {
        var item = InventoryItem.Create(
            ItemId,
            Now,
            "Ибупрофен",
            [],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            true);
        var shopping = Valid();

        var archived = item.Delete(Now.AddMinutes(5));

        Assert.NotNull(archived.DeletedAt);
        Assert.Null(shopping.DeletedAt);
        Assert.True(shopping.IsRequested);
        Assert.Equal(1, shopping.Revision);
    }

    private static ShoppingItem Valid() =>
        ShoppingItem.Create(ItemId, Now, true, "купить упаковку");

    private static void AssertInvalid(ShoppingItem shopping) =>
        Assert.Throws<InvalidOperationException>(shopping.EnsureValid);
}
