using Aptechka.Application.Sync;

namespace Aptechka.Application.Tests;

public sealed class AutoSyncSchedulerTests
{
    private static AutoSyncReadiness Ready(
        bool hasInternet = true,
        bool mainPageSafe = true,
        bool hasToken = true,
        bool hasTarget = true,
        bool conflict = false) =>
        new(hasToken, hasTarget, hasInternet, mainPageSafe, conflict);

    [Fact]
    public void TwoSignals_StartOneAttempt()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        var runs = 0;
        Parallel.For(0, 8, _ =>
        {
            if (scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun)
            {
                Interlocked.Increment(ref runs);
            }
        });

        Assert.Equal(1, runs);
        Assert.True(scheduler.HasPending);
    }

    [Fact]
    public void ManualAndAuto_DoNotRunTogether()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);

        Assert.True(scheduler.TryBeginExclusive());
        var auto = scheduler.Request(AutoSyncReason.Foreground, Ready());

        Assert.False(auto.ShouldRun);
        Assert.False(scheduler.TryBeginExclusive());
        Assert.True(scheduler.HasPending);
    }

    [Fact]
    public void ForegroundBurst_IsCoalescedByCooldown()
    {
        var now = TimeSpan.Zero;
        var scheduler = new AutoSyncScheduler(() => now);
        Assert.True(scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun);
        Assert.Equal(AutoSyncBlock.InFlight, scheduler.Request(AutoSyncReason.Foreground, Ready()).Block);

        scheduler.End(AutoSyncFinish.Succeeded, suppressFollowUp: false);
        now += TimeSpan.FromSeconds(1);
        var burst = scheduler.Request(AutoSyncReason.Foreground, Ready());

        Assert.False(burst.ShouldRun);
        Assert.Equal(AutoSyncBlock.Cooldown, burst.Block);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void Reconnect_StartsOnlyOnTransitionToInternet()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);

        Assert.False(scheduler.ObserveInternet(true));
        Assert.False(scheduler.ObserveInternet(true));
        Assert.False(scheduler.ObserveInternet(false));
        Assert.True(scheduler.ObserveInternet(true));
        Assert.False(scheduler.ObserveInternet(true));
    }

    [Fact]
    public void Offline_DoesNotStartAndKeepsPending()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);

        var decision = scheduler.Request(AutoSyncReason.Foreground, Ready(hasInternet: false));

        Assert.False(decision.ShouldRun);
        Assert.True(decision.ShowOffline);
        Assert.True(scheduler.HasPending);
        Assert.False(scheduler.IsInFlight);
    }

    [Fact]
    public void Pending_RunsWhenInternetReturns()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        Assert.True(scheduler.Request(AutoSyncReason.Foreground, Ready(hasInternet: false)).ShowOffline);
        Assert.False(scheduler.ObserveInternet(false));
        Assert.True(scheduler.ObserveInternet(true));

        var decision = scheduler.Request(AutoSyncReason.Reconnect, Ready());

        Assert.True(decision.ShouldRun);
        Assert.Equal(AutoSyncReason.Reconnect, decision.Reason);
    }

    [Fact]
    public void UnsafePage_DefersUntilFlush()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        var deferred = scheduler.Request(AutoSyncReason.Foreground, Ready(mainPageSafe: false));
        Assert.False(deferred.ShouldRun);
        Assert.True(scheduler.HasPending);

        var flush = scheduler.FlushPending(Ready());

        Assert.True(flush.ShouldRun);
        Assert.False(scheduler.FlushPending(Ready()).ShouldRun);
    }

    [Fact]
    public void Conflict_DoesNotStartAutoLoop()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        Assert.True(scheduler.TryBeginExclusive());
        scheduler.End(AutoSyncFinish.Conflict, suppressFollowUp: true);

        var foreground = scheduler.Request(AutoSyncReason.Foreground, Ready());
        var reconnect = scheduler.Request(AutoSyncReason.Reconnect, Ready(conflict: true));

        Assert.False(foreground.ShouldRun);
        Assert.Equal(AutoSyncBlock.Conflict, foreground.Block);
        Assert.False(reconnect.ShouldRun);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void PermanentFailures_DoNotRetry()
    {
        foreach (var kind in new[]
        {
            SyncFailureKind.Authentication,
            SyncFailureKind.AccessDenied,
            SyncFailureKind.RepositoryNotFound,
            SyncFailureKind.InvalidConfiguration,
            SyncFailureKind.InvalidData,
            SyncFailureKind.LocalStorage,
            SyncFailureKind.LocalChangedRepeatedly,
            SyncFailureKind.RemoteChangedRepeatedly,
            SyncFailureKind.Unknown,
            SyncFailureKind.RateLimited,
        })
        {
            var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
            Assert.True(scheduler.TryBeginExclusive());
            scheduler.End(AutoSyncPolicy.FinishFor(kind), suppressFollowUp: false);

            Assert.False(scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun);
            Assert.False(scheduler.Request(AutoSyncReason.Reconnect, Ready()).ShouldRun);
            Assert.False(scheduler.HasPending);
        }
    }

    [Fact]
    public void TransientFailures_RetryOnlyOnReconnect()
    {
        foreach (var kind in new[]
        {
            SyncFailureKind.Offline,
            SyncFailureKind.Timeout,
            SyncFailureKind.RemoteUnavailable,
        })
        {
            var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
            Assert.True(scheduler.TryBeginExclusive());
            scheduler.End(AutoSyncPolicy.FinishFor(kind), suppressFollowUp: false);

            Assert.False(scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun);
            Assert.True(scheduler.Request(AutoSyncReason.Reconnect, Ready()).ShouldRun);
            scheduler.End(AutoSyncFinish.Succeeded, suppressFollowUp: true);
        }
    }

    [Fact]
    public void Resolve_BlocksAutoAndDoesNotFollowImmediately()
    {
        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        Assert.True(scheduler.TryBeginExclusive());
        Assert.Equal(AutoSyncBlock.InFlight, scheduler.Request(AutoSyncReason.Foreground, Ready()).Block);

        scheduler.End(AutoSyncFinish.Succeeded, suppressFollowUp: true);

        Assert.False(scheduler.HasPending);
        Assert.False(scheduler.FlushPending(Ready()).ShouldRun);
    }

    [Fact]
    public void Manual_BypassesCooldown()
    {
        var now = TimeSpan.Zero;
        var scheduler = new AutoSyncScheduler(() => now);
        Assert.True(scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun);
        scheduler.End(AutoSyncFinish.Succeeded, suppressFollowUp: true);
        now += TimeSpan.FromSeconds(1);

        Assert.True(scheduler.TryBeginExclusive());
        Assert.False(scheduler.Request(AutoSyncReason.Foreground, Ready()).ShouldRun);
    }

    [Fact]
    public void CallerCancellation_IsNotTimeoutAndDoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var exception = new OperationCanceledException(cts.Token);

        Assert.True(AutoSyncPolicy.IsCallerCancellation(exception, cts.Token));
        Assert.False(AutoSyncPolicy.IsCallerCancellation(new TimeoutException(), cts.Token));
        Assert.NotEqual(AutoSyncFinish.TransientFailure, AutoSyncFinish.Cancelled);

        var scheduler = new AutoSyncScheduler(() => TimeSpan.Zero);
        Assert.True(scheduler.TryBeginExclusive());
        scheduler.End(AutoSyncFinish.Cancelled, suppressFollowUp: true);

        Assert.False(scheduler.HasPending);
        Assert.False(scheduler.IsInFlight);
    }
}
