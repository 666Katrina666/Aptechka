using Aptechka.App.ViewModels;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

public partial class ItemEditorPage : ContentPage
{
    private readonly ItemEditorViewModel viewModel;
    private readonly IServiceProvider services;
    private bool appeared;

    public ItemEditorPage(ItemEditorViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.services = services;
        viewModel.Completed += OnCompleted;
        viewModel.NameConflictDetected += OnNameConflictDetected;
    }

    public Task InitializeAsync(string? itemId) => viewModel.LoadAsync(itemId);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!appeared)
        {
            appeared = true;
            return;
        }

        await viewModel.RefreshPackagesAsync();
    }

    private async void OnCompleted(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnNameConflictDetected(object? sender, IReadOnlyList<InventoryItem> conflicts)
    {
        var openLabels = NameConflictChoice.OpenLabels(conflicts).ToArray();
        var buttons = openLabels.Append(NameConflictChoice.CreateAnyway).ToArray();
        var title = conflicts.Count == 1
            ? $"Уже есть позиция «{conflicts[0].Name}»."
            : $"Найдено позиций с таким названием: {conflicts.Count}.";

        var action = await DisplayActionSheet(title, "Отмена", null, buttons);
        var openedItemId = NameConflictChoice.ResolveOpenedItemId(conflicts, action);
        if (openedItemId is not null)
        {
            await viewModel.LoadAsync(openedItemId);
        }
        else if (action == NameConflictChoice.CreateAnyway)
        {
            await viewModel.CreateDespiteNameConflictAsync();
        }
    }

    private async void OnArchiveClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Архивировать позицию?",
            "Позиция исчезнет из каталога.",
            "Архивировать",
            "Отмена");
        if (confirmed)
        {
            await viewModel.ArchiveAsync();
        }
    }

    private async void OnAddPackageClicked(object? sender, EventArgs e)
    {
        if (!viewModel.CanMutatePackages)
        {
            return;
        }

        await OpenPackageEditorAsync(null);
    }

    private async void OnPackageTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PackageListRow row)
        {
            await OpenPackageEditorAsync(row.Id);
        }
    }

    private async void OnMarkLowClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PackageListRow row)
        {
            await viewModel.MarkLowAsync(row.Id);
        }
    }

    private async void OnMarkDepletedClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PackageListRow row)
        {
            await viewModel.MarkDepletedAsync(row.Id);
        }
    }

    private async Task OpenPackageEditorAsync(string? packageId)
    {
        if (viewModel.ItemId is null)
        {
            return;
        }

        var page = services.GetRequiredService<PackageEditorPage>();
        await page.InitializeAsync(viewModel.ItemId, packageId);
        await Navigation.PushAsync(page);
    }
}
