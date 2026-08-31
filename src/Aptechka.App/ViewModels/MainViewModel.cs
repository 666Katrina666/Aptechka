using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.App.Services;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string OwnerPreference = "sync-owner";
    private const string RepositoryPreference = "sync-repository";
    private const string BranchPreference = "sync-branch";

    private readonly InventoryService inventoryService;
    private readonly ISyncService syncService;
    private readonly ISecureTokenStore tokenStore;
    private string name = string.Empty;
    private string activeIngredients = string.Empty;
    private string? form;
    private string? strength;
    private string? description;
    private bool keepInStock;
    private CategoryOption selectedCategory;
    private string owner;
    private string repository;
    private string branch;
    private string tokenInput = string.Empty;
    private bool hasSavedToken;
    private string status = "Локальные данные ещё не загружены.";
    private bool isBusy;

    public MainViewModel(
        InventoryService inventoryService,
        ISyncService syncService,
        ISecureTokenStore tokenStore)
    {
        this.inventoryService = inventoryService;
        this.syncService = syncService;
        this.tokenStore = tokenStore;

        CategoryOptions =
        [
            new("Лекарство", InventoryItemCategory.Medicine),
            new("Медицинский расходник", InventoryItemCategory.MedicalSupply),
        ];
        selectedCategory = CategoryOptions[0];
        owner = Preferences.Default.Get(OwnerPreference, "666Katrina666");
        repository = Preferences.Default.Get(RepositoryPreference, "aptechka-data");
        branch = Preferences.Default.Get(BranchPreference, "main");

        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
        SyncCommand = new Command(async () => await SyncAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<CategoryOption> CategoryOptions { get; }
    public ICommand SaveCommand { get; }
    public ICommand SyncCommand { get; }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public CategoryOption SelectedCategory
    {
        get => selectedCategory;
        set => SetField(ref selectedCategory, value);
    }

    public string ActiveIngredients
    {
        get => activeIngredients;
        set => SetField(ref activeIngredients, value);
    }

    public string? Form
    {
        get => form;
        set => SetField(ref form, value);
    }

    public string? Strength
    {
        get => strength;
        set => SetField(ref strength, value);
    }

    public string? Description
    {
        get => description;
        set => SetField(ref description, value);
    }

    public bool KeepInStock
    {
        get => keepInStock;
        set => SetField(ref keepInStock, value);
    }

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

            ((Command)SaveCommand).ChangeCanExecute();
            ((Command)SyncCommand).ChangeCanExecute();
        }
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            HasSavedToken = await tokenStore.HasTokenAsync();
            var item = await inventoryService.GetPrototypeItemAsync();
            PopulateItem(item);
            Status = item is null
                ? "Позиции пока нет. Заполни карточку и сохрани её локально."
                : $"Загружена локальная ревизия {item.Revision}.";
        }
        catch (Exception exception)
        {
            Status = $"Не удалось загрузить локальные данные: {exception.Message}";
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
        try
        {
            var item = await SaveLocalCoreAsync();
            Status = $"Сохранено локально. Ревизия {item.Revision}.";
        }
        catch (Exception exception)
        {
            Status = $"Не удалось сохранить: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

            if (result.Outcome is SyncOutcome.Pulled)
            {
                PopulateItem(await inventoryService.GetPrototypeItemAsync());
            }

            Status = result.Message;
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

    private async Task<InventoryItem> SaveLocalCoreAsync()
    {
        var ingredients = ActiveIngredients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var draft = new InventoryItemDraft(
            Name,
            SelectedCategory.Value,
            ingredients,
            Form,
            Strength,
            Description,
            KeepInStock);

        return await inventoryService.SavePrototypeItemAsync(draft);
    }

    private void PopulateItem(InventoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Name = item.Name;
        SelectedCategory = CategoryOptions.Single(option => option.Value == item.Category);
        ActiveIngredients = string.Join(", ", item.ActiveIngredients);
        Form = item.Form;
        Strength = item.Strength;
        Description = item.Description;
        KeepInStock = item.KeepInStock;
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

    public sealed record CategoryOption(string Name, InventoryItemCategory Value)
    {
        public override string ToString() => Name;
    }
}
