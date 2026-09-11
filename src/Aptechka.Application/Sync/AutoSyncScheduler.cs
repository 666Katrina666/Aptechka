namespace Aptechka.Application.Sync;

public enum AutoSyncReason
{
    Foreground,
    Reconnect,
}

public enum AutoSyncBlock
{
    None,
    NotConfigured,
    Offline,
    UnsafePage,
    InFlight,
    Cooldown,
    Conflict,
    PermanentFailure,
    AwaitingReconnect,
}

public enum AutoSyncFinish
{
    Succeeded,
    Conflict,
    TransientFailure,
    PermanentFailure,
    RateLimited,
    Cancelled,
}

public readonly record struct AutoSyncReadiness(
    bool HasSavedToken,
    bool HasValidTarget,
    bool HasInternet,
    bool MainPageSafe,
    bool HasUnresolvedConflict);

public readonly record struct AutoSyncDecision(
    bool ShouldRun,
    bool ShowOffline,
    AutoSyncBlock Block,
    AutoSyncReason Reason);

public static class AutoSyncPolicy
{
    public static bool AllowsReconnectRetry(SyncFailureKind kind) =>
        kind is SyncFailureKind.Offline or SyncFailureKind.Timeout or SyncFailureKind.RemoteUnavailable;

    public static AutoSyncFinish FinishFor(SyncFailureKind kind) =>
        kind switch
        {
            SyncFailureKind.Offline or SyncFailureKind.Timeout or SyncFailureKind.RemoteUnavailable =>
                AutoSyncFinish.TransientFailure,
            SyncFailureKind.RateLimited => AutoSyncFinish.RateLimited,
            _ => AutoSyncFinish.PermanentFailure,
        };

    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}

