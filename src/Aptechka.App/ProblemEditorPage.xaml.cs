using System.ComponentModel;
using Aptechka.App.ViewModels;
using Aptechka.Domain.Inventory;

namespace Aptechka.App;

public partial class ProblemEditorPage : ContentPage
{
    private readonly ProblemEditorViewModel viewModel;
    private int completionHandled;
    private int navigationInProgress;
    private int handlersReleased;

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateBackButton();
    }

    protected override bool OnBackButtonPressed() =>
        ShouldBlockBack || base.OnBackButtonPressed();

    private bool ShouldBlockBack =>
        viewModel.IsBusy || Volatile.Read(ref navigationInProgress) != 0;

    private bool IsTop() =>
        ReferenceEquals(Navigation?.NavigationStack.LastOrDefault(), this);

    private async void OnCompleted(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref completionHandled, 1) != 0)
        {
            return;
        }

        if (!IsTop())
        {
            return;
        }

        Interlocked.Exchange(ref navigationInProgress, 1);
        UpdateBackButton();
        try
        {
            await Navigation.PopAsync();
        }
        catch (Exception)
        {
        }
        finally
        {
            Interlocked.Exchange(ref navigationInProgress, 0);
            UpdateBackButton();
            TryReleaseHandlers();
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
        if (!IsTop() || ShouldBlockBack)
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
        if (!viewModel.CanArchive || ShouldBlockBack)
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "Архивировать проблему?",
            "Проблема исчезнет из списка. Связанные позиции не изменятся.",
            "Архивировать",
            "Отмена");
        if (confirmed && IsTop() && !ShouldBlockBack)
        {
            await viewModel.ArchiveAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProblemEditorViewModel.IsBusy) or null)
        {
            UpdateBackButton();
            TryReleaseHandlers();
        }
    }

    private void UpdateBackButton() =>
        NavigationPage.SetHasBackButton(this, !ShouldBlockBack);

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (viewModel.IsBusy || Volatile.Read(ref navigationInProgress) != 0)
        {
            return;
        }

        ReleaseHandlers();
    }

    private void TryReleaseHandlers()
    {
        if (Volatile.Read(ref completionHandled) == 0 ||
            viewModel.IsBusy ||
            Volatile.Read(ref navigationInProgress) != 0)
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
        viewModel.Completed -= OnCompleted;
        viewModel.NameConflictDetected -= OnNameConflictDetected;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
