using Aptechka.Domain.Inventory;

namespace Aptechka.Domain.Tests;

public sealed class PackageTests
{
    private const string PackageId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    private const string ItemId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 18, 25, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Create_NormalizesLabelAndNote()
    {
        var package = Package.Create(
            PackageId,
            Now,
            ItemId,
            "  блистер  ",
            null,
            null,
            null,
            null,
            StockState.Available,
            " ");

        Assert.Equal("блистер", package.Label);
        Assert.Null(package.Note);
        Assert.Equal(1, package.Revision);
        Assert.Null(package.DeletedAt);
        Assert.Equal(ItemId, package.ItemId);
    }

    [Fact]
    public void Create_RequiresExpirationDateAndPrecisionTogether()
    {
        Assert.Throws<InvalidOperationException>(() => Create(
            expirationDate: new DateOnly(2027, 4, 30)));
        Assert.Throws<InvalidOperationException>(() => Create(
            expirationPrecision: ExpirationPrecision.Day));
    }

    [Fact]
    public void Create_RequiresMonthPrecisionToUseLastDayOfMonth()
    {
        Assert.Throws<InvalidOperationException>(() => Create(
            expirationDate: new DateOnly(2027, 4, 15),
            expirationPrecision: ExpirationPrecision.Month));

        var package = Create(
            expirationDate: new DateOnly(2027, 4, 30),
            expirationPrecision: ExpirationPrecision.Month);

        Assert.Equal(new DateOnly(2027, 4, 30), package.ExpirationDate);
        Assert.Equal(ExpirationPrecision.Month, package.ExpirationPrecision);
    }

    [Fact]
    public void Create_RequiresPositiveShelfLifeAfterOpening()
    {
        Assert.Throws<InvalidOperationException>(() => Create(shelfLifeAfterOpeningDays: 0));
        Assert.Throws<InvalidOperationException>(() => Create(shelfLifeAfterOpeningDays: -1));

        var package = Create(shelfLifeAfterOpeningDays: 1);
        Assert.Equal(1, package.ShelfLifeAfterOpeningDays);
    }

    [Fact]
    public void EffectiveExpiration_UsesLabeledDateWhenItIsTheOnlyKnownValue()
    {
        var package = Create(
            expirationDate: new DateOnly(2027, 4, 30),
            expirationPrecision: ExpirationPrecision.Day);

        Assert.Equal(new DateOnly(2027, 4, 30), ItemStock.GetEffectiveExpirationDate(package));
        Assert.Null(ItemStock.GetExpirationAfterOpening(package));
    }

    [Fact]
    public void EffectiveExpiration_UsesAfterOpeningDateWhenItIsTheOnlyKnownValue()
    {
        var package = Create(
            openedDate: new DateOnly(2026, 9, 1),
            shelfLifeAfterOpeningDays: 10);

        Assert.Equal(new DateOnly(2026, 9, 11), ItemStock.GetExpirationAfterOpening(package));
        Assert.Equal(new DateOnly(2026, 9, 11), ItemStock.GetEffectiveExpirationDate(package));
    }

    [Fact]
    public void EffectiveExpiration_ChoosesTheEarlierOfTwoKnownDates()
    {
        var earlierAfterOpening = Create(
            expirationDate: new DateOnly(2026, 9, 20),
            expirationPrecision: ExpirationPrecision.Day,
            openedDate: new DateOnly(2026, 9, 1),
            shelfLifeAfterOpeningDays: 10);
        var earlierLabeled = Create(
            expirationDate: new DateOnly(2026, 9, 5),
            expirationPrecision: ExpirationPrecision.Day,
            openedDate: new DateOnly(2026, 9, 1),
            shelfLifeAfterOpeningDays: 10);

        Assert.Equal(new DateOnly(2026, 9, 11), ItemStock.GetEffectiveExpirationDate(earlierAfterOpening));
        Assert.Equal(new DateOnly(2026, 9, 5), ItemStock.GetEffectiveExpirationDate(earlierLabeled));
    }

    [Fact]
    public void Package_IsUsableThroughTheEndOfTheEffectiveDate()
    {
        var package = Create(
            expirationDate: new DateOnly(2026, 9, 10),
            expirationPrecision: ExpirationPrecision.Day);

        Assert.True(ItemStock.IsUsable(package, Today));
        Assert.Equal(PackageUsability.Usable, ItemStock.GetUsability(package, Today));
        Assert.False(ItemStock.IsExpired(package, Today));
    }

    [Fact]
    public void Package_IsExpiredTheDayAfterTheEffectiveDate()
    {
        var package = Create(
            expirationDate: new DateOnly(2026, 9, 10),
            expirationPrecision: ExpirationPrecision.Day);

        Assert.False(ItemStock.IsUsable(package, Today.AddDays(1)));
        Assert.True(ItemStock.IsExpired(package, Today.AddDays(1)));
        Assert.Equal(PackageUsability.Expired, ItemStock.GetUsability(package, Today.AddDays(1)));
    }

    [Fact]
    public void Depleted_HasPriorityOverExpired()
    {
        var package = Create(
            expirationDate: new DateOnly(2026, 9, 1),
            expirationPrecision: ExpirationPrecision.Day,
            stockState: StockState.Depleted);

        Assert.False(ItemStock.IsUsable(package, Today));
        Assert.True(ItemStock.IsExpired(package, Today));
        Assert.Equal(PackageUsability.Depleted, ItemStock.GetUsability(package, Today));
    }

