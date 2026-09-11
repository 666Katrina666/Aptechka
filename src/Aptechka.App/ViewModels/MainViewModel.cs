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
    private int refreshEpoch;
    private string searchQuery = string.Empty;
    private IReadOnlyList<CatalogItemRow> items = [];
    private string owner;
    private string repository;
    private string branch;
    private string tokenInput = string.Empty;
    private bool hasSavedToken;
    private bool pinSessionStatus;
    private SyncUiState syncState = SyncUiState.NotConfigured;
    private string syncHeadline = SyncStatusText.Headline(SyncUiState.NotConfigured);
    private string syncDetail = "Вставь GitHub-токен, чтобы синхронизировать аптечку.";
    private string? lastSuccessfulText;
    private bool isBusy;

    public MainViewModel(
        InventoryService inventoryService,
        PackageService packageService,
        ISyncService syncService,
        ISyncStateInspector syncStateInspector,
        ISecureTokenStore tokenStore)
    {
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        this.syncService = syncService;
        this.syncStateInspector = syncStateInspector;
        this.tokenStore = tokenStore;

        owner = Preferences.Default.Get(OwnerPreference, "666Katrina666");
        repository = Preferences.Default.Get(RepositoryPreference, "aptechka-data");
        branch = Preferences.Default.Get(BranchPreference, "main");

        SyncCommand = new Command(async () => await SyncAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ConflictResolutionRequest>? ConflictResolutionRequested;

    public ICommand SyncCommand { get; }

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

    public async Task LoadAsync()
    {
        try
        {
            try
            {
                HasSavedToken = await tokenStore.HasTokenAsync();
            }
            catch (Exception)
            {
                HasSavedToken = false;
                ShowFailure(SyncFailureKind.LocalStorage);
                return;
            }

            await RefreshItemsAsync();
            if (!pinSessionStatus)
            {
                await RefreshSyncInspectionAsync();
            }
        }
        catch (Exception)
        {
            ShowFailure(SyncFailureKind.LocalStorage);
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

    private async Task SyncAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        pinSessionStatus = false;
        Show(SyncUiState.Syncing, "Отправляем и получаем изменения.");
        try
        {
            var token = TokenInput.Trim();
            if (token.Length > 0)
            {
                await tokenStore.SaveTokenAsync(token);
                TokenInput = string.Empty;
                HasSavedToken = true;
            }
            else
            {
                token = await tokenStore.GetTokenAsync() ?? string.Empty;
            }

            if (token.Length == 0)
            {
                ShowFailure(SyncFailureKind.Authentication);
                return;
            }

            SaveSyncPreferences();
            var target = new SyncTarget(Owner.Trim(), Repository.Trim(), Branch.Trim());
            var result = await syncService.SyncAsync(
                target,
                token,
                DeviceInfo.Name,
                CancellationToken.None);

            await RefreshItemsAsync();
            if (result.Outcome == SyncOutcome.Conflict)
            {
                ShowConflict();
                var resolvable = result.Conflicts
                    .Where(static conflict => !ConflictPresentation.IsManifest(conflict))
                    .ToArray();
                if (resolvable.Length > 0)
                {
                    ConflictResolutionRequested?.Invoke(
                        this,
                        new ConflictResolutionRequest(target, DeviceInfo.Name, resolvable));
                }

                return;
            }

            pinSessionStatus = false;
            Show(SyncUiState.Synced, "Изменения синхронизированы.");
            await RefreshSyncInspectionAsync();
        }
        catch (SyncFailureException exception)
        {
            ShowFailure(exception.Kind);
        }
        catch (Exception)
        {
            ShowFailure(SyncFailureKind.Unknown);
        }
        finally
        {
            IsBusy = false;
        }
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
