using System.Globalization;
using Aptechka.Application.Sync;

namespace Aptechka.App;

public enum SyncUiState
{
    NotConfigured,
    Synced,
    LocalChanges,
    Syncing,
    Conflict,
    Error,
}

internal static class SyncStatusText
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Headline(SyncUiState state) =>
        state switch
        {
            SyncUiState.NotConfigured => "Синхронизация не настроена",
            SyncUiState.Synced => "Синхронизировано",
            SyncUiState.LocalChanges => "Есть локальные изменения",
            SyncUiState.Syncing => "Синхронизация",
            SyncUiState.Conflict => "Нужно выбрать версии",
            SyncUiState.Error => "Синхронизация остановлена",
            _ => "Синхронизация",
        };

    public static string Detail(SyncFailureKind kind) =>
        kind switch
        {
            SyncFailureKind.InvalidConfiguration => "Проверь владельца, репозиторий и ветку.",
            SyncFailureKind.Offline => "Нет сети. Изменения сохранены на устройстве. Проверь подключение и повтори.",
            SyncFailureKind.Timeout => "GitHub не ответил вовремя. Изменения сохранены. Повтори синхронизацию.",
            SyncFailureKind.Authentication => "GitHub-токен недействителен. Вставь новый токен и повтори.",
            SyncFailureKind.AccessDenied => "У токена нет доступа на запись в этот репозиторий. Проверь Contents: Read and write.",
            SyncFailureKind.RepositoryNotFound => "Репозиторий или ветка не найдены. Проверь настройки и доступ токена.",
            SyncFailureKind.RateLimited => "GitHub временно ограничил запросы. Повтори позже.",
            SyncFailureKind.RemoteUnavailable => "GitHub временно недоступен. Локальные данные сохранены.",
            SyncFailureKind.InvalidData => "Данные имеют повреждённый или несовместимый формат. Автоматическая перезапись остановлена.",
            SyncFailureKind.LocalStorage => "Не удалось прочитать или сохранить локальные данные. Проверь свободное место и доступ приложения.",
            SyncFailureKind.LocalChangedRepeatedly => "Данные на устройстве менялись во время синхронизации. Повтори попытку.",
            SyncFailureKind.RemoteChangedRepeatedly => "GitHub несколько раз изменился во время отправки. Повтори синхронизацию.",
            _ => "Не удалось синхронизировать. Локальные данные сохранены.",
        };

    public static string AutomaticDetail(AutoSyncReason reason) =>
        reason == AutoSyncReason.Reconnect
            ? "Соединение восстановлено. Синхронизируем изменения."
            : "Проверяем изменения при запуске приложения.";

    public const string AutomaticCompleted = "Автоматическая синхронизация завершена.";

    public static string? LastSuccessful(DateTimeOffset? utc) =>
        utc is { } value ? $"Последняя успешная: {value.ToLocalTime().ToString("g", Russian)}" : null;
}
