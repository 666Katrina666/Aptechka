using Aptechka.App.ViewModels;
using Aptechka.Application.Sync;

namespace Aptechka.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel viewModel;
    private readonly IServiceProvider services;
    private readonly List<PendingConflictResolution> pendingConflicts = [];

    public MainPage(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.services = services;
        viewModel.ConflictResolutionRequested += OnConflictResolutionRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    private async void OnAddClicked(object? sender, EventArgs e) =>
        await OpenEditorAsync(null);

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CatalogItemRow row)
        {
            return;
        }

        if (sender is CollectionView list)
        {
            list.SelectedItem = null;
        }

        await OpenEditorAsync(row.Id);
    }

    private async void OnConflictResolutionRequested(object? sender, ConflictResolutionRequest request)
    {
        var page = services.GetRequiredService<ConflictResolutionPage>();
        await page.InitializeAsync(request);

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
            pendingConflicts.Remove(pending);
        };

        pendingConflicts.Add(pending);
        page.Resolved += resolved;
        page.Unloaded += unloaded;
        await Navigation.PushAsync(page);
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

    private sealed class PendingConflictResolution(ConflictResolutionPage page)
    {
        private int completed;

        public ConflictResolutionPage Page { get; } = page;

        public bool TryBegin() => Interlocked.Exchange(ref completed, 1) == 0;
    }

    private async Task OpenEditorAsync(string? itemId)
    {
        var page = services.GetRequiredService<ItemEditorPage>();
        await page.InitializeAsync(itemId);
        await Navigation.PushAsync(page);
    }
}