    [Fact]
    public void Update_PreservesIdentityAndIncrementsRevision()
    {
        var original = Create(label: "блистер");
        var updatedAt = Now.AddHours(1);
        var updated = original.Update(
            updatedAt,
            "коробка",
            new DateOnly(2027, 4, 30),
            ExpirationPrecision.Month,
            new DateOnly(2026, 9, 1),
            30,
            StockState.Low,
            "в холодильнике");

        Assert.Equal(PackageId, updated.Id);
        Assert.Equal(ItemId, updated.ItemId);
        Assert.Equal(Now, updated.CreatedAt);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(updatedAt, updated.UpdatedAt);
        Assert.Equal("коробка", updated.Label);
        Assert.Equal(StockState.Low, updated.StockState);
    }

    [Fact]
    public void Delete_SetsTombstoneAndIsIdempotent()
    {
        var original = Create();
        var deletedAt = Now.AddHours(2);
        var deleted = original.Delete(deletedAt);
        var repeated = deleted.Delete(deletedAt.AddMinutes(30));

        Assert.Equal(2, deleted.Revision);
        Assert.Equal(deletedAt, deleted.DeletedAt);
        Assert.Equal(deletedAt, deleted.UpdatedAt);
        Assert.Same(deleted, repeated);
    }

    [Fact]
    public void Availability_ExpiringAvailablePackageDoesNotBecomeLow()
    {
        var summary = ItemStock.Summarize(
            [
                Create(
                    expirationDate: Today,
                    expirationPrecision: ExpirationPrecision.Day,
                    stockState: StockState.Available),
            ],
            Today);

        Assert.Equal(ItemAvailability.Available, summary.Availability);
        Assert.Equal(1, summary.UsablePackageCount);
    }

    [Fact]
    public void Availability_IsAvailableWhenAnyUsablePackageIsAvailable()
    {
        var summary = ItemStock.Summarize(
            [
                Create(stockState: StockState.Available),
                Create(id: "01ARZ3NDEKTSV4RRFFQ69G5FAY", stockState: StockState.Low),
            ],
            Today);

        Assert.Equal(ItemAvailability.Available, summary.Availability);
    }

    [Fact]
    public void Availability_IsLowWhenOnlyUsableStockIsLow()
    {
        var summary = ItemStock.Summarize(
            [Create(stockState: StockState.Low)],
            Today);

        Assert.Equal(ItemAvailability.Low, summary.Availability);
    }

    [Fact]
    public void Availability_IsMissingWhenTheListIsEmpty()
    {
        var summary = ItemStock.Summarize([], Today);

        Assert.Equal(ItemAvailability.Missing, summary.Availability);
        Assert.Equal(0, summary.UsablePackageCount);
        Assert.Null(summary.NearestExpirationDate);
        Assert.Equal(0, summary.ExpiredPackageCount);
    }

    [Fact]
    public void Availability_IgnoresExpiredDepletedAndTombstonePackages()
    {
        var expired = Create(
            expirationDate: new DateOnly(2026, 9, 1),
            expirationPrecision: ExpirationPrecision.Day);
        var depleted = Create(id: "01ARZ3NDEKTSV4RRFFQ69G5FAY", stockState: StockState.Depleted);
        var tombstone = Create(id: "01ARZ3NDEKTSV4RRFFQ69G5FAZ").Delete(Now.AddMinutes(1));
        var usableLow = Create(id: "01ARZ3NDEKTSV4RRFFQ69G5FB0", stockState: StockState.Low);

        var summary = ItemStock.Summarize([expired, depleted, tombstone, usableLow], Today);

        Assert.Equal(ItemAvailability.Low, summary.Availability);
        Assert.Equal(1, summary.UsablePackageCount);
    }

    [Fact]
    public void Summary_ReportsNearestUsableExpirationAndUnfinishedExpiredCount()
    {
        var nearer = Create(
            expirationDate: new DateOnly(2026, 10, 1),
            expirationPrecision: ExpirationPrecision.Day);
        var later = Create(
            id: "01ARZ3NDEKTSV4RRFFQ69G5FAY",
            expirationDate: new DateOnly(2026, 12, 31),
            expirationPrecision: ExpirationPrecision.Day);
        var expired = Create(
            id: "01ARZ3NDEKTSV4RRFFQ69G5FAZ",
            expirationDate: new DateOnly(2026, 9, 1),
            expirationPrecision: ExpirationPrecision.Day);
        var depletedExpired = Create(
            id: "01ARZ3NDEKTSV4RRFFQ69G5FB0",
            expirationDate: new DateOnly(2026, 8, 1),
            expirationPrecision: ExpirationPrecision.Day,
            stockState: StockState.Depleted);
        var tombstoneExpired = Create(
            id: "01ARZ3NDEKTSV4RRFFQ69G5FB1",
            expirationDate: new DateOnly(2026, 8, 15),
            expirationPrecision: ExpirationPrecision.Day)
            .Delete(Now.AddMinutes(1));

        var summary = ItemStock.Summarize(
            [nearer, later, expired, depletedExpired, tombstoneExpired],
            Today);

        Assert.Equal(ItemAvailability.Available, summary.Availability);
        Assert.Equal(2, summary.UsablePackageCount);
        Assert.Equal(new DateOnly(2026, 10, 1), summary.NearestExpirationDate);
        Assert.Equal(1, summary.ExpiredPackageCount);
    }

    private static Package Create(
        string id = PackageId,
        string? label = null,
        DateOnly? expirationDate = null,
        ExpirationPrecision? expirationPrecision = null,
        DateOnly? openedDate = null,
        int? shelfLifeAfterOpeningDays = null,
        StockState stockState = StockState.Available,
        string? note = null) =>
        Package.Create(
            id,
            Now,
            ItemId,
            label,
            expirationDate,
            expirationPrecision,
            openedDate,
            shelfLifeAfterOpeningDays,
            stockState,
            note);
}
