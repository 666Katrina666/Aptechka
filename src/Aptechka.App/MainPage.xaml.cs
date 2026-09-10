using Aptechka.App.ViewModels;
using Aptechka.Application.Sync;

namespace Aptechka.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel viewModel;
    private readonly IServiceProvider services;

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
        EventHandler<SyncResult>? resolved = null;
        EventHandler? unloaded = null;
        resolved = async (_, result) =>
        {
            page.Resolved -= resolved;
            await Navigation.PopAsync();
            await viewModel.CompleteConflictResolutionAsync(result);
        };
        unloaded = (_, _) =>
        {
            page.Unloaded -= unloaded;
            page.Resolved -= resolved;
        };
        page.Resolved += resolved;
        page.Unloaded += unloaded;
        await Navigation.PushAsync(page);
    }

    private async Task OpenEditorAsync(string? itemId)
    {
        var page = services.GetRequiredService<ItemEditorPage>();
        await page.InitializeAsync(itemId);
        await Navigation.PushAsync(page);
    }
}
