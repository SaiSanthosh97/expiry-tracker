using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.ViewModels;

public sealed partial class CategoryChip(ShelfLifeCategory category) : ObservableObject
{
    public ShelfLifeCategory Category { get; } = category;

    public string Label { get; } = Text.Category(category.Id);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// One row of the two-column category grid. Each cell is a 0–1 item list so both cells
/// can share one chip template through BindableLayout.
/// </summary>
public sealed record CategoryRow(CategoryChip Left, CategoryChip? Right)
{
    public IReadOnlyList<CategoryChip> LeftCell { get; } = [Left];

    public IReadOnlyList<CategoryChip> RightCell { get; } = Right is null ? [] : [Right];
}

/// <summary>Add and edit screen. One scrolling form with defaults, so most items need only a few taps.</summary>
public sealed partial class AddItemViewModel : ObservableObject, IQueryAttributable
{
    private const int YearsBack = 2;
    private const int YearsAhead = 10;

    private readonly ItemService _items;
    private readonly Dialogs _dialogs;
    private TrackedItem? _editing;
    private bool _loading;

    public AddItemViewModel(ItemService items, ShelfLifeCatalog catalog, Dialogs dialogs)
    {
        _items = items;
        _dialogs = dialogs;

        Categories = new ObservableCollection<CategoryChip>(
            DefaultPresets.All.Select(p => catalog.Find(p.Id) ?? p).Select(c => new CategoryChip(c)));
        CategoryRows = Categories.Chunk(2).Select(pair => new CategoryRow(pair[0], pair.Length > 1 ? pair[1] : null)).ToList();
        Months = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames.Take(12).ToList();
        var thisYear = items.Today.Year;
        Years = Enumerable.Range(thisYear - YearsBack, YearsBack + YearsAhead + 1).ToList();
        Days = Enumerable.Range(1, 31).ToList();

        Title = AppResources.Add_Title;
        ResetDates();
        SelectCategory(Categories.First(c => c.Category.Id == DefaultPresets.PackagedFood.Id));
    }

    public ObservableCollection<CategoryChip> Categories { get; }

    public IReadOnlyList<CategoryRow> CategoryRows { get; }

    public IReadOnlyList<string> Months { get; }

    public IReadOnlyList<int> Years { get; }

    public IReadOnlyList<int> Days { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    // Exactly one of the three date modes is on; RadioButtons bind to these.
    [ObservableProperty]
    public partial bool IsPackMode { get; set; }

    [ObservableProperty]
    public partial bool IsNoDateMode { get; set; }

    [ObservableProperty]
    public partial bool IsMadeMode { get; set; }

    [ObservableProperty]
    public partial bool HasDay { get; set; }

    [ObservableProperty]
    public partial int DayIndex { get; set; }

    [ObservableProperty]
    public partial int MonthIndex { get; set; }

    [ObservableProperty]
    public partial int YearIndex { get; set; }

    [ObservableProperty]
    public partial int ShelfDays { get; set; } = 3;

    [ObservableProperty]
    public partial int MadeMonthIndex { get; set; }

    [ObservableProperty]
    public partial int MadeYearIndex { get; set; }

    [ObservableProperty]
    public partial int GoodForMonths { get; set; } = 12;

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial string Preview { get; set; } = "";

    public string ShelfDaysText => Text.Duration(ShelfLifeDuration.Days(ShelfDays));

    public string GoodForMonthsText => Text.Duration(ShelfLifeDuration.Months(GoodForMonths));

    private CategoryChip SelectedCategory => Categories.FirstOrDefault(c => c.IsSelected) ?? Categories[0];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && id is string itemId)
        {
            _ = LoadForEditAsync(itemId);
        }
    }

    private async Task LoadForEditAsync(string id)
    {
        var evaluation = await _items.GetAsync(id);
        if (evaluation is null)
        {
            return;
        }

        _loading = true;
        var item = evaluation.Item;
        _editing = item;
        Title = AppResources.Edit_Title;
        Name = item.Name;
        Quantity = item.Quantity;
        if (Categories.FirstOrDefault(c => c.Category.Id == item.CategoryId) is { } chip)
        {
            SelectCategory(chip, applyDefaults: false);
        }

        if (item.PrintedExpiry is { } printed)
        {
            SetMode(pack: true);
            HasDay = printed.Day != DateTime.DaysInMonth(printed.Year, printed.Month);
            DayIndex = printed.Day - 1;
            MonthIndex = printed.Month - 1;
            YearIndex = Math.Max(0, Years.ToList().IndexOf(printed.Year));
        }
        else if (item is { ManufactureDate: { } made, BestBeforeFromManufacture: { Unit: ShelfLifeUnit.Months } bestBefore })
        {
            SetMode(made: true);
            MadeMonthIndex = made.Month - 1;
            MadeYearIndex = Math.Max(0, Years.ToList().IndexOf(made.Year));
            GoodForMonths = bestBefore.Amount;
        }
        else if (item.ShelfLifeFromAdded is { } shelfLife)
        {
            SetMode(noDate: true);
            ShelfDays = shelfLife.ApproximateDays;
        }

        _loading = false;
        UpdatePreview();
    }

