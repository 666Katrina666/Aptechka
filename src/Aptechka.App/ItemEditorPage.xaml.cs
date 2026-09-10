using Aptechka.App.ViewModels;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

public partial class ItemEditorPage : ContentPage
{
    private readonly ItemEditorViewModel viewModel;

    public ItemEditorPage(ItemEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.Completed += OnCompleted;
        viewModel.NameConflictDetected += OnNameConflictDetected;
    }

    public Task InitializeAsync(string? itemId) => viewModel.LoadAsync(itemId);

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
}
