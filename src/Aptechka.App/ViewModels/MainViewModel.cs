using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.App.Services;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed record CatalogItemRow(
    string Id,
    string Name,
    string Category,
    string? Details,
    bool KeepInStock,
    string Availability,
    string? UsablePackages,
    string? NearestExpiration,
    string? ExpiredWarning)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    public bool HasUsablePackages => !string.IsNullOrEmpty(UsablePackages);

    public bool HasNearestExpiration => !string.IsNullOrEmpty(NearestExpiration);

    public bool HasExpiredWarning => !string.IsNullOrEmpty(ExpiredWarning);
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string OwnerPreference = "sync-owner";
    private const string RepositoryPreference = "sync-repository";
    private const string BranchPreference = "sync-branch";

    private readonly InventoryService inventoryService;
    private readonly PackageService packageService;
    private readonly ISyncService syncService;
    private readonly ISyncStateInspector syncStateInspector;
    private readonly ISecureTokenStore tokenStore;
    private readonly AutoSyncScheduler scheduler;
    private readonly AppSyncLifetime syncLifetime;
    private int refreshEpoch;
    private string searchQuery = string.Empty;
    private IReadOnlyList<CatalogItemRow> items = [];
    private string owner;
    private string repository;
    private string branch;
    private string tokenInput = string.Empty;
    private bool hasSavedToken;
    private bool pinSessionStatus;
    private bool gitHubSettingsInitialized;
    private bool areGitHubSettingsExpanded = true;
    private SyncUiState syncState = SyncUiState.NotConfigured;
    private string syncHeadline = SyncStatusText.Headline(SyncUiState.NotConfigured);
    private string syncDetail = "Вставь GitHub-токен, чтобы синхронизировать аптечку.";
    private string? lastSuccessfulText;
    private bool isBusy;
    private SyncFailureKind? lastFailure;

    public MainViewModel(
        InventoryService inventoryService,
        PackageService packageService,
        ISyncService syncService,
        ISyncStateInspector syncStateInspector,
        ISecureTokenStore tokenStore,
        AutoSyncScheduler scheduler,
        AppSyncLifetime syncLifetime)
    {
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        this.syncService = syncService;
        this.syncStateInspector = syncStateInspector;
        this.tokenStore = tokenStore;
        this.scheduler = scheduler;
        this.syncLifetime = syncLifetime;
        scheduler.BusyChanged += () => SetBusy(scheduler.IsInFlight);

        owner = Preferences.Default.Get(OwnerPreference, "666Katrina666");
        repository = Preferences.Default.Get(RepositoryPreference, "aptechka-data");
        branch = Preferences.Default.Get(BranchPreference, "main");

        SyncCommand = new Command(async () => await SyncAsync(), () => !IsBusy);
        ToggleGitHubSettingsCommand = new Command(ToggleGitHubSettings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ConflictResolutionRequest>? ConflictResolutionRequested;

    public event EventHandler? SyncOperationFinished;

    public ICommand SyncCommand { get; }

    public ICommand ToggleGitHubSettingsCommand { get; }

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
            _ = RefreshItemsAsync();
        }
    }

    public IReadOnlyList<CatalogItemRow> Items
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
        ? "Позиций пока нет. Нажми «Добавить», чтобы создать первую."
        : "Ничего не найдено.";

    public string Owner
    {
        get => owner;
        set => SetField(ref owner, value);
    }

    public string Repository
    {
        get => repository;
        set => SetField(ref repository, value);
    }

    public string Branch
    {
        get => branch;
        set => SetField(ref branch, value);
    }

    public string TokenInput
    {
        get => tokenInput;
        set => SetField(ref tokenInput, value);
    }

    public bool HasSavedToken
    {
        get => hasSavedToken;
        private set
        {
            if (SetField(ref hasSavedToken, value))
            {
                OnPropertyChanged(nameof(TokenStatus));
            }
        }
    }

    public string TokenStatus => HasSavedToken
        ? "Токен сохранён в Secure Storage. Поле можно оставить пустым."
        : "Введи repository-scoped GitHub token с Contents: Read and write.";

    public string SyncHeadline
    {
        get => syncHeadline;
        private set => SetField(ref syncHeadline, value);
    }

    public string SyncDetail
    {
        get => syncDetail;
        private set => SetField(ref syncDetail, value);
    }

    public string? LastSuccessfulText
    {
        get => lastSuccessfulText;
        private set
        {
            if (SetField(ref lastSuccessfulText, value))
            {
                OnPropertyChanged(nameof(HasLastSuccessful));
            }
        }
    }

    public bool HasLastSuccessful => !string.IsNullOrEmpty(LastSuccessfulText);

    public bool IsSynced => syncState == SyncUiState.Synced;

    public bool IsLocalChanges => syncState == SyncUiState.LocalChanges;

    public bool IsSyncing => syncState == SyncUiState.Syncing;

    public bool IsConflict => syncState == SyncUiState.Conflict;

    public bool IsError => syncState == SyncUiState.Error;

    public bool HasUnresolvedConflict => syncState == SyncUiState.Conflict;

    public bool AreGitHubSettingsExpanded
    {
        get => areGitHubSettingsExpanded;
        private set => SetField(ref areGitHubSettingsExpanded, value);
    }

    public bool HasSavedSyncTarget
    {
        get
        {
            try
            {
                ReadSavedTarget().EnsureValid();
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
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

            ((Command)SyncCommand).ChangeCanExecute();
        }
    }

    private void SetBusy(bool value)
    {
        if (MainThread.IsMainThread)
        {
            IsBusy = value;
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => IsBusy = scheduler.IsInFlight);
    }

    public void ShowAutomaticOffline()
    {
        if (syncState == SyncUiState.Conflict ||
            (pinSessionStatus && syncState == SyncUiState.Error && lastFailure is not null and not SyncFailureKind.Offline))
        {
            return;
        }

        ShowFailure(SyncFailureKind.Offline);
    }

    public Task RunAutomaticAsync(AutoSyncReason reason, Func<bool> stillSafe, CancellationToken cancellationToken) =>
        ExecuteSyncAsync(saveSettings: false, reason, stillSafe, cancellationToken);

    public async Task LoadAsync()
    {
        var tokenStoreFailed = false;
        try
        {
            HasSavedToken = await tokenStore.HasTokenAsync();
        }
        catch (Exception)
        {
            HasSavedToken = false;
            ShowFailure(SyncFailureKind.LocalStorage);
            tokenStoreFailed = true;
        }

        await RefreshItemsAsync();
        InitializeGitHubSettingsVisibility();
        if (tokenStoreFailed || pinSessionStatus)
        {
            return;
        }

        try
        {
            await RefreshSyncInspectionAsync();
        }
        catch (Exception)
        {
            ShowFailure(SyncFailureKind.Unknown);
        }
    }

    public async Task RefreshItemsAsync()
    {
        var epoch = Interlocked.Increment(ref refreshEpoch);
        try
        {
            var catalog = await inventoryService.SearchAsync(SearchQuery);
            if (epoch != refreshEpoch)
            {
                return;
            }

            var rows = new List<CatalogItemRow>(catalog.Count);
            foreach (var item in catalog)
            {
                ItemStockSummary? summary = null;
                try
                {
                    summary = await packageService.GetItemStockSummaryAsync(item.Id);
                }
                catch
                {
                }

                if (epoch != refreshEpoch)
                {
                    return;
                }

                rows.Add(ToRow(item, summary));
            }

            Items = rows;
        }
        catch (Exception)
        {
            if (epoch != refreshEpoch || pinSessionStatus)
            {
                return;
            }

            ShowFailure(SyncFailureKind.LocalStorage);
        }
    }

    public async Task CompleteConflictResolutionAsync(SyncResult result)
    {
        if (result.Outcome == SyncOutcome.Conflict)
        {
            ShowConflict();
            return;
        }

        pinSessionStatus = false;
        Show(SyncUiState.Synced, "Выбранные версии применены.");
        await RefreshItemsAsync();
        await RefreshSyncInspectionAsync();
    }

    public void ShowSyncFailure(SyncFailureKind kind) => ShowFailure(kind);

    private Task SyncAsync()
    {
        if (!scheduler.TryBeginExclusive())
        {
            return Task.CompletedTask;
        }

        return ExecuteSyncAsync(saveSettings: true, reason: null, static () => true, syncLifetime.Token);
    }

    private async Task ExecuteSyncAsync(
        bool saveSettings,
        AutoSyncReason? reason,
        Func<bool> stillSafe,
        CancellationToken cancellationToken)
    {
        var finish = AutoSyncFinish.PermanentFailure;
        var released = false;
        try
        {
            var token = await ResolveTokenAsync(saveSettings);
            if (token.Length == 0)
            {
                ShowFailure(SyncFailureKind.Authentication);
                return;
            }

            if (!stillSafe())
            {
                released = true;
                scheduler.TryKeepAutomaticClaim(new AutoSyncReadiness(true, true, false, false, HasUnresolvedConflict));
                return;
            }

            pinSessionStatus = false;
            Show(
                SyncUiState.Syncing,
                reason is { } automatic
                    ? SyncStatusText.AutomaticDetail(automatic)
                    : "Отправляем и получаем изменения.");

            var target = saveSettings ? SaveCurrentTarget() : ReadSavedTarget();
            var result = await syncService.SyncAsync(target, token, DeviceInfo.Name, cancellationToken);
            await RefreshItemsAsync();
            if (result.Outcome == SyncOutcome.Conflict)
            {
                ShowConflict();
                RequestConflictPage(target, result);
                finish = AutoSyncFinish.Conflict;
                return;
            }

            pinSessionStatus = false;
            await RefreshSyncInspectionAsync();
            Show(
                SyncUiState.Synced,
                reason is null
                    ? "Изменения синхронизированы."
                    : SyncStatusText.AutomaticCompleted);
            finish = AutoSyncFinish.Succeeded;
        }
        catch (OperationCanceledException exception) when (AutoSyncPolicy.IsCallerCancellation(exception, cancellationToken))
        {
            finish = AutoSyncFinish.Cancelled;
        }
        catch (SyncFailureException exception)
        {
            ShowFailure(exception.Kind);
            finish = AutoSyncPolicy.FinishFor(exception.Kind);
        }
        catch (Exception)
        {
            ShowFailure(SyncFailureKind.Unknown);
            finish = AutoSyncFinish.PermanentFailure;
        }
        finally
        {
            if (!released)
            {
                scheduler.End(finish, suppressFollowUp: saveSettings);
            }

            SyncOperationFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<string> ResolveTokenAsync(bool allowTokenInput)
    {
        if (allowTokenInput)
        {
            var typed = TokenInput.Trim();
            if (typed.Length > 0)
            {
                await tokenStore.SaveTokenAsync(typed);
                TokenInput = string.Empty;
                HasSavedToken = true;
                return typed;
            }
        }

        var saved = await tokenStore.GetTokenAsync() ?? string.Empty;
        HasSavedToken = saved.Length > 0;
        return saved;
    }

    private SyncTarget SaveCurrentTarget()
    {
        SaveSyncPreferences();
        return new SyncTarget(Owner.Trim(), Repository.Trim(), Branch.Trim());
    }

    private static SyncTarget ReadSavedTarget() => new(
        Preferences.Default.Get(OwnerPreference, "666Katrina666").Trim(),
        Preferences.Default.Get(RepositoryPreference, "aptechka-data").Trim(),
        Preferences.Default.Get(BranchPreference, "main").Trim());

    private void RequestConflictPage(SyncTarget target, SyncResult result)
    {
        var resolvable = result.Conflicts
            .Where(static conflict => !ConflictPresentation.IsManifest(conflict))
            .ToArray();
        if (resolvable.Length == 0)
        {
            return;
        }

        ConflictResolutionRequested?.Invoke(
            this,
            new ConflictResolutionRequest(target, DeviceInfo.Name, resolvable));
    }

    private async Task RefreshSyncInspectionAsync()
    {
        if (!HasSavedToken)
        {
            Show(SyncUiState.NotConfigured, "Вставь GitHub-токен, чтобы синхронизировать аптечку.");
            return;
        }

        try
        {
            var inspection = await syncStateInspector.InspectAsync();
            LastSuccessfulText = SyncStatusText.LastSuccessful(inspection.LastSuccessfulAt);
            switch (inspection.Condition)
            {
                case SyncInspectionCondition.MatchesBase:
                    Show(SyncUiState.Synced, "Локальные данные совпадают с последней успешной синхронизацией.");
                    break;
                case SyncInspectionCondition.LocalChanges:
                    Show(SyncUiState.LocalChanges, "Изменения сохранены на устройстве и ещё не отправлены.");
                    break;
                default:
                    if (Items.Count == 0)
                    {
                        Show(SyncUiState.NotConfigured, "Синхронизация ещё не выполнялась.");
                        SyncHeadline = "Синхронизация ещё не выполнялась";
                    }
                    else
                    {
                        Show(SyncUiState.LocalChanges, "Локальные данные ещё не отправлялись.");
                    }

                    break;
            }
        }
        catch (SyncFailureException exception)
        {
            ShowFailure(exception.Kind);
        }
        catch (Exception)
        {
            ShowFailure(SyncFailureKind.Unknown);
        }
    }

    private void ShowConflict() =>
        Show(SyncUiState.Conflict, "Выбери версию на этом устройстве или из GitHub.");

    private void ShowFailure(SyncFailureKind kind)
    {
        pinSessionStatus = true;
        lastFailure = kind;
        if (RequiresGitHubSettings(kind))
        {
            AreGitHubSettingsExpanded = true;
        }

        Show(SyncUiState.Error, SyncStatusText.Detail(kind));
    }

    private void Show(SyncUiState state, string detail)
    {
        pinSessionStatus = state is SyncUiState.Conflict or SyncUiState.Error;
        syncState = state;
        SyncHeadline = state == SyncUiState.NotConfigured && detail == "Синхронизация ещё не выполнялась."
            ? "Синхронизация ещё не выполнялась"
            : SyncStatusText.Headline(state);
        SyncDetail = detail;
        OnPropertyChanged(nameof(IsSynced));
        OnPropertyChanged(nameof(IsLocalChanges));
        OnPropertyChanged(nameof(IsSyncing));
        OnPropertyChanged(nameof(IsConflict));
        OnPropertyChanged(nameof(IsError));
    }

    private static CatalogItemRow ToRow(InventoryItem item, ItemStockSummary? summary)
    {
        var details = string.Join(
            ", ",
            new[] { item.Form, item.Strength }.Where(static value => !string.IsNullOrEmpty(value)));

        return new CatalogItemRow(
            item.Id,
            item.Name,
            item.Category switch
            {
                InventoryItemCategory.Medicine => "Лекарство",
                InventoryItemCategory.MedicalSupply => "Медицинский расходник",
                _ => item.Category.ToString(),
            },
            string.IsNullOrEmpty(details) ? null : details,
            item.KeepInStock,
            summary is null ? "Наличие не загружено" : PackageText.Availability(summary.Availability),
            summary is null ? null : PackageText.UsablePackageCount(summary.UsablePackageCount),
            summary is null ? null : PackageText.NearestExpiration(summary.NearestExpirationDate),
            summary is null ? null : PackageText.ExpiredWarning(summary.ExpiredPackageCount));
    }

    private void SaveSyncPreferences()
    {
        Preferences.Default.Set(OwnerPreference, Owner.Trim());
        Preferences.Default.Set(RepositoryPreference, Repository.Trim());
        Preferences.Default.Set(BranchPreference, Branch.Trim());
    }

    private void InitializeGitHubSettingsVisibility()
    {
        if (gitHubSettingsInitialized)
        {
            return;
        }

        AreGitHubSettingsExpanded = !HasSavedToken || !HasSavedSyncTarget;
        gitHubSettingsInitialized = true;
    }

    private void ToggleGitHubSettings() =>
        AreGitHubSettingsExpanded = !AreGitHubSettingsExpanded;

    private static bool RequiresGitHubSettings(SyncFailureKind kind) =>
        kind is SyncFailureKind.InvalidConfiguration
            or SyncFailureKind.Authentication
            or SyncFailureKind.AccessDenied
            or SyncFailureKind.RepositoryNotFound;

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
