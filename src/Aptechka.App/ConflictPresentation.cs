using System.Globalization;
using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

internal static class ConflictPresentation
{
    private const string Unspecified = "Не указано";
    private const string Archived = "Архивировано";
    private const string Removed = "Удалено";
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static bool IsManifest(SyncConflict conflict) =>
        conflict.Path.Equals("aptechka.json", StringComparison.Ordinal);

    public static bool TryGetItemId(string path, out string id) =>
        TryGetId(path, "items/", out id);

    public static bool TryGetPackageId(string path, out string id) =>
        TryGetId(path, "packages/", out id);

    public static string EntityCaption(
        SyncConflict conflict,
        InventoryItem? item,
        Package? package,
        InventoryItem? packageItem)
    {
        if (TryDescribeJson(conflict.LocalValueJson, out var described) ||
            TryDescribeJson(conflict.RemoteValueJson, out described) ||
            TryDescribeJson(conflict.BaseValueJson, out described))
        {
            return described;
        }

        if (item is not null)
        {
            return ItemCaption(item.Name, item.Form, item.Strength);
        }

        if (package is not null)
        {
            return PackageCaption(package.Label, package.ExpirationDate, package.ExpirationPrecision, packageItem?.Name);
        }

        if (TryGetPackageId(conflict.Path, out _))
        {
            return "Упаковка";
        }

        return TryGetItemId(conflict.Path, out _) ? "Позиция" : "Различие в данных";
    }

    public static string FieldCaption(SyncConflict conflict) =>
        conflict.Kind switch
        {
            SyncConflictKind.DeleteVsModify => "Архивирование",
            SyncConflictKind.FileChangedBoth => "Целая версия",
            SyncConflictKind.FileDeleteVsModify => "Наличие",
            _ => conflict.Field switch
            {
                "name" => "Название",
                "aliases" => "Другие названия",
                "category" => "Категория",
                "activeIngredients" => "Действующие вещества",
                "form" => "Форма",
                "strength" => "Дозировка",
                "description" => "Описание",
                "keepInStock" => "Иметь постоянно",
                "label" => "Подпись упаковки",
                "expiration" => "Срок годности",
                "openedDate" => "Дата вскрытия",
                "shelfLifeAfterOpeningDays" => "Срок после вскрытия",
                "stockState" => "Наличие",
                "note" => "Заметка",
                "deletedAt" => "Архивирование",
                _ => "Различие",
            },
        };

    public static string FormatSide(SyncConflict conflict, SyncConflictSide side)
    {
        var json = side == SyncConflictSide.Local ? conflict.LocalValueJson : conflict.RemoteValueJson;
        if (json is null)
        {
            return Removed;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return FormatElement(document.RootElement, side);
        }
        catch (JsonException)
        {
            return Fallback(side);
        }
    }

