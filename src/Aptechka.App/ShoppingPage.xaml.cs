using System.ComponentModel;
using Aptechka.App.ViewModels;

namespace Aptechka.App;

public partial class ShoppingPage : ContentPage
{
    private const string OpenItem = "Открыть позицию";
    private const string ClearManual = "Снять ручную отметку";
    private const string AddManual = "Добавить ручную отметку";
    private const string EditNote = "Изменить заметку";
    private const string Restore = "Вернуть в покупки";

    private readonly ShoppingViewModel viewModel;
    private readonly IServiceProvider services;
    private int handlersReleased;

    public ShoppingPage(ShoppingViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.services = services;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
        UpdateToolbar();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (viewModel.IsBusy)
        {
            return true;
        }

        if (viewModel.IsAdding)
        {
            viewModel.CancelAddMode();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnAddOrCancelClicked(object? sender, EventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }

        if (viewModel.IsAdding)
        {
            viewModel.CancelAddMode();
            return;
        }

        viewModel.EnterAddMode();
    }

    private async void OnActiveSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        await HandleRowAsync(sender, e.CurrentSelection.FirstOrDefault() as ShoppingRow, recent: false);

    private async void OnRecentSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        await HandleRowAsync(sender, e.CurrentSelection.FirstOrDefault() as ShoppingRow, recent: true);

    private async void OnCandidateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var row = e.CurrentSelection.FirstOrDefault() as ShoppingCandidateRow;
        ClearSelection(sender);
        if (row is null || viewModel.IsBusy || !IsTop())
        {
            return;
        }

        await viewModel.AddManualAsync(row.ItemId);
    }

    private async Task HandleRowAsync(object? sender, ShoppingRow? row, bool recent)
    {
        ClearSelection(sender);
        if (row is null || viewModel.IsBusy || !IsTop())
        {
            return;
        }

        var buttons = recent ? RecentActions(row) : ActiveActions(row);
        var action = await DisplayActionSheet(row.Name, "Отмена", null, buttons);
        if (!IsTop() || viewModel.IsBusy || string.IsNullOrEmpty(action) || action == "Отмена")
        {
            return;
        }

        if (action == OpenItem)
        {
            await OpenItemAsync(row.ItemId);
            return;
        }

        if (action is Restore or AddManual)
        {
            await viewModel.AddManualAsync(row.ItemId);
            return;
        }

        if (action == ClearManual)
        {
            var result = await viewModel.ClearManualAsync(row.ItemId);
            if (result == ShoppingClearManualResult.RemainsDueToKeepInStock && IsTop())
            {
                await DisplayAlert(
                    "Покупки",
                    "Ручная отметка снята. Позиция останется в покупках, пока в аптечке не появится пригодная упаковка.",
                    "ОК");
            }

            return;
        }

        if (action == EditNote)
        {
            await EditNoteAsync(row);
        }
    }

    private async Task EditNoteAsync(ShoppingRow row)
    {
        if (!row.HasShoppingRecord)
        {
            return;
        }

        var note = await DisplayPromptAsync(
            "Заметка",
            "Короткая личная заметка к покупке.",
            "Сохранить",
            "Отмена",
            "Например, какая аптека",
            -1,
            Keyboard.Text,
            row.Note ?? string.Empty);
        if (note is null || !IsTop() || viewModel.IsBusy)
        {
            return;
        }

        await viewModel.UpdateNoteAsync(row.ItemId, note);
    }

    private async Task OpenItemAsync(string itemId)
    {
        if (!IsTop())
        {
            return;
        }

        var page = services.GetRequiredService<ItemEditorPage>();
        await page.InitializeAsync(itemId);
        if (!IsTop())
        {
            return;
        }

        await Navigation.PushAsync(page);
    }

    private static string[] ActiveActions(ShoppingRow row)
    {
        var buttons = new List<string> { OpenItem, row.HasManualReason ? ClearManual : AddManual };
        if (row.HasShoppingRecord)
        {
            buttons.Add(EditNote);
        }

        return buttons.ToArray();
    }

    private static string[] RecentActions(ShoppingRow row) =>
        [Restore, OpenItem, EditNote];

    private static void ClearSelection(object? sender)
    {
        if (sender is CollectionView list)
        {
            list.SelectedItem = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShoppingViewModel.IsAdding)
            or nameof(ShoppingViewModel.IsBusy)
            or null)
        {
            UpdateToolbar();
        }
    }

    private void UpdateToolbar()
    {
        ToolbarItems[0].Text = viewModel.IsAdding ? "Отмена" : "Добавить";
        ToolbarItems[0].IsEnabled = !viewModel.IsBusy;
        NavigationPage.SetHasBackButton(this, !viewModel.IsBusy);
    }

    private bool IsTop() =>
        ReferenceEquals(Navigation?.NavigationStack.LastOrDefault(), this);

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Navigation?.NavigationStack.Contains(this) == true)
        {
            return;
        }

        ReleaseHandlers();
    }

    private void ReleaseHandlers()
    {
        if (Interlocked.Exchange(ref handlersReleased, 1) != 0)
        {
            return;
        }

        Unloaded -= OnUnloaded;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
