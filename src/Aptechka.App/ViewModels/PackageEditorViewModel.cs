using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Aptechka.Application.Inventory;
using Aptechka.Domain.Inventory;

namespace Aptechka.App.ViewModels;

public sealed class PackageEditorViewModel : INotifyPropertyChanged
{
    private readonly PackageService packageService;
    private readonly IClock clock;
    private string? packageId;
    private string itemId = string.Empty;
    private string title = "Новая упаковка";
    private string label = string.Empty;
    private bool hasExpiration;
    private DateTime expirationPickerDate;
    private PrecisionOption selectedPrecision;
    private bool hasOpenedDate;
    private DateTime openedPickerDate;
    private string shelfLifeText = string.Empty;
    private StockOption selectedStock;
    private string note = string.Empty;
    private string? errorMessage;
    private bool isBusy;
    private bool isEditing;

    public PackageEditorViewModel(PackageService packageService, IClock clock)
    {
        this.packageService = packageService;
        this.clock = clock;
        PrecisionOptions =
        [
            new("до дня", ExpirationPrecision.Day),
            new("до месяца", ExpirationPrecision.Month),
        ];
        StockOptions =
        [
            new("В наличии", StockState.Available),
            new("Скоро закончится", StockState.Low),
            new("Закончилась", StockState.Depleted),
        ];
        selectedPrecision = PrecisionOptions[0];
        selectedStock = StockOptions[0];
        var today = clock.Today.ToDateTime(TimeOnly.MinValue);
        expirationPickerDate = today;
        openedPickerDate = today;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
        ArchiveCommand = new Command(async () => await ArchiveAsync(), () => CanArchive);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Completed;

    public IReadOnlyList<PrecisionOption> PrecisionOptions { get; }

    public IReadOnlyList<StockOption> StockOptions { get; }

    public ICommand SaveCommand { get; }

    public ICommand ArchiveCommand { get; }

    public string Title
    {
        get => title;
        private set => SetField(ref title, value);
    }

    public string Label
    {
        get => label;
        set => SetField(ref label, value);
    }

    public bool HasExpiration
    {
        get => hasExpiration;
        set
        {
            if (SetField(ref hasExpiration, value))
            {
                OnPropertyChanged(nameof(MonthHint));
            }
        }
    }

    public DateTime ExpirationPickerDate
    {
        get => expirationPickerDate;
        set
        {
            if (SetField(ref expirationPickerDate, value))
            {
                OnPropertyChanged(nameof(MonthHint));
            }
        }
    }

    public PrecisionOption SelectedPrecision
    {
        get => selectedPrecision;
        set
        {
            if (SetField(ref selectedPrecision, value))
            {
                OnPropertyChanged(nameof(MonthHint));
            }
        }
    }

    public string MonthHint =>
        HasExpiration && SelectedPrecision.Value == ExpirationPrecision.Month
            ? $"При точности до месяца число не учитывается. Будет сохранено: {PackageText.FormatMonthYear(DateOnly.FromDateTime(ExpirationPickerDate))} (до {PackageText.FormatDate(PackageText.LastDayOfMonth(DateOnly.FromDateTime(ExpirationPickerDate)))})."
            : "При точности «до месяца» число на календаре не учитывается.";

    public bool HasOpenedDate
    {
        get => hasOpenedDate;
        set => SetField(ref hasOpenedDate, value);
    }

    public DateTime OpenedPickerDate
    {
        get => openedPickerDate;
        set => SetField(ref openedPickerDate, value);
    }

    public string ShelfLifeText
    {
        get => shelfLifeText;
        set => SetField(ref shelfLifeText, value);
    }

    public StockOption SelectedStock
    {
        get => selectedStock;
        set => SetField(ref selectedStock, value);
    }

    public string Note
    {
        get => note;
        set => SetField(ref note, value);
    }

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetField(ref isEditing, value))
            {
                OnPropertyChanged(nameof(CanArchive));
                ((Command)ArchiveCommand).ChangeCanExecute();
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
            ((Command)ArchiveCommand).ChangeCanExecute();
        }
    }

    public async Task LoadAsync(string ownerItemId, string? id)
    {
        ErrorMessage = null;
        itemId = ownerItemId;
        packageId = null;
        IsEditing = false;
        Title = "Новая упаковка";
        Label = string.Empty;
        HasExpiration = false;
        HasOpenedDate = false;
        ShelfLifeText = string.Empty;
        Note = string.Empty;
        SelectedPrecision = PrecisionOptions[0];
        SelectedStock = StockOptions[0];
        var today = clock.Today.ToDateTime(TimeOnly.MinValue);
        ExpirationPickerDate = today;
        OpenedPickerDate = today;

        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var package = await packageService.GetPackageAsync(id);
        if (package is null)
        {
            ErrorMessage = "Упаковка не найдена.";
            return;
        }

        packageId = package.Id;
        itemId = package.ItemId;
        IsEditing = true;
        Title = string.IsNullOrEmpty(package.Label) ? "Упаковка" : package.Label;
        Label = package.Label ?? string.Empty;
        if (package.ExpirationDate is { } expiration)
        {
            HasExpiration = true;
            ExpirationPickerDate = expiration.ToDateTime(TimeOnly.MinValue);
            SelectedPrecision = PrecisionOptions.Single(option =>
                option.Value == (package.ExpirationPrecision ?? ExpirationPrecision.Day));
        }

        if (package.OpenedDate is { } opened)
        {
            HasOpenedDate = true;
            OpenedPickerDate = opened.ToDateTime(TimeOnly.MinValue);
        }

        ShelfLifeText = package.ShelfLifeAfterOpeningDays?.ToString() ?? string.Empty;
        SelectedStock = StockOptions.Single(option => option.Value == package.StockState);
        Note = package.Note ?? string.Empty;
    }

    public async Task ConfirmArchiveAsync() => await ArchiveAsync();

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
            if (!TryCreateDraft(out var draft, out var validationError))
            {
                ErrorMessage = validationError;
                return;
            }

            if (packageId is null)
            {
                await packageService.CreateAsync(itemId, draft);
            }
            else
            {
                await packageService.UpdateAsync(packageId, draft);
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ErrorMessage = FriendlyError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ArchiveAsync()
    {
        if (!CanArchive || packageId is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await packageService.ArchiveAsync(packageId);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ErrorMessage = FriendlyError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryCreateDraft(out PackageDraft draft, out string? error)
    {
        draft = null!;
        error = null;
        DateOnly? expirationDate = null;
        ExpirationPrecision? precision = null;
        if (HasExpiration)
        {
            var date = DateOnly.FromDateTime(ExpirationPickerDate);
            if (SelectedPrecision.Value == ExpirationPrecision.Month)
            {
                date = PackageText.LastDayOfMonth(date);
            }

            expirationDate = date;
            precision = SelectedPrecision.Value;
        }

        DateOnly? openedDate = HasOpenedDate ? DateOnly.FromDateTime(OpenedPickerDate) : null;
        int? shelfLife = null;
        if (!string.IsNullOrWhiteSpace(ShelfLifeText))
        {
            if (!int.TryParse(ShelfLifeText.Trim(), out var days) || days < 1)
            {
                error = "Срок после вскрытия должен быть целым положительным числом либо пустым.";
                return false;
            }

            shelfLife = days;
        }

        draft = new PackageDraft(
            Label,
            expirationDate,
            precision,
            openedDate,
            shelfLife,
            SelectedStock.Value,
            Note);
        return true;
    }

    private static string FriendlyError(Exception exception) =>
        exception.Message switch
        {
            "Срок годности и его точность задаются только вместе." =>
                "Включи срок годности полностью или выключи его.",
            "При точности до месяца срок должен быть последним днём месяца." =>
                "Для срока до месяца сохраняется последний день выбранного месяца.",
            "Срок после вскрытия должен быть строго положительным." =>
                "Срок после вскрытия должен быть целым положительным числом либо пустым.",
            _ => exception.Message,
        };

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

    public sealed record PrecisionOption(string Name, ExpirationPrecision Value)
    {
        public override string ToString() => Name;
    }

    public sealed record StockOption(string Name, StockState Value)
    {
        public override string ToString() => Name;
    }
}
