using Aptechka.Domain.Inventory;

namespace Aptechka.Domain.Tests;

public sealed class InventoryItemTests
{
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

    [Fact]
    public void Create_NormalizesUserInput()
    {
        var now = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

        var item = InventoryItem.Create(
            ItemId,
            now,
            "  Ибупрофен  ",
            [" Нурофен ", ""],
            InventoryItemCategory.Medicine,
            [" ибупрофен ", "ИБУПРОФЕН", ""],
            " таблетки ",
            " 200 мг ",
            " ",
            true);

        Assert.Equal("Ибупрофен", item.Name);
        Assert.Equal(["Нурофен"], item.Aliases);
        Assert.Equal(["ибупрофен"], item.ActiveIngredients);
        Assert.Equal("таблетки", item.Form);
        Assert.Equal("200 мг", item.Strength);
        Assert.Null(item.Description);
        Assert.True(item.KeepInStock);
    }

    [Fact]
    public void Create_RemovesAliasDuplicatesIgnoringCase()
    {
        var item = InventoryItem.Create(
            ItemId,
            new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero),
            "Ибупрофен",
            ["Нурофен", "  нурофен  ", "НУРОФЕН", ""],
            InventoryItemCategory.Medicine,
            [],
            null,
            null,
            null,
            false);

        Assert.Equal(["Нурофен"], item.Aliases);
    }

    [Fact]
    public void Update_PreservesIdentityAndIncrementsRevision()
    {
        var createdAt = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        var original = InventoryItem.Create(
            ItemId,
            createdAt,
            "Вата",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            false);

        var updated = original.Update(
            updatedAt,
            "Вата медицинская",
            ["вата"],
            InventoryItemCategory.MedicalSupply,
            [],
            "рулон",
            null,
            null,
            true);

        Assert.Equal(ItemId, updated.Id);
        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(updatedAt, updated.UpdatedAt);
        Assert.Equal(["вата"], updated.Aliases);
        Assert.True(updated.KeepInStock);
        Assert.Null(updated.DeletedAt);
    }

    [Fact]
    public void Delete_SetsTombstoneAndIncrementsRevision()
    {
        var createdAt = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        var deletedAt = createdAt.AddHours(2);
        var original = InventoryItem.Create(
            ItemId,
            createdAt,
            "Бинт",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            false);

        var deleted = original.Delete(deletedAt);

        Assert.Equal(ItemId, deleted.Id);
        Assert.Equal(createdAt, deleted.CreatedAt);
        Assert.Equal(2, deleted.Revision);
        Assert.Equal(deletedAt, deleted.UpdatedAt);
        Assert.Equal(deletedAt, deleted.DeletedAt);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_ReturnsSameTombstone()
    {
        var createdAt = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        var firstDeletedAt = createdAt.AddMinutes(10);
        var original = InventoryItem.Create(
            ItemId,
            createdAt,
            "Бинт",
            [],
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            false);
        var deleted = original.Delete(firstDeletedAt);

        var repeated = deleted.Delete(firstDeletedAt.AddMinutes(30));

        Assert.Same(deleted, repeated);
        Assert.Equal(2, repeated.Revision);
        Assert.Equal(firstDeletedAt, repeated.DeletedAt);
    }
}
