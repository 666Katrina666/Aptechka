using System.Globalization;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

internal static class PackageText
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Availability(ItemAvailability availability) =>
        availability switch
        {
            ItemAvailability.Available => "В наличии",
            ItemAvailability.Low => "Скоро закончится",
            ItemAvailability.Missing => "Нет дома",
            _ => availability.ToString(),
        };

    public static string FormatStockState(StockState stockState) =>
        stockState switch
        {
            Domain.Inventory.StockState.Available => "В наличии",
            Domain.Inventory.StockState.Low => "Скоро закончится",
            Domain.Inventory.StockState.Depleted => "Закончилась",
            _ => stockState.ToString(),
        };

    public static string? UsablePackageCount(int count) =>
        count <= 0
            ? null
            : Count(count, "пригодная упаковка", "пригодные упаковки", "пригодных упаковок");

    public static string? NearestExpiration(DateOnly? date) =>
        date is { } value ? $"Ближайший срок: {FormatDate(value)}" : null;

    public static string? ExpiredWarning(int count) =>
        count <= 0
            ? null
            : count == 1
                ? "Есть просроченная упаковка"
                : $"Есть просроченные упаковки: {count}";

    public static string EffectiveExpiration(Package package)
    {
        var effective = ItemStock.GetEffectiveExpirationDate(package);
        return effective is { } date ? $"Срок до {FormatDate(date)}" : "Срок не указан";
    }

    public static string? Opened(Package package) =>
        package.OpenedDate is { } opened ? $"Вскрыта {FormatDate(opened)}" : null;

    public static string FormatDate(DateOnly date) =>
        date.ToString("dd.MM.yyyy", Russian);

    public static string FormatMonthYear(DateOnly date) =>
        $"{Russian.DateTimeFormat.GetMonthName(date.Month)} {date.Year}";

    public static DateOnly LastDayOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static string Count(int count, string one, string few, string many)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;
        var form = n is >= 11 and <= 14
            ? many
            : n1 == 1
                ? one
                : n1 is 2 or 3 or 4
                    ? few
                    : many;
        return $"{count} {form}";
    }
}
