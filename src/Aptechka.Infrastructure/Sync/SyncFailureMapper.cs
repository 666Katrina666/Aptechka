using System.Net;
using System.Net.Http;
using System.Text.Json;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.GitHub;

namespace Aptechka.Infrastructure.Sync;

internal static class SyncFailureMapper
{
    public static Exception Map(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            SyncFailureException => exception,
            OperationCanceledException when cancellationToken.IsCancellationRequested => exception,
            ArgumentException argument when IsConfiguration(argument) => Failure(
                SyncFailureKind.InvalidConfiguration,
                "Проверь владельца, репозиторий и ветку.",
                exception),
            HttpRequestException => Failure(SyncFailureKind.Offline, "Нет сети. Изменения сохранены на устройстве.", exception),
            OperationCanceledException or TimeoutException => Failure(
                SyncFailureKind.Timeout,
                "GitHub не ответил вовремя. Изменения сохранены.",
                exception),
            GitHubApiException api => FromGitHub(api),
            GitHubHeadChangedException => Failure(
                SyncFailureKind.RemoteChangedRepeatedly,
                "GitHub несколько раз изменился во время отправки.",
                exception),
            FormatException => Failure(SyncFailureKind.InvalidData, SafeInvalidData(), exception),
            JsonException or InvalidDataException => Failure(SyncFailureKind.InvalidData, SafeInvalidData(), exception),
            IOException or UnauthorizedAccessException => Failure(
                SyncFailureKind.LocalStorage,
                "Не удалось прочитать или сохранить локальные данные.",
                exception),
            _ => Failure(SyncFailureKind.Unknown, "Не удалось синхронизировать. Локальные данные сохранены.", exception),
        };

    private static SyncFailureException FromGitHub(GitHubApiException exception)
    {
        if (exception.IsRateLimited || exception.StatusCode == (int)HttpStatusCode.TooManyRequests)
        {
            return Failure(SyncFailureKind.RateLimited, "GitHub временно ограничил запросы.", exception);
        }

        return exception.StatusCode switch
        {
            401 => Failure(SyncFailureKind.Authentication, "GitHub-токен недействителен.", exception),
            403 => Failure(SyncFailureKind.AccessDenied, "У токена нет доступа на запись.", exception),
            404 => Failure(SyncFailureKind.RepositoryNotFound, "Репозиторий или ветка не найдены.", exception),
            >= 500 and < 600 => Failure(SyncFailureKind.RemoteUnavailable, "GitHub временно недоступен.", exception),
            _ => Failure(SyncFailureKind.Unknown, "Не удалось синхронизировать. Локальные данные сохранены.", exception),
        };
    }

    private static string SafeInvalidData() =>
        "Данные имеют повреждённый или несовместимый формат.";

    private static SyncFailureException Failure(SyncFailureKind kind, string message, Exception exception) =>
        new(kind, message, exception);

    private static bool IsConfiguration(ArgumentException exception) =>
        exception.ParamName is "Owner" or nameof(SyncTarget.Owner)
            or "Repository" or nameof(SyncTarget.Repository)
            or "Branch" or nameof(SyncTarget.Branch)
            or nameof(SyncTarget);
}
