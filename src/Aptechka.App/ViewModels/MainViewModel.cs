using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly InventoryService inventoryService;
    private string name = string.Empty;
    private string activeIngredients = string.Empty;
    private string? form;
    private string? strength;
    private string? description;
    private bool keepInStock;
    private CategoryOption selectedCategory;
    private string status = "Локальные данные ещё не загружены.";
    private bool isBusy;

    public MainViewModel(InventoryService inventoryService)
    {
        this.inventoryService = inventoryService;
        CategoryOptions =
        [
            new("Лекарство", InventoryItemCategory.Medicine),
            new("Медицинский расходник", InventoryItemCategory.MedicalSupply),
        ];
        selectedCategory = CategoryOptions[0];

        LoadCommand = new Command(async () => await LoadAsync(), () => !IsBusy);
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<CategoryOption> CategoryOptions { get; }
    public ICommand LoadCommand { get; }
    public ICommand SaveCommand { get; }

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

            ((Command)LoadCommand).ChangeCanExecute();
            ((Command)SaveCommand).ChangeCanExecute();
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
            var item = await inventoryService.GetPrototypeItemAsync();
            if (item is null)
            {
                Status = "Позиции пока нет. Заполни карточку и сохрани её локально.";
                return;
            }

            Name = item.Name;
            SelectedCategory = CategoryOptions.Single(option => option.Value == item.Category);
            ActiveIngredients = string.Join(", ", item.ActiveIngredients);
            Form = item.Form;
            Strength = item.Strength;
            Description = item.Description;
            KeepInStock = item.KeepInStock;
            Status = $"Загружена локальная ревизия {item.Revision}.";
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

            var item = await inventoryService.SavePrototypeItemAsync(draft);
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public sealed record CategoryOption(string Name, InventoryItemCategory Value)
    {
        public override string ToString() => Name;
    }
}
