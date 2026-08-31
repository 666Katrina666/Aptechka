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
            InventoryItemCategory.Medicine,
            [" ибупрофен ", "ИБУПРОФЕН", ""],
            " таблетки ",
            " 200 мг ",
            " ",
            true);

        Assert.Equal("Ибупрофен", item.Name);
        Assert.Equal(["ибупрофен"], item.ActiveIngredients);
        Assert.Equal("таблетки", item.Form);
        Assert.Equal("200 мг", item.Strength);
        Assert.Null(item.Description);
        Assert.True(item.KeepInStock);
    }

    [Fact]
    public void Update_IncrementsRevisionAndPreservesIdentity()
    {
        var createdAt = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        var original = InventoryItem.Create(
            ItemId,
            createdAt,
            "Вата",
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            false);

        var updated = original.Update(
            updatedAt,
            "Вата медицинская",
            InventoryItemCategory.MedicalSupply,
            [],
            null,
            null,
            null,
            true);

        Assert.Equal(ItemId, updated.Id);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(updatedAt, updated.UpdatedAt);
        Assert.True(updated.KeepInStock);
    }
}
