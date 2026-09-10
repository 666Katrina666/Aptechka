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
        var match = conflicts[0];
        var details = string.Join(
            ", ",
            new[]
            {
                match.Category == InventoryItemCategory.Medicine ? "лекарство" : "медицинский расходник",
                match.Form,
                match.Strength,
            }.Where(static value => !string.IsNullOrEmpty(value)));
        var title = conflicts.Count == 1
            ? $"Уже есть позиция «{match.Name}»."
            : $"Найдено позиций с таким названием: {conflicts.Count}.";
        var message = string.IsNullOrEmpty(details)
            ? "Открыть существующую или создать ещё одну?"
            : $"{details}. Открыть существующую или создать ещё одну?";

        var action = await DisplayActionSheet(
            $"{title} {message}",
            "Отмена",
            null,
            "Открыть существующую",
            "Всё равно создать");

        if (action == "Открыть существующую")
        {
            await viewModel.LoadAsync(match.Id);
        }
        else if (action == "Всё равно создать")
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
