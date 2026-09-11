using Aptechka.App.ViewModels;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

public partial class ProblemEditorPage : ContentPage
{
    private readonly ProblemEditorViewModel viewModel;

    public ProblemEditorPage(ProblemEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.Completed += OnCompleted;
        viewModel.NameConflictDetected += OnNameConflictDetected;
    }

    public Task InitializeAsync(string? problemId) => viewModel.LoadAsync(problemId);

    private async void OnCompleted(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnNameConflictDetected(object? sender, IReadOnlyList<Problem> conflicts)
    {
        var openLabels = ProblemNameConflictChoice.OpenLabels(conflicts).ToArray();
        var buttons = openLabels.Append(ProblemNameConflictChoice.CreateAnyway).ToArray();
        var title = conflicts.Count == 1
            ? $"Уже есть проблема «{conflicts[0].Name}»."
            : $"Найдено проблем с таким названием: {conflicts.Count}.";

        var action = await DisplayActionSheet(title, "Отмена", null, buttons);
        var openedProblemId = ProblemNameConflictChoice.ResolveOpenedProblemId(conflicts, action);
        if (openedProblemId is not null)
        {
            await viewModel.LoadAsync(openedProblemId);
        }
        else if (action == ProblemNameConflictChoice.CreateAnyway)
        {
            await viewModel.CreateDespiteNameConflictAsync();
        }
    }

    private async void OnArchiveClicked(object? sender, EventArgs e)
    {
        if (!viewModel.CanArchive)
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "Архивировать проблему?",
            "Проблема исчезнет из списка. Связанные позиции не изменятся.",
            "Архивировать",
            "Отмена");
        if (confirmed)
        {
            await viewModel.ArchiveAsync();
        }
    }
}
