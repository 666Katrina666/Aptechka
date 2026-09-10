using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed record PackageListRow(
    string Id,
    string Title,
    string StockState,
    string Expiration,
    string? Opened,
    bool IsExpired)
{
    public bool HasOpened => !string.IsNullOrEmpty(Opened);
}

public sealed class ItemEditorViewModel : INotifyPropertyChanged
{
    private readonly InventoryService inventoryService;
    private readonly PackageService packageService;
    private readonly IClock clock;
    private string? itemId;
    private string title = "Новая позиция";
    private string name = string.Empty;
    private string aliases = string.Empty;
    private string activeIngredients = string.Empty;
    private string? form;
    private string? strength;
    private string? description;
    private bool keepInStock;
    private CategoryOption selectedCategory;
    private string? errorMessage;
    private bool isBusy;
    private bool isEditing;
    private bool createDespiteNameConflict;
    private IReadOnlyList<PackageListRow> packages = [];

    public ItemEditorViewModel(
        InventoryService inventoryService,
        PackageService packageService,
        IClock clock)
    {
        this.inventoryService = inventoryService;
        this.packageService = packageService;
        this.clock = clock;
        CategoryOptions =
        [
            new("Лекарство", InventoryItemCategory.Medicine),
            new("Медицинский расходник", InventoryItemCategory.MedicalSupply),
        ];
        selectedCategory = CategoryOptions[0];
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Completed;

    public event EventHandler<IReadOnlyList<InventoryItem>>? NameConflictDetected;

    public IReadOnlyList<CategoryOption> CategoryOptions { get; }

    public ICommand SaveCommand { get; }

    public string? ItemId => itemId;

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

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetField(ref isEditing, value))
            {
                OnPropertyChanged(nameof(HasNoPackages));
                OnPropertyChanged(nameof(CanMutatePackages));
            }
        }
    }

    public IReadOnlyList<PackageListRow> Packages
    {
        get => packages;
        private set
        {
            if (SetField(ref packages, value))
            {
                OnPropertyChanged(nameof(HasNoPackages));
            }
        }
    }

    public bool HasNoPackages => IsEditing && Packages.Count == 0;

    public bool CanMutatePackages => IsEditing && !IsBusy;

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

            OnPropertyChanged(nameof(CanMutatePackages));
            ((Command)SaveCommand).ChangeCanExecute();
        }
    }

    public async Task LoadAsync(string? id)
    {
        ErrorMessage = null;
        itemId = null;
        IsEditing = false;
        createDespiteNameConflict = false;
        Title = "Новая позиция";
        Name = string.Empty;
        Aliases = string.Empty;
        ActiveIngredients = string.Empty;
        Form = null;
        Strength = null;
        Description = null;
        KeepInStock = false;
        SelectedCategory = CategoryOptions[0];
        Packages = [];

        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var item = await inventoryService.GetItemAsync(id);
        if (item is null || item.DeletedAt is not null)
        {
            ErrorMessage = "Позиция не найдена.";
            return;
        }

        itemId = item.Id;
        IsEditing = true;
        Title = item.Name;
        Name = item.Name;
        Aliases = string.Join(", ", item.Aliases);
        SelectedCategory = CategoryOptions.Single(option => option.Value == item.Category);
        ActiveIngredients = string.Join(", ", item.ActiveIngredients);
        Form = item.Form;
        Strength = item.Strength;
        Description = item.Description;
        KeepInStock = item.KeepInStock;
        await RefreshPackagesAsync();
    }

    public async Task RefreshPackagesAsync()
    {
        if (itemId is null)
        {
            Packages = [];
            return;
        }

        try
        {
            var loaded = await packageService.GetPackagesForItemAsync(itemId);
            var today = clock.Today;
            Packages = loaded.Select(package => new PackageListRow(
                package.Id,
                string.IsNullOrEmpty(package.Label) ? "Упаковка" : package.Label,
                PackageText.FormatStockState(package.StockState),
                PackageText.EffectiveExpiration(package),
                PackageText.Opened(package),
                ItemStock.GetUsability(package, today) == PackageUsability.Expired)).ToArray();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task ArchiveAsync()
    {
        if (IsBusy || itemId is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await inventoryService.ArchiveAsync(itemId);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CreateDespiteNameConflictAsync()
    {
        createDespiteNameConflict = true;
        return SaveAsync();
    }

    public async Task MarkLowAsync(string id)
    {
        if (!CanMutatePackages || string.IsNullOrEmpty(id))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await packageService.MarkLowAsync(id);
            await RefreshPackagesAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MarkDepletedAsync(string id)
    {
        if (!CanMutatePackages || string.IsNullOrEmpty(id))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await packageService.MarkDepletedAsync(id);
            await RefreshPackagesAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
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
            var draft = new InventoryItemDraft(
                Name,
                SplitCsv(Aliases),
                SelectedCategory.Value,
                SplitCsv(ActiveIngredients),
                Form,
                Strength,
                Description,
                KeepInStock);

            if (itemId is null)
            {
                if (!createDespiteNameConflict)
                {
                    var conflicts = await inventoryService.FindNameConflictsAsync(Name);
                    if (conflicts.Count > 0)
                    {
                        NameConflictDetected?.Invoke(this, conflicts);
                        return;
                    }
                }

                await inventoryService.CreateAsync(draft);
            }
            else
            {
                await inventoryService.UpdateAsync(itemId, draft);
            }

            createDespiteNameConflict = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException exception)
        {
            createDespiteNameConflict = false;
            ErrorMessage = exception.ParamName == "value"
                ? "Название не может быть пустым."
                : exception.Message;
        }
        catch (Exception exception)
        {
            createDespiteNameConflict = false;
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