    public static bool IsDestructive(SyncConflict conflict, SyncConflictSide side)
    {
        var json = side == SyncConflictSide.Local ? conflict.LocalValueJson : conflict.RemoteValueJson;
        if (json is null)
        {
            return conflict.Kind is SyncConflictKind.FileDeleteVsModify or SyncConflictKind.FileChangedBoth;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return IsTombstone(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatElement(JsonElement element, SyncConflictSide side) =>
        element.ValueKind switch
        {
            JsonValueKind.Null => Unspecified,
            JsonValueKind.True => "Да",
            JsonValueKind.False => "Нет",
            JsonValueKind.Number => element.TryGetInt64(out var number) ? number.ToString(Russian) : Unspecified,
            JsonValueKind.String => FormatString(element.GetString()),
            JsonValueKind.Array => FormatArray(element),
            JsonValueKind.Object => FormatObject(element, side),
            _ => Fallback(side),
        };

    private static string FormatString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unspecified;
        }

        return value switch
        {
            "medicine" => "Лекарство",
            "medical_supply" => "Медицинский расходник",
            "available" => PackageText.FormatStockState(StockState.Available),
            "low" => PackageText.FormatStockState(StockState.Low),
            "depleted" => PackageText.FormatStockState(StockState.Depleted),
            _ when TryParseDateOnly(value, out var date) => PackageText.FormatDate(date),
            _ when TryParseDateTime(value, out var stamp) => stamp.ToLocalTime().ToString("g", Russian),
            _ => value,
        };
    }

    private static string FormatArray(JsonElement element)
    {
        var parts = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? Unspecified : string.Join(", ", parts);
    }

    private static string FormatObject(JsonElement element, SyncConflictSide side)
    {
        if (IsTombstone(element))
        {
            return TryReadName(element, out var name)
                ? $"{Archived}: {name}"
                : Archived;
        }

        if (element.TryGetProperty("name", out _))
        {
            return ItemCaption(
                ReadString(element, "name"),
                ReadString(element, "form"),
                ReadString(element, "strength"));
        }

        if (element.TryGetProperty("stockState", out _) ||
            element.TryGetProperty("label", out _) ||
            element.TryGetProperty("itemId", out _))
        {
            return PackageCaption(
                ReadString(element, "label"),
                TryReadDateOnly(element, "expirationDate", out var date) ? date : null,
                ReadPrecision(element),
                null);
        }

        if (element.TryGetProperty("expirationDate", out _) ||
            element.TryGetProperty("expirationPrecision", out _))
        {
            return FormatExpiration(
                TryReadDateOnly(element, "expirationDate", out var date) ? date : null,
                ReadPrecision(element));
        }

        return Fallback(side);
    }

    private static bool TryDescribeJson(string? json, out string caption)
    {
        caption = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !LooksLikeEntity(document.RootElement))
            {
                return false;
            }

            caption = FormatObject(document.RootElement, SyncConflictSide.Local);
            return caption is not "Версия на этом устройстве";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeEntity(JsonElement element) =>
        element.TryGetProperty("name", out _) ||
        element.TryGetProperty("itemId", out _) ||
        element.TryGetProperty("stockState", out _);

    private static string ItemCaption(string? name, string? form, string? strength)
    {
        var parts = new[] { name, form, strength }.Where(static value => !string.IsNullOrWhiteSpace(value));
        var text = string.Join(", ", parts);
        return string.IsNullOrEmpty(text) ? "Позиция" : text;
    }

    private static string PackageCaption(
        string? label,
        DateOnly? expirationDate,
        ExpirationPrecision? precision,
        string? itemName)
    {
        var title = string.IsNullOrWhiteSpace(label) ? "Упаковка" : label.Trim();
        var expiration = FormatExpiration(expirationDate, precision);
        var withItem = string.IsNullOrWhiteSpace(itemName) ? title : $"{itemName}: {title}";
        return expiration == Unspecified ? withItem : $"{withItem}, {expiration}";
    }

    private static string FormatExpiration(DateOnly? date, ExpirationPrecision? precision)
    {
        if (date is null)
        {
            return Unspecified;
        }

        return precision == ExpirationPrecision.Month
            ? PackageText.FormatMonthYear(date.Value)
            : PackageText.FormatDate(date.Value);
    }

    private static bool IsTombstone(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("deletedAt", out var deletedAt) &&
        deletedAt.ValueKind is JsonValueKind.String or JsonValueKind.Number;

    private static bool TryReadName(JsonElement element, out string name)
    {
        name = ReadString(element, "name") ?? ReadString(element, "label") ?? "";
        return name.Length > 0;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static bool TryReadDateOnly(JsonElement element, string name, out DateOnly date)
    {
        date = default;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               TryParseDateOnly(property.GetString(), out date);
    }

    private static ExpirationPrecision? ReadPrecision(JsonElement element)
    {
        if (!element.TryGetProperty("expirationPrecision", out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString() switch
        {
            "month" => ExpirationPrecision.Month,
            "day" => ExpirationPrecision.Day,
            _ => null,
        };
    }

    private static bool TryParseDateOnly(string? value, out DateOnly date) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
        DateOnly.TryParse(value, Russian, DateTimeStyles.None, out date);

    private static bool TryParseDateTime(string? value, out DateTimeOffset stamp) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp) ||
        DateTimeOffset.TryParse(value, Russian, DateTimeStyles.None, out stamp);

    private static string Fallback(SyncConflictSide side) =>
        side == SyncConflictSide.Local ? "Версия на этом устройстве" : "Версия из GitHub";

    private static bool TryGetId(string path, string prefix, out string id)
    {
        id = "";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
            !path.EndsWith(".json", StringComparison.Ordinal))
        {
            return false;
        }

        id = path[prefix.Length..^".json".Length];
        return id.Length > 0;
    }
}
