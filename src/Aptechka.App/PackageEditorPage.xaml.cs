using Aptechka.App.ViewModels;

namespace Aptechka.App;

public partial class PackageEditorPage : ContentPage
{
    private readonly PackageEditorViewModel viewModel;

    public PackageEditorPage(PackageEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.Completed += OnCompleted;
    }

    public Task InitializeAsync(string itemId, string? packageId) =>
        viewModel.LoadAsync(itemId, packageId);

    private async void OnCompleted(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnArchiveClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Архивировать упаковку?",
            "Упаковка исчезнет из списка позиции.",
            "Архивировать",
            "Отмена");
        if (confirmed)
        {
            await viewModel.ConfirmArchiveAsync();
        }
    }
}
