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
    private readonly ISecureTokenStore tokenStore;
    private int refreshEpoch;
    private string searchQuery = string.Empty;
    private IReadOnlyList<CatalogItemRow> items = [];
    private string owner;
    private string repository;
    private string branch;
    private string tokenInput = string.Empty;
    private bool hasSavedToken;
    private string status = "Локальные данные ещё не загружены.";
    private bool isBusy;

    public MainViewModel(
        InventoryService inventoryService,
        PackageService packageService,
        ISyncService syncService,
        ISecureTokenStore tokenStore)
    {
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        this.syncService = syncService;
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
            catch (Exception exception)
            {
                HasSavedToken = false;
                Status = $"Не удалось проверить сохранённый токен: {exception.Message}";
            }

            await RefreshItemsAsync();
        }
        catch (Exception exception)
        {
            Status = $"Не удалось загрузить каталог: {exception.Message}";
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
            var failedSummaries = 0;
            foreach (var item in catalog)
            {
                ItemStockSummary? summary = null;
                try
                {
                    summary = await packageService.GetItemStockSummaryAsync(item.Id);
                }
                catch
                {
                    failedSummaries++;
                }

                if (epoch != refreshEpoch)
                {
                    return;
                }

                rows.Add(ToRow(item, summary));
            }

            Items = rows;
            if (failedSummaries > 0)
            {
                Status = failedSummaries == 1
                    ? "Не удалось загрузить наличие для одной позиции."
                    : $"Не удалось загрузить наличие для {failedSummaries} позиций.";
            }
            else if (Status == "Локальные данные ещё не загружены." ||
                     Status.StartsWith("Не удалось загрузить наличие", StringComparison.Ordinal))
            {
                Status = Items.Count == 0
                    ? "Каталог пуст. Добавь позицию или синхронизируй данные."
                    : $"Загружен каталог: {Items.Count}.";
            }
        }
        catch (Exception exception)
        {
            if (epoch != refreshEpoch)
            {
                return;
            }

            Status = $"Не удалось загрузить каталог: {exception.Message}";
        }
    }

    public async Task CompleteConflictResolutionAsync(SyncResult result)
    {
        Status = result.Message;
        await RefreshItemsAsync();
    }

    private async Task SyncAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
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
                throw new InvalidOperationException("Сначала введи GitHub-токен.");
            }

            SaveSyncPreferences();
            var target = new SyncTarget(Owner.Trim(), Repository.Trim(), Branch.Trim());
            var result = await syncService.SyncAsync(
                target,
                token,
                DeviceInfo.Name,
                CancellationToken.None);

            await RefreshItemsAsync();
            Status = result.Message;
            if (result.Outcome == SyncOutcome.Conflict)
            {
                var resolvable = result.Conflicts
                    .Where(static conflict => !ConflictPresentation.IsManifest(conflict))
                    .ToArray();
                if (resolvable.Length > 0)
                {
                    ConflictResolutionRequested?.Invoke(
                        this,
                        new ConflictResolutionRequest(target, DeviceInfo.Name, resolvable));
                }
            }
        }
        catch (Exception exception)
        {
            Status = $"Синхронизация остановлена: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
