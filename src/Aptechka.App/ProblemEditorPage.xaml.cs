using System.ComponentModel;
using Aptechka.App.ViewModels;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

public partial class ProblemEditorPage : ContentPage
{
    private readonly ProblemEditorViewModel viewModel;
    private int completed;

    public ProblemEditorPage(ProblemEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.Completed += OnCompleted;
        viewModel.NameConflictDetected += OnNameConflictDetected;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
        NavigationPage.SetHasBackButton(this, true);
    }

    public Task InitializeAsync(string? problemId) => viewModel.LoadAsync(problemId);

    protected override bool OnBackButtonPressed() =>
        viewModel.IsBusy || IsFinishing || base.OnBackButtonPressed();

    private bool IsFinishing => Volatile.Read(ref completed) != 0;

    private bool IsCurrentPage() =>
        !IsFinishing && ReferenceEquals(Navigation?.NavigationStack.LastOrDefault(), this);

    private async void OnCompleted(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        Detach();
        UpdateBackButton();
        if (!ReferenceEquals(Navigation?.NavigationStack.LastOrDefault(), this))
        {
            return;
        }

        try
        {
            await Navigation.PopAsync();
        }
        catch (Exception)
        {
        }
    }

    private async void OnNameConflictDetected(object? sender, IReadOnlyList<Problem> conflicts)
    {
        var openLabels = ProblemNameConflictChoice.OpenLabels(conflicts).ToArray();
        var buttons = openLabels.Append(ProblemNameConflictChoice.CreateAnyway).ToArray();
        var title = conflicts.Count == 1
            ? $"Уже есть проблема «{conflicts[0].Name}»."
            : $"Найдено проблем с таким названием: {conflicts.Count}.";

        var action = await DisplayActionSheet(title, "Отмена", null, buttons);
        if (!IsCurrentPage())
        {
            return;
        }

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
        if (!viewModel.CanArchive || IsFinishing)
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "Архивировать проблему?",
            "Проблема исчезнет из списка. Связанные позиции не изменятся.",
            "Архивировать",
            "Отмена");
        if (confirmed && IsCurrentPage())
        {
            await viewModel.ArchiveAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProblemEditorViewModel.IsBusy) or null)
        {
            UpdateBackButton();
        }
    }

    private void UpdateBackButton() =>
        NavigationPage.SetHasBackButton(this, !viewModel.IsBusy && !IsFinishing);

    private void OnUnloaded(object? sender, EventArgs e) => Detach();

    private void Detach()
    {
        Unloaded -= OnUnloaded;
        viewModel.Completed -= OnCompleted;
        viewModel.NameConflictDetected -= OnNameConflictDetected;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
