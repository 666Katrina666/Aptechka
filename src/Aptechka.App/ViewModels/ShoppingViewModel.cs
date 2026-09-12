using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public enum ShoppingClearManualResult
{
    Ignored,
    Closed,
    RemainsDueToKeepInStock,
    Failed,
}

public sealed record ShoppingRow(
    string ItemId,
    string Name,
    string? Details,
    string Availability,
    string? Note,
    bool HasManualReason,
    bool HasKeepInStockMissingReason,
    bool HasShoppingRecord)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    public bool HasNote => !string.IsNullOrEmpty(Note);
}

public sealed record ShoppingCandidateRow(
    string ItemId,
    string Caption,
    string? Details,
    string Availability)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);
}

public sealed class ShoppingViewModel : INotifyPropertyChanged
{
    private const int RecentlyClosedLimit = 20;
    private const string LoadFailed = "Не удалось загрузить список покупок.";
    private const string ActionFailed = "Не удалось изменить покупку.";

    private readonly ShoppingListService listService;
    private readonly ShoppingService shoppingService;
    private int refreshEpoch;
    private int busy;
    private ShoppingListSnapshot snapshot = new([], [], []);
    private string searchQuery = string.Empty;
    private string candidateSearchQuery = string.Empty;
    private string? errorMessage;
    private bool isAdding;
    private IReadOnlyList<ShoppingRow> active = [];
    private IReadOnlyList<ShoppingRow> recentlyClosed = [];
    private IReadOnlyList<ShoppingCandidateRow> candidates = [];