    // Dates start empty on purpose: a pre-filled guess that the user forgets to change could
    // record an item as good for months longer than it really is.
    private void ResetDates()
    {
        DayIndex = -1;
        MonthIndex = -1;
        YearIndex = -1;
        MadeMonthIndex = -1;
        MadeYearIndex = -1;
    }

    private bool IsDateMissing =>
        (IsPackMode && (MonthIndex < 0 || YearIndex < 0 || (HasDay && DayIndex < 0)))
        || (IsMadeMode && (MadeMonthIndex < 0 || MadeYearIndex < 0));

    private void SetMode(bool pack = false, bool noDate = false, bool made = false)
    {
        IsPackMode = pack;
        IsNoDateMode = noDate;
        IsMadeMode = made;
    }

    [RelayCommand]
    private void SelectCategory(CategoryChip chip) => SelectCategory(chip, applyDefaults: _editing is null);

    private void SelectCategory(CategoryChip chip, bool applyDefaults)
    {
        foreach (var c in Categories)
        {
            c.IsSelected = c == chip;
        }

        if (applyDefaults)
        {
            if (chip.Category.ShelfLife is { } shelfLife)
            {
                SetMode(noDate: true);
                ShelfDays = shelfLife.ApproximateDays;
            }
            else
            {
                SetMode(pack: true);
            }
        }

        UpdatePreview();
    }

    [RelayCommand]
    private void ChangeShelfDays(string delta) => ShelfDays = Math.Clamp(ShelfDays + int.Parse(delta, CultureInfo.InvariantCulture), 1, 365);

    [RelayCommand]
    private void ChangeGoodForMonths(string delta) => GoodForMonths = Math.Clamp(GoodForMonths + int.Parse(delta, CultureInfo.InvariantCulture), 1, 120);

    [RelayCommand]
    private void ChangeQuantity(string delta) => Quantity = Math.Clamp(Quantity + int.Parse(delta, CultureInfo.InvariantCulture), 1, 999);

    partial void OnIsPackModeChanged(bool value) => UpdatePreview();

    partial void OnIsNoDateModeChanged(bool value) => UpdatePreview();

    partial void OnIsMadeModeChanged(bool value) => UpdatePreview();

    partial void OnHasDayChanged(bool value) => UpdatePreview();

    partial void OnDayIndexChanged(int value) => UpdatePreview();

    partial void OnMonthIndexChanged(int value) => UpdatePreview();

    partial void OnYearIndexChanged(int value) => UpdatePreview();

    partial void OnMadeMonthIndexChanged(int value) => UpdatePreview();

    partial void OnMadeYearIndexChanged(int value) => UpdatePreview();

    partial void OnShelfDaysChanged(int value)
    {
        OnPropertyChanged(nameof(ShelfDaysText));
        UpdatePreview();
    }

    partial void OnGoodForMonthsChanged(int value)
    {
        OnPropertyChanged(nameof(GoodForMonthsText));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_loading || Years is null)
        {
            return;
        }

        var evaluation = _items.Preview(BuildItem());
        Preview = evaluation.Expiry.LastUsableDay is { } last
            ? string.Format(AppResources.Add_LastDayPreview, Text.Date(last))
            : "";
    }

    private TrackedItem BuildItem()
    {
        var category = SelectedCategory.Category;
        var name = string.IsNullOrWhiteSpace(Name) ? SelectedCategory.Label : Name.Trim();
        var item = (_editing ?? new TrackedItem
        {
            Id = TrackedItem.NewId(),
            Name = name,
            CategoryId = category.Id,
            AddedDate = _items.Today,
        }) with
        {
            Name = name,
            CategoryId = category.Id,
            Quantity = Quantity,
            PrintedExpiry = null,
            ManufactureDate = null,
            BestBeforeFromManufacture = null,
            ShelfLifeFromAdded = null,
        };

        if (IsPackMode && TryGetYear(YearIndex, out var year) && MonthIndex >= 0 && !(HasDay && DayIndex < 0))
        {
            var month = MonthIndex + 1;
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var day = HasDay ? Math.Min(DayIndex + 1, daysInMonth) : daysInMonth;
            return item with { PrintedExpiry = new DateOnly(year, month, day) };
        }

        if (IsMadeMode && TryGetYear(MadeYearIndex, out var madeYear) && MadeMonthIndex >= 0)
        {
            return item with
            {
                ManufactureDate = new DateOnly(madeYear, MadeMonthIndex + 1, 1),
                BestBeforeFromManufacture = ShelfLifeDuration.Months(GoodForMonths),
            };
        }

        if (IsNoDateMode)
        {
            return item with { ShelfLifeFromAdded = ShelfLifeDuration.Days(ShelfDays) };
        }

        return item;
    }

    private bool TryGetYear(int index, out int year)
    {
        year = index >= 0 && index < Years.Count ? Years[index] : 0;
        return year > 0;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsDateMissing)
        {
            await _dialogs.AlertAsync(AppResources.Add_DateLabel, AppResources.Add_PickDate);
            return;
        }

        await _items.SaveAsync(BuildItem());
        await Shell.Current.GoToAsync("..");
    }
}
