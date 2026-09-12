using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.App.Services;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed record ConflictResolutionRequest(
    SyncTarget Target,
    string DeviceName,
    IReadOnlyList<SyncConflict> Conflicts);

public sealed class ConflictChoiceRow : INotifyPropertyChanged
{
    private SyncConflictSide? selectedSide;
    private readonly Action changed;

    public ConflictChoiceRow(
        SyncConflict conflict,
        string title,
        string fieldCaption,
        string localValue,
        string remoteValue,
        Action changed)
    {
        Conflict = conflict;
        Title = title;
        FieldCaption = fieldCaption;
        LocalValue = localValue;
        RemoteValue = remoteValue;
        this.changed = changed;
        SelectLocalCommand = new Command(() => SelectedSide = SyncConflictSide.Local);
        SelectRemoteCommand = new Command(() => SelectedSide = SyncConflictSide.Remote);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public SyncConflict Conflict { get; }
    public string Title { get; }
    public string FieldCaption { get; }
    public string LocalValue { get; }
    public string RemoteValue { get; }
    public ICommand SelectLocalCommand { get; }
    public ICommand SelectRemoteCommand { get; }
    public bool IsLocalSelected => SelectedSide == SyncConflictSide.Local;
    public bool IsRemoteSelected => SelectedSide == SyncConflictSide.Remote;

    public SyncConflictSide? SelectedSide
    {
        get => selectedSide;
        private set
        {
            if (selectedSide == value)
            {
                return;
            }

            selectedSide = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalSelected));
            OnPropertyChanged(nameof(IsRemoteSelected));
            changed();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ConflictResolutionViewModel : INotifyPropertyChanged
{
    private readonly ISyncService syncService;
    private readonly ISecureTokenStore tokenStore;
    private readonly InventoryService inventoryService;
    private readonly PackageService packageService;
    private readonly ProblemService problemService;
    private readonly AutoSyncScheduler scheduler;
    private readonly AppSyncLifetime syncLifetime;
    private SyncTarget? target;
    private string deviceName = string.Empty;
    private IReadOnlyList<ConflictChoiceRow> rows = [];
    private string status = string.Empty;
    private bool isBusy;

    public ConflictResolutionViewModel(
        ISyncService syncService,
        ISecureTokenStore tokenStore,
        InventoryService inventoryService,
        PackageService packageService,
        ProblemService problemService,
        AutoSyncScheduler scheduler,
        AppSyncLifetime syncLifetime)
    {
        this.syncService = syncService;
        this.tokenStore = tokenStore;
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        this.problemService = problemService;
        this.scheduler = scheduler;
        this.syncLifetime = syncLifetime;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<SyncResult>? Completed;

    public event EventHandler<SyncFailureKind>? Failed;

    public IReadOnlyList<ConflictChoiceRow> Rows
    {
        get => rows;
        private set
        {
            if (SetField(ref rows, value))
            {
                OnPropertyChanged(nameof(HasConflicts));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public bool HasConflicts => Rows.Count > 0;

    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool CanApply =>
        !IsBusy &&
        Rows.Count > 0 &&
        Rows.All(static row => row.SelectedSide is not null);

    public bool HasDestructiveSelection =>
        Rows.Any(static row =>
            row.SelectedSide is { } side &&
            ConflictPresentation.IsDestructive(row.Conflict, side));

    public async Task InitializeAsync(ConflictResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        target = request.Target;
        deviceName = request.DeviceName;
        await ReplaceConflictsAsync(request.Conflicts, "Выбери версию для каждого различия.");
    }

    public async Task ApplyAsync()
    {
        if (IsBusy || !CanApply || target is null || !scheduler.TryBeginExclusive())
        {
            if (scheduler.IsInFlight)
            {
                Status = "Синхронизация уже выполняется.";
            }

            return;
        }

        IsBusy = true;
        Status = "Применяем решения…";
        var finish = AutoSyncFinish.PermanentFailure;
        var ended = false;
        try
        {
            var token = await tokenStore.GetTokenAsync() ?? string.Empty;
            if (token.Length == 0)
            {
                Status = SyncStatusText.Detail(SyncFailureKind.Authentication);
                Failed?.Invoke(this, SyncFailureKind.Authentication);
                return;
            }

            var resolutions = Rows
                .Select(static row => new SyncConflictResolution(row.Conflict, row.SelectedSide!.Value))
                .ToArray();
            var result = await syncService.ResolveConflictsAsync(
                target,
                token,
                deviceName,
                resolutions,
                syncLifetime.Token);

            if (result.Outcome is SyncOutcome.Initialized or
                SyncOutcome.Pulled or
                SyncOutcome.Pushed or
                SyncOutcome.UpToDate)
            {
                finish = AutoSyncFinish.Succeeded;
                scheduler.End(finish, suppressFollowUp: true);
                ended = true;
                Completed?.Invoke(this, result);
                return;
            }

            if (result.Outcome == SyncOutcome.Conflict)
            {
                finish = AutoSyncFinish.Conflict;
                scheduler.End(finish, suppressFollowUp: true);
                ended = true;
                await ReplaceConflictsAsync(
                    result.Conflicts,
                    "Данные изменились после открытия экрана. Проверь новые версии ещё раз.");
                return;
            }

            Status = SyncStatusText.Detail(SyncFailureKind.Unknown);
            Failed?.Invoke(this, SyncFailureKind.Unknown);
        }
        catch (OperationCanceledException exception) when (AutoSyncPolicy.IsCallerCancellation(exception, syncLifetime.Token))
        {
            finish = AutoSyncFinish.Cancelled;
        }
        catch (SyncFailureException exception)
        {
            finish = AutoSyncPolicy.FinishFor(exception.Kind);
            Status = SyncStatusText.Detail(exception.Kind);
            Failed?.Invoke(this, exception.Kind);
        }
        catch (Exception)
        {
            Status = SyncStatusText.Detail(SyncFailureKind.Unknown);
            Failed?.Invoke(this, SyncFailureKind.Unknown);
        }
        finally
        {
            if (!ended)
            {
                scheduler.End(finish, suppressFollowUp: true);
            }

            IsBusy = false;
        }
    }

    private async Task ReplaceConflictsAsync(IReadOnlyList<SyncConflict> conflicts, string message)
    {
        var resolvable = conflicts.Where(static conflict => !ConflictPresentation.IsManifest(conflict)).ToArray();
        if (resolvable.Length == 0)
        {
            Rows = [];
            Status = "Этот конфликт нельзя разрешить выбором версии. Вернись назад и повтори синхронизацию.";
            return;
        }

        var itemNames = await LoadItemNamesAsync(resolvable);
        var next = new List<ConflictChoiceRow>(resolvable.Length);
        foreach (var conflict in resolvable)
        {
            var item = await TryGetItemAsync(conflict);
            var (package, packageItem) = await TryGetPackageAsync(conflict);
            var problem = await TryGetProblemAsync(conflict);
            next.Add(new ConflictChoiceRow(
                conflict,
                ConflictPresentation.EntityCaption(conflict, item, package, packageItem, problem),
                ConflictPresentation.FieldCaption(conflict),
                ConflictPresentation.FormatSide(conflict, SyncConflictSide.Local, itemNames),
                ConflictPresentation.FormatSide(conflict, SyncConflictSide.Remote, itemNames),
                OnRowChanged));
        }

        Rows = next;
        Status = message;
    }

    private async Task<InventoryItem?> TryGetItemAsync(SyncConflict conflict)
    {
        if (!ConflictPresentation.TryGetItemId(conflict.Path, out var id) &&
            !ConflictPresentation.TryGetShoppingId(conflict.Path, out id))
        {
            return null;
        }

        return await TryGetAsync(() => inventoryService.GetItemAsync(id));
    }

    private async Task<Problem?> TryGetProblemAsync(SyncConflict conflict) =>
        ConflictPresentation.TryGetProblemId(conflict.Path, out var id)
            ? await TryGetAsync(() => problemService.GetProblemAsync(id))
            : null;

    private async Task<IReadOnlyDictionary<string, string>> LoadItemNamesAsync(
        IReadOnlyList<SyncConflict> conflicts)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in await inventoryService.GetCatalogAsync())
            {
                names[item.Id] = item.Name;
            }
        }
        catch
        {
        }

        foreach (var id in ConflictPresentation.CollectLinkedItemIds(conflicts))
        {
            if (names.ContainsKey(id))
            {
                continue;
            }

            var item = await TryGetAsync(() => inventoryService.GetItemAsync(id));
            if (item is not null)
            {
                names[item.Id] = item.Name;
            }
        }

        return names;
    }

    private async Task<(Package? Package, InventoryItem? Item)> TryGetPackageAsync(SyncConflict conflict)
    {
        if (!ConflictPresentation.TryGetPackageId(conflict.Path, out var id))
        {
            return (null, null);
        }

        var package = await TryGetAsync(() => packageService.GetPackageAsync(id));
        if (package is null)
        {
            return (null, null);
        }

        return (package, await TryGetAsync(() => inventoryService.GetItemAsync(package.ItemId)));
    }

    private static async Task<T?> TryGetAsync<T>(Func<Task<T?>> load)
    {
        try
        {
            return await load();
        }
        catch
        {
            return default;
        }
    }

    private void OnRowChanged() =>
        OnPropertyChanged(nameof(CanApply));

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
