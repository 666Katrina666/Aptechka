namespace Aptechka.Application.Sync;

public enum SyncFailureKind
{
    InvalidConfiguration,
    Offline,
    Timeout,
    Authentication,
    AccessDenied,
    RepositoryNotFound,
    RateLimited,
    RemoteUnavailable,
    InvalidData,
    LocalStorage,
    LocalChangedRepeatedly,
    RemoteChangedRepeatedly,
    Unknown,
}

public sealed class SyncFailureException : Exception
{
    public SyncFailureException(SyncFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
    }

    public SyncFailureKind Kind { get; }
}
