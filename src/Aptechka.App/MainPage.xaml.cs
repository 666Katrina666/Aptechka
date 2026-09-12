using System.ComponentModel;
using Aptechka.App.Services;
using Aptechka.App.ViewModels;
using Aptechka.Application.Sync;

namespace Aptechka.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel viewModel;
    private readonly IServiceProvider services;
    private readonly AutoSyncCoordinator coordinator;
    private readonly List<PendingConflictResolution> pendingConflicts = [];
    private ConflictResolutionRequest? deferredConflict;

    public MainPage(MainViewModel viewModel, IServiceProvider services, AutoSyncCoordinator coordinator)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.services = services;
        this.coordinator = coordinator;
        viewModel.ConflictResolutionRequested += OnConflictResolutionRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
        if (deferredConflict is { } request && IsTop())
        {
            await PresentConflictAsync(request);
        }

        coordinator.NotifyMainPageSafe(IsTop());
        coordinator.NotifyInitialCatalog();
    }

    protected override void OnDisappearing()
    {
        coordinator.NotifyMainPageSafe(false);
        base.OnDisappearing();
    }

    private async void OnShoppingClicked(object? sender, EventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }

        await Navigation.PushAsync(services.GetRequiredService<ShoppingPage>());
    }

    private async void OnProblemsClicked(object? sender, EventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }

        await Navigation.PushAsync(services.GetRequiredService<ProblemsPage>());
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }

        await OpenEditorAsync(null);
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewModel.IsBusy || e.CurrentSelection.FirstOrDefault() is not CatalogItemRow row)
        {
            if (sender is CollectionView busyList)
            {
                busyList.SelectedItem = null;
            }

            return;
        }

        if (sender is CollectionView list)
        {
            list.SelectedItem = null;
        }

        await OpenEditorAsync(row.Id);
    }

    private void OnConflictResolutionRequested(object? sender, ConflictResolutionRequest request) =>
        _ = PresentConflictAsync(request);

    private bool presentingConflict;

    private async Task PresentConflictAsync(ConflictResolutionRequest request)
    {
        if (presentingConflict || pendingConflicts.Count > 0)
        {
            return;
        }

        if (!IsTop())
        {
            deferredConflict = request;
            return;
        }

        presentingConflict = true;
        deferredConflict = null;
        try
        {
            var page = services.GetRequiredService<ConflictResolutionPage>();
            await page.InitializeAsync(request);
            if (!IsTop() || pendingConflicts.Count > 0)
            {
                deferredConflict = request;
                return;
            }

            var pending = new PendingConflictResolution(page);
            EventHandler<SyncResult>? resolved = null;
            EventHandler? unloaded = null;
            resolved = async (_, result) =>
                await FinishConflictResolutionAsync(pending, resolved, unloaded, result);
            unloaded = (_, _) =>
            {
                if (page.IsResolveInFlight)
                {
                    return;
                }

                page.Unloaded -= unloaded;
                page.Resolved -= resolved;
                page.Failed -= OnConflictResolutionFailed;
                pendingConflicts.Remove(pending);
            };

            pendingConflicts.Add(pending);
            page.Resolved += resolved;
            page.Unloaded += unloaded;
            page.Failed += OnConflictResolutionFailed;
            await Navigation.PushAsync(page);
        }
        finally
        {
            presentingConflict = false;
        }
    }

    private async Task FinishConflictResolutionAsync(
        PendingConflictResolution pending,
        EventHandler<SyncResult>? resolved,
        EventHandler? unloaded,
        SyncResult result)
    {
        if (!pending.TryBegin())
        {
            return;
        }

        pending.Page.Resolved -= resolved;
        pending.Page.Unloaded -= unloaded;
        pending.Page.Failed -= OnConflictResolutionFailed;
        pendingConflicts.Remove(pending);

        try
        {
            if (ReferenceEquals(pending.Page.Navigation.NavigationStack.LastOrDefault(), pending.Page))
            {
                await pending.Page.Navigation.PopAsync();
            }
        }
        catch (Exception)
        {
        }

        await viewModel.CompleteConflictResolutionAsync(result);
    }

    private void OnConflictResolutionFailed(object? sender, SyncFailureKind kind) =>
        viewModel.ShowSyncFailure(kind);

    private sealed class PendingConflictResolution(ConflictResolutionPage page)
    {
        private int completed;

        public ConflictResolutionPage Page { get; } = page;

        public bool TryBegin() => Interlocked.Exchange(ref completed, 1) == 0;
    }

    private async Task OpenEditorAsync(string? itemId)
    {
        if (viewModel.IsBusy)
        {
            return;
        }

        var page = services.GetRequiredService<ItemEditorPage>();
        await page.InitializeAsync(itemId);
        await Navigation.PushAsync(page);
    }

    private bool IsTop() => ReferenceEquals(Navigation?.NavigationStack.LastOrDefault(), this);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsBusy) or null)
        {
            foreach (var item in ToolbarItems)
            {
                item.IsEnabled = !viewModel.IsBusy;
            }
        }
    }
}