public sealed class AutoSyncScheduler
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    private readonly object gate = new();
    private readonly Func<TimeSpan> clock;
    private bool inFlight;
    private bool pending;
    private bool pendingBypassesCooldown;
    private AutoSyncReason pendingReason;
    private bool internetKnown;
    private bool hasInternet;
    private TimeSpan cooldownUntil;
    private bool blocksAutomatic;
    private bool awaitReconnect;
    private bool unresolvedConflict;

    public AutoSyncScheduler(Func<TimeSpan>? clock = null)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        this.clock = clock ?? (() => System.Diagnostics.Stopwatch.GetElapsedTime(started));
    }

    public event Action? BusyChanged;

    public bool IsInFlight
    {
        get
        {
            lock (gate)
            {
                return inFlight;
            }
        }
    }

    public bool HasPending
    {
        get
        {
            lock (gate)
            {
                return pending;
            }
        }
    }

    public bool ObserveInternet(bool internet)
    {
        lock (gate)
        {
            var becameAvailable = internetKnown && !hasInternet && internet;
            internetKnown = true;
            hasInternet = internet;
            return becameAvailable;
        }
    }

    public AutoSyncDecision Request(AutoSyncReason reason, AutoSyncReadiness readiness)
    {
        bool changed;
        AutoSyncDecision decision;
        lock (gate)
        {
            decision = Admit(reason, readiness, fromPendingFlush: false, out changed);
        }

        Raise(changed);
        return decision;
    }

    public AutoSyncDecision FlushPending(AutoSyncReadiness readiness)
    {
        bool changed;
        AutoSyncDecision decision;
        lock (gate)
        {
            if (!pending || inFlight)
            {
                return new AutoSyncDecision(false, false, AutoSyncBlock.InFlight, pendingReason);
            }

            decision = Admit(pendingReason, readiness, fromPendingFlush: true, out changed);
        }

        Raise(changed);
        return decision;
    }

    public bool TryBeginExclusive()
    {
        bool changed;
        lock (gate)
        {
            if (inFlight)
            {
                return false;
            }

            inFlight = true;
            changed = true;
        }

        Raise(changed);
        return true;
    }

    public bool TryKeepAutomaticClaim(AutoSyncReadiness readiness)
    {
        bool changed = false;
        bool keep;
        lock (gate)
        {
            keep = inFlight && CanExecute(readiness);
            if (!keep && inFlight)
            {
                inFlight = false;
                Remember(AutoSyncReason.Foreground, bypassCooldown: true);
                changed = true;
            }
        }

        Raise(changed);
        return keep;
    }

    public void End(AutoSyncFinish finish, bool suppressFollowUp)
    {
        bool changed;
        lock (gate)
        {
            changed = inFlight;
            inFlight = false;
            var now = clock();
            switch (finish)
            {
                case AutoSyncFinish.Succeeded:
                    unresolvedConflict = false;
                    blocksAutomatic = false;
                    awaitReconnect = false;
                    cooldownUntil = now + Cooldown;
                    if (suppressFollowUp || !pendingBypassesCooldown)
                    {
                        pending = false;
                    }

                    break;
                case AutoSyncFinish.Conflict:
                    unresolvedConflict = true;
                    pending = false;
                    break;
                case AutoSyncFinish.Cancelled:
                    pending = false;
                    break;
                case AutoSyncFinish.PermanentFailure:
                case AutoSyncFinish.RateLimited:
                    blocksAutomatic = true;
                    pending = false;
                    break;
                default:
                    awaitReconnect = true;
                    if (!pendingBypassesCooldown)
                    {
                        pending = false;
                    }

                    break;
            }
        }

        Raise(changed);
    }

    private AutoSyncDecision Admit(
        AutoSyncReason reason,
        AutoSyncReadiness readiness,
        bool fromPendingFlush,
        out bool busyChanged)
    {
        busyChanged = false;
        var bypassCooldown = reason == AutoSyncReason.Reconnect || (fromPendingFlush && pendingBypassesCooldown);
        if (!readiness.HasSavedToken || !readiness.HasValidTarget)
        {
            return Decline(AutoSyncBlock.NotConfigured, reason);
        }

        if (unresolvedConflict || readiness.HasUnresolvedConflict)
        {
            return Decline(AutoSyncBlock.Conflict, reason);
        }

        if (blocksAutomatic)
        {
            return Decline(AutoSyncBlock.PermanentFailure, reason);
        }

        if (!readiness.HasInternet)
        {
            Remember(reason, bypassCooldown: true);
            return new AutoSyncDecision(false, !inFlight, AutoSyncBlock.Offline, reason);
        }

        if (!readiness.MainPageSafe)
        {
            Remember(reason, bypassCooldown: true);
            return Decline(AutoSyncBlock.UnsafePage, reason);
        }

        if (inFlight)
        {
            Remember(reason, bypassCooldown: reason == AutoSyncReason.Reconnect);
            return Decline(AutoSyncBlock.InFlight, reason);
        }

        if (!bypassCooldown && clock() < cooldownUntil)
        {
            return Decline(AutoSyncBlock.Cooldown, reason);
        }

        if (awaitReconnect && reason == AutoSyncReason.Foreground && !bypassCooldown)
        {
            return Decline(AutoSyncBlock.AwaitingReconnect, reason);
        }

        inFlight = true;
        pending = false;
        pendingBypassesCooldown = false;
        busyChanged = true;
        return new AutoSyncDecision(true, false, AutoSyncBlock.None, reason);
    }

    private bool CanExecute(AutoSyncReadiness readiness) =>
        readiness.HasSavedToken &&
        readiness.HasValidTarget &&
        readiness.HasInternet &&
        readiness.MainPageSafe &&
        !readiness.HasUnresolvedConflict &&
        !unresolvedConflict;

    private void Remember(AutoSyncReason reason, bool bypassCooldown)
    {
        if (!pending || reason == AutoSyncReason.Reconnect)
        {
            pendingReason = reason;
        }

        pending = true;
        if (bypassCooldown)
        {
            pendingBypassesCooldown = true;
        }
    }

    private static AutoSyncDecision Decline(AutoSyncBlock block, AutoSyncReason reason) =>
        new(false, false, block, reason);

    private void Raise(bool busyChanged)
    {
        if (busyChanged)
        {
            BusyChanged?.Invoke();
        }
    }
}