    public ShoppingViewModel(ShoppingListService listService, ShoppingService shoppingService)
    {
        this.listService = listService;
        this.shoppingService = shoppingService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => IsAdding ? "Добавить покупку" : "Покупки";

    public bool IsAdding
    {
        get => isAdding;
        private set
        {
            if (!SetField(ref isAdding, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsBrowsing));
        }
    }

    public bool IsBrowsing => !IsAdding;

    public bool IsBusy => Volatile.Read(ref busy) != 0;

    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (SetField(ref searchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

    public string CandidateSearchQuery
    {
        get => candidateSearchQuery;
        set
        {
            if (SetField(ref candidateSearchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

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

    public IReadOnlyList<ShoppingRow> Active
    {
        get => active;
        private set
        {
            if (!SetField(ref active, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsActiveEmpty));
            OnPropertyChanged(nameof(ActiveEmptyMessage));
        }
    }

    public IReadOnlyList<ShoppingRow> RecentlyClosed
    {
        get => recentlyClosed;
        private set
        {
            if (!SetField(ref recentlyClosed, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRecentEmpty));
            OnPropertyChanged(nameof(RecentEmptyMessage));
        }
    }

    public IReadOnlyList<ShoppingCandidateRow> Candidates
    {
        get => candidates;
        private set
        {
            if (!SetField(ref candidates, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCandidatesEmpty));
            OnPropertyChanged(nameof(CandidateEmptyMessage));
        }
    }

    public bool IsActiveEmpty => Active.Count == 0;

    public bool IsRecentEmpty => RecentlyClosed.Count == 0;

    public bool IsCandidatesEmpty => Candidates.Count == 0;

    public string ActiveEmptyMessage => HasSearch(SearchQuery)
        ? "Ничего не найдено."
        : "Пока ничего покупать не нужно.";

    public string RecentEmptyMessage => HasSearch(SearchQuery)
        ? "Ничего не найдено."
        : "Недавно закрытых покупок нет.";

    public string CandidateEmptyMessage => HasSearch(CandidateSearchQuery)
        ? "Ничего не найдено."
        : "Нет позиций, которые можно добавить.";

    public Task LoadAsync() => RefreshAsync();

    public void EnterAddMode()
    {
        if (IsBusy || IsAdding)
        {
            return;
        }

        CandidateSearchQuery = string.Empty;
        IsAdding = true;
        ApplyFilter();
    }

    public void CancelAddMode()
    {
        if (!IsAdding)
        {
            return;
        }

        CandidateSearchQuery = string.Empty;
        IsAdding = false;
        ApplyFilter();
    }

    public async Task<bool> AddManualAsync(string itemId)
    {
        if (!TryBeginBusy())
        {
            return false;
        }

        ErrorMessage = null;
        try
        {
            await shoppingService.RequestPurchaseAsync(itemId, ExistingNote(itemId));
            IsAdding = false;
            CandidateSearchQuery = string.Empty;
            await RefreshAsync();
            return true;
        }
        catch (Exception)
        {
            ErrorMessage = ActionFailed;
            return false;
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task<ShoppingClearManualResult> ClearManualAsync(string itemId)
    {
        if (!TryBeginBusy())
        {
            return ShoppingClearManualResult.Ignored;
        }

        ErrorMessage = null;
        try
        {
            await shoppingService.ClearRequestAsync(itemId);
            await RefreshAsync();
            var remaining = snapshot.Active.FirstOrDefault(entry => entry.ItemId == itemId);
            return remaining is { HasKeepInStockMissingReason: true }
                ? ShoppingClearManualResult.RemainsDueToKeepInStock
                : ShoppingClearManualResult.Closed;
        }
        catch (Exception)
        {
            ErrorMessage = ActionFailed;
            return ShoppingClearManualResult.Failed;
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task<bool> UpdateNoteAsync(string itemId, string? note)
    {
        if (!TryBeginBusy())
        {
            return false;
        }

        var current = Find(itemId);
        if (current is not { HasShoppingRecord: true })
        {
            EndBusy();
            return false;
        }

        ErrorMessage = null;
        try
        {
            await shoppingService.UpdateNoteAsync(itemId, note);
            await RefreshAsync();
            return true;
        }
        catch (Exception)
        {
            ErrorMessage = ActionFailed;
            return false;
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task RefreshAsync()
    {
        var epoch = Interlocked.Increment(ref refreshEpoch);
        try
        {
            var loaded = await listService.GetListAsync();
            if (epoch != Volatile.Read(ref refreshEpoch))
            {
                return;
            }

            snapshot = loaded;
            ErrorMessage = null;
            ApplyFilter();
        }
        catch (Exception)
        {
            if (epoch != Volatile.Read(ref refreshEpoch))
            {
                return;
            }

            ErrorMessage = LoadFailed;
        }
    }

    private void ApplyFilter()
    {
        var term = SearchQuery.Trim();
        Active = snapshot.Active
            .Where(entry => Matches(entry, term))
            .Select(ToRow)
            .ToArray();
        RecentlyClosed = snapshot.RecentlyClosed
            .Where(entry => Matches(entry, term))
            .Take(RecentlyClosedLimit)
            .Select(ToRow)
            .ToArray();
        var candidateTerm = CandidateSearchQuery.Trim();
        Candidates = ToCandidates(snapshot.Addable.Where(candidate =>
            Matches(candidate.Name, candidate.Form, candidate.Strength, null, candidateTerm)));
        OnPropertyChanged(nameof(ActiveEmptyMessage));
        OnPropertyChanged(nameof(RecentEmptyMessage));
        OnPropertyChanged(nameof(CandidateEmptyMessage));
    }

    private string? ExistingNote(string itemId) => Find(itemId)?.Note;

    private ShoppingEntry? Find(string itemId) =>
        snapshot.Active.FirstOrDefault(entry => entry.ItemId == itemId)
        ?? snapshot.RecentlyClosed.FirstOrDefault(entry => entry.ItemId == itemId);

    private bool TryBeginBusy()
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        {
            return false;
        }

        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    private void EndBusy()
    {
        Interlocked.Exchange(ref busy, 0);
        OnPropertyChanged(nameof(IsBusy));
    }

    private static ShoppingRow ToRow(ShoppingEntry entry) =>
        new(
            entry.ItemId,
            entry.Name,
            Details(entry.Form, entry.Strength),
            PackageText.Availability(entry.Availability),
            entry.Note,
            entry.HasManualReason,
            entry.HasKeepInStockMissingReason,
            entry.HasShoppingRecord);

    private static IReadOnlyList<ShoppingCandidateRow> ToCandidates(
        IEnumerable<ShoppingCandidate> source)
    {
        var list = source.ToArray();
        var keys = list.Select(static candidate => CaptionKey(candidate)).ToArray();
        var duplicates = keys
            .GroupBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return list.Select((candidate, index) => new ShoppingCandidateRow(
            candidate.ItemId,
            duplicates.Contains(keys[index])
                ? $"{candidate.Name} · {ShortId(candidate.ItemId)}"
                : candidate.Name,
            Details(candidate.Form, candidate.Strength),
            PackageText.Availability(candidate.Availability))).ToArray();
    }

    private static string CaptionKey(ShoppingCandidate candidate) =>
        string.Join('\n', candidate.Name, candidate.Form, candidate.Strength);

    private static string ShortId(string id) => id.Length <= 6 ? id : id[^6..];

    private static string? Details(string? form, string? strength)
    {
        var text = string.Join(
            ", ",
            new[] { form, strength }.Where(static value => !string.IsNullOrEmpty(value)));
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool Matches(ShoppingEntry entry, string term) =>
        Matches(entry.Name, entry.Form, entry.Strength, entry.Note, term);

    private static bool Matches(string? name, string? form, string? strength, string? note, string term) =>
        !HasSearch(term)
        || Contains(name, term)
        || Contains(form, term)
        || Contains(strength, term)
        || Contains(note, term);

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool HasSearch(string? value) => !string.IsNullOrWhiteSpace(value);

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
