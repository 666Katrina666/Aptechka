using Aptechka.App.ViewModels;
using Aptechka.Application.Sync;

namespace Aptechka.App;

public partial class ConflictResolutionPage : ContentPage
{
    private readonly ConflictResolutionViewModel viewModel;

    public ConflictResolutionPage(ConflictResolutionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.Completed += OnCompleted;
    }

    public event EventHandler<SyncResult>? Resolved;

    public Task InitializeAsync(ConflictResolutionRequest request) =>
        viewModel.InitializeAsync(request);

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (!viewModel.CanApply)
        {
            return;
        }

        if (viewModel.HasDestructiveSelection)
        {
            var confirmed = await DisplayAlert(
                "Применить решения?",
                "Некоторые выбранные версии архивируют или удаляют данные. Применить решения?",
                "Применить",
                "Отмена");
            if (!confirmed)
            {
                return;
            }
        }

        await viewModel.ApplyAsync();
    }

    private void OnCompleted(object? sender, SyncResult result) =>
        Resolved?.Invoke(this, result);
}
