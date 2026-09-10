using Aptechka.App.ViewModels;

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

    private async Task OpenEditorAsync(string? itemId)
    {
        var page = services.GetRequiredService<ItemEditorPage>();
        await page.InitializeAsync(itemId);
        await Navigation.PushAsync(page);
    }
}
