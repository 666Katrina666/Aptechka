using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed record ProblemListRow(string Id, string Name, string Links, string? Note)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
}

public sealed class ProblemsViewModel : INotifyPropertyChanged
{
    private readonly ProblemService problemService;
    private int refreshEpoch;
    private string searchQuery = string.Empty;
    private IReadOnlyList<ProblemListRow> items = [];

    public ProblemsViewModel(ProblemService problemService)
    {
        this.problemService = problemService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (!SetField(ref searchQuery, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EmptyMessage));
            _ = RefreshAsync();
        }
    }

    public IReadOnlyList<ProblemListRow> Items
    {
        get => items;
        private set
        {
            if (!SetField(ref items, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyMessage));
        }
    }

    public bool IsEmpty => Items.Count == 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchQuery)
        ? "Проблем пока нет. Нажми «Добавить», чтобы создать первую."
        : "Ничего не найдено.";

    public Task LoadAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        var epoch = Interlocked.Increment(ref refreshEpoch);
        try
        {
            var catalog = await problemService.SearchAsync(SearchQuery);
            if (epoch != refreshEpoch)
            {
                return;
            }

            Items = catalog.Select(ToRow).ToArray();
        }
        catch (Exception)
        {
            if (epoch != refreshEpoch)
            {
                return;
            }

            Items = [];
        }
    }

    private static ProblemListRow ToRow(Problem problem) =>
        new(
            problem.Id,
            problem.Name,
            LinkedCount(problem.ItemIds.Count),
            string.IsNullOrEmpty(problem.Note) ? null : problem.Note);

    private static string LinkedCount(int count)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;
        var form = n is >= 11 and <= 14
            ? "связанных позиций"
            : n1 == 1
                ? "связанная позиция"
                : n1 is 2 or 3 or 4
                    ? "связанные позиции"
                    : "связанных позиций";
        return $"{count} {form}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
