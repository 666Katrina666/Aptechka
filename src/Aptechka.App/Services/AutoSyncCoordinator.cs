using Aptechka.App.ViewModels;
using Aptechka.Application.Sync;

namespace Aptechka.App.Services;

public sealed class AutoSyncCoordinator
{
    private readonly AutoSyncScheduler scheduler;
    private readonly MainViewModel viewModel;
    private readonly AppSyncLifetime lifetime;
    private readonly List<Window> windows = [];
    private bool connectivityAttached;
    private bool mainPageSafe;
    private bool initialCatalog;
    private bool stopped;

    public AutoSyncCoordinator(
        AutoSyncScheduler scheduler,
        MainViewModel viewModel,
        AppSyncLifetime lifetime)
    {
        this.scheduler = scheduler;
        this.viewModel = viewModel;
        this.lifetime = lifetime;
        viewModel.SyncOperationFinished += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() => _ = FlushAsync());
    }

    public void Attach(Window window)
    {
        if (stopped)
        {
            return;
        }

        EnsureConnectivity();
        window.Activated += OnForeground;
        window.Resumed += OnForeground;
        window.Destroying += OnDestroying;
        windows.Add(window);
        scheduler.ObserveInternet(HasInternet());
    }

    public void NotifyInitialCatalog()
    {
        if (stopped || initialCatalog)
        {
            return;
        }

        initialCatalog = true;
        Request(AutoSyncReason.Foreground);
    }

    public void NotifyMainPageSafe(bool safe)
    {
        mainPageSafe = safe;
        if (safe)
        {
            _ = FlushAsync();
        }
    }

    private void OnForeground(object? sender, EventArgs e) => Request(AutoSyncReason.Foreground);

    private void OnDestroying(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Activated -= OnForeground;
            window.Resumed -= OnForeground;
            window.Destroying -= OnDestroying;
            windows.Remove(window);
        }

        if (windows.Count > 0)
        {
            return;
        }

        stopped = true;
        if (connectivityAttached)
        {
            Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
            connectivityAttached = false;
        }

        lifetime.Cancel();
    }

    private void EnsureConnectivity()
    {
        if (connectivityAttached)
        {
            return;
        }

        connectivityAttached = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var internet = e.NetworkAccess == NetworkAccess.Internet;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (stopped || !scheduler.ObserveInternet(internet))
            {
                return;
            }

            Request(AutoSyncReason.Reconnect);
        });
    }

    private void Request(AutoSyncReason reason)
    {
        if (stopped)
        {
            return;
        }

        _ = ApplyAsync(scheduler.Request(reason, Readiness()));
    }

    private async Task FlushAsync()
    {
        if (stopped)
        {
            return;
        }

        await ApplyAsync(scheduler.FlushPending(Readiness()));
    }

    private async Task ApplyAsync(AutoSyncDecision decision)
    {
        if (stopped)
        {
            return;
        }

        if (decision.ShowOffline)
        {
            viewModel.ShowAutomaticOffline();
        }

        if (!decision.ShouldRun)
        {
            return;
        }

        if (!scheduler.TryKeepAutomaticClaim(Readiness()))
        {
            if (!Readiness().HasInternet)
            {
                viewModel.ShowAutomaticOffline();
            }

            return;
        }

        await viewModel.RunAutomaticAsync(decision.Reason, StillSafe, lifetime.Token);
    }

    private bool StillSafe() =>
        mainPageSafe && !stopped && !lifetime.Token.IsCancellationRequested;

    private AutoSyncReadiness Readiness() => new(
        viewModel.HasSavedToken,
        viewModel.HasSavedSyncTarget,
        HasInternet(),
        mainPageSafe,
        viewModel.HasUnresolvedConflict);

    private static bool HasInternet() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
