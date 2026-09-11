using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed class ProblemItemLinkRow : INotifyPropertyChanged
{
    private bool isSelected;
    private readonly Action<string, bool> selectionChanged;

    public ProblemItemLinkRow(
        string id,
        string name,
        string? details,
        string availability,
        bool isArchived,
        bool isMissing,
        bool isSelected,
        Action<string, bool> selectionChanged)
    {
        Id = id;
        Name = name;
        Details = details;
        Availability = availability;
        IsArchived = isArchived;
        IsMissing = isMissing;
        this.isSelected = isSelected;
        this.selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string Name { get; }
    public string? Details { get; }
    public string Availability { get; }
    public bool IsArchived { get; }
    public bool IsMissing { get; }
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            selectionChanged(Id, value);
        }
    }

    public bool Matches(string term) =>
        Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrEmpty(Details) && Details.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
        Id.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public sealed class ProblemEditorViewModel : INotifyPropertyChanged
{
    private readonly ProblemService problemService;
    private readonly InventoryService inventoryService;
    private readonly PackageService packageService;
    private readonly List<string> selectedOrder = [];
    private readonly HashSet<string> selectedIds = new(StringComparer.Ordinal);
    private IReadOnlyList<ProblemItemLinkRow> links = [];
    private string? problemId;
    private string title = "Новая проблема";
    private string name = string.Empty;
    private string aliases = string.Empty;
    private string? note;
    private string itemFilter = string.Empty;
    private string? errorMessage;
    private bool isBusy;
    private bool isEditing;
    private bool createDespiteNameConflict;

    public ProblemEditorViewModel(
        ProblemService problemService,
        InventoryService inventoryService,
        PackageService packageService)
    {
        this.problemService = problemService;
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Completed;

    public event EventHandler<IReadOnlyList<Problem>>? NameConflictDetected;

    public ICommand SaveCommand { get; }

    public string Title
    {
        get => title;
        private set => SetField(ref title, value);
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string Aliases
    {
        get => aliases;
        set => SetField(ref aliases, value);
    }

    public string? Note
    {
        get => note;
        set => SetField(ref note, value);
    }

    public string ItemFilter
    {
        get => itemFilter;
        set
        {
            if (SetField(ref itemFilter, value))
            {
                OnPropertyChanged(nameof(VisibleLinks));
            }
        }
    }

    public IReadOnlyList<ProblemItemLinkRow> VisibleLinks =>
        string.IsNullOrWhiteSpace(ItemFilter)
            ? links
            : links.Where(row => row.Matches(ItemFilter.Trim())).ToArray();

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetField(ref isEditing, value))
            {
                OnPropertyChanged(nameof(CanArchive));
            }
        }
    }

    public bool CanArchive => IsEditing && !IsBusy;

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetField(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanArchive));
            ((Command)SaveCommand).ChangeCanExecute();
        }
    }

    public async Task LoadAsync(string? id)
    {
        ErrorMessage = null;
        createDespiteNameConflict = false;
        problemId = null;
        IsEditing = false;
        Title = "Новая проблема";
        Name = string.Empty;
        Aliases = string.Empty;
        Note = null;
        ItemFilter = string.Empty;
        selectedIds.Clear();
        selectedOrder.Clear();

        IReadOnlyList<string> linked = [];
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var problem = await problemService.GetProblemAsync(id);
                if (problem is null || problem.DeletedAt is not null)
                {
                    ErrorMessage = "Проблема не найдена.";
                    await ReloadLinksAsync([]);
                    return;
                }

                problemId = problem.Id;
                IsEditing = true;
                Title = problem.Name;
                Name = problem.Name;
                Aliases = string.Join(", ", problem.Aliases);
                Note = problem.Note;
                linked = problem.ItemIds;
                foreach (var itemId in linked)
                {
                    if (selectedIds.Add(itemId))
                    {
                        selectedOrder.Add(itemId);
                    }
                }
            }
            catch (Exception)
            {
                ErrorMessage = "Не удалось открыть проблему.";
                await ReloadLinksAsync([]);
                return;
            }
        }

        await ReloadLinksAsync(linked);
    }

    public Task CreateDespiteNameConflictAsync()
    {
        createDespiteNameConflict = true;
        return SaveAsync();
    }

    public async Task ArchiveAsync()
    {
        if (IsBusy || problemId is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await problemService.ArchiveAsync(problemId);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            ErrorMessage = "Не удалось архивировать проблему.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var draft = new ProblemDraft(Name, SplitCsv(Aliases), selectedOrder.ToArray(), Note);
            if (problemId is null)
            {
                if (!createDespiteNameConflict)
                {
                    var conflicts = await FindDuplicatesAsync();
                    if (conflicts.Count > 0)
                    {
                        NameConflictDetected?.Invoke(this, conflicts);
                        return;
                    }
                }

                await problemService.CreateAsync(draft);
            }
            else
            {
                await problemService.UpdateAsync(problemId, draft);
            }

            createDespiteNameConflict = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException exception)
        {
            createDespiteNameConflict = false;
            ErrorMessage = exception.ParamName == "value"
                ? "Название не может быть пустым."
                : "Проверь название, формулировки и связанные позиции.";
        }
        catch (Exception)
        {
            createDespiteNameConflict = false;
            ErrorMessage = "Не удалось сохранить проблему.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IReadOnlyList<Problem>> FindDuplicatesAsync()
    {
        var byId = new Dictionary<string, Problem>(StringComparer.Ordinal);
        foreach (var term in SplitCsv(Aliases).Prepend(Name))
        {
            foreach (var match in await problemService.FindNameMatchesAsync(term))
            {
                byId.TryAdd(match.Id, match);
            }
        }

        return byId.Values
            .OrderBy(static problem => problem.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task ReloadLinksAsync(IReadOnlyList<string> linked)
    {
        var rows = new List<ProblemItemLinkRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in await inventoryService.GetCatalogAsync())
            {
                seen.Add(item.Id);
                rows.Add(await ToLinkAsync(item, isArchived: false, isMissing: false));
            }
        }
        catch (Exception)
        {
            ErrorMessage ??= "Не удалось загрузить позиции аптечки.";
        }

        foreach (var itemId in linked)
        {
            if (!seen.Add(itemId))
            {
                continue;
            }

            InventoryItem? item = null;
            try
            {
                item = await inventoryService.GetItemAsync(itemId);
            }
            catch (Exception)
            {
            }

            if (item is null)
            {
                rows.Add(MissingLink(itemId));
                continue;
            }

            rows.Add(await ToLinkAsync(item, isArchived: item.DeletedAt is not null, isMissing: false));
        }

        links = rows;
        OnPropertyChanged(nameof(VisibleLinks));
    }

    private async Task<ProblemItemLinkRow> ToLinkAsync(InventoryItem item, bool isArchived, bool isMissing)
    {
        var details = string.Join(
            ", ",
            new[] { item.Form, item.Strength }.Where(static value => !string.IsNullOrEmpty(value)));
        var availability = "Наличие не загружено";
        try
        {
            var summary = await packageService.GetItemStockSummaryAsync(item.Id);
            availability = PackageText.Availability(summary.Availability);
        }
        catch (Exception)
        {
        }

        return new ProblemItemLinkRow(
            item.Id,
            item.Name,
            string.IsNullOrEmpty(details) ? null : details,
            availability,
            isArchived,
            isMissing,
            selectedIds.Contains(item.Id),
            OnLinkSelected);
    }

    private ProblemItemLinkRow MissingLink(string itemId) =>
        new(
            itemId,
            $"Позиция отсутствует · {ShortId(itemId)}",
            null,
            "Наличие неизвестно",
            false,
            true,
            selectedIds.Contains(itemId),
            OnLinkSelected);

    private void OnLinkSelected(string id, bool selected)
    {
        if (selected)
        {
            if (selectedIds.Add(id))
            {
                selectedOrder.Add(id);
            }

            return;
        }

        if (selectedIds.Remove(id))
        {
            selectedOrder.Remove(id);
        }
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ShortId(string id) =>
        id.Length <= 6 ? id : id[^6..];

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
