using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Households;
using HomeTracker.Core.Medicines;

namespace ExpiryTracker.App.ViewModels;

public sealed partial class DoseTime(TimeSpan time) : ObservableObject
{
    [ObservableProperty]
    public partial TimeSpan Time { get; set; } = time;
}

public sealed partial class DayChip(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public string Label { get; } = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)day];

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Add or edit a medicine: who, what, when, how much, stock, and which phone rings.</summary>
public sealed partial class MedicineEditViewModel(MedicineService medicines, HomeService homes, Dialogs dialogs, Clock clock)
    : ObservableObject, IQueryAttributable
{
    private Medicine? _editing;
    private IReadOnlyList<Home> _homes = [];
    private List<(string? DeviceId, string Name)> _phones = [];

    public ObservableCollection<DoseTime> Times { get; } = [new(new TimeSpan(9, 0, 0))];

    public IReadOnlyList<DayChip> Days { get; } =
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new DayChip(d))];

    public IReadOnlyList<string> UnitNames { get; } = [.. MedicineText.Units.Select(u => MedicineText.UnitName(u, 2))];

    public ObservableCollection<string> People { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = AppResources.Med_AddTitle;

    [ObservableProperty]
    public partial string Person { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimes))]
    public partial bool IsDaily { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimes))]
    public partial bool IsAlternate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimes))]
    public partial bool IsSomeDays { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimes))]
    public partial bool IsAsNeeded { get; set; }

    public bool HasTimes => !IsAsNeeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountText))]
    public partial decimal Amount { get; set; } = 1;

    public string AmountText => MedicineText.Number(Amount);

    [ObservableProperty]
    public partial int UnitIndex { get; set; }

    [ObservableProperty]
    public partial string StockText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> HomeNames { get; set; } = [];

    [ObservableProperty]
    public partial int HomeIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowHomePicker { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> PhoneNames { get; set; } = [];

    [ObservableProperty]
    public partial int PhoneIndex { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string StockSummary { get; set; } = "";

    private string? _id;
    private bool _loaded;

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _id = query.TryGetValue("id", out var value) ? value as string : null;

    /// <summary>Called when the screen appears (a new medicine has no query, so this can't rely on it).</summary>
    public async Task LoadOnceAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync(_id);
    }

    private async Task LoadAsync(string? id)
    {
        _homes = await homes.GetHomesAsync();
        var current = await homes.GetCurrentAsync();
        HomeNames = _homes.Select(h => h.Name).ToList();

        if (id is not null && await medicines.GetAsync(id) is { } m)
        {
            _editing = m;
            IsEditing = true;
            Title = AppResources.Med_EditTitle;
            Person = m.Person;
            Name = m.Name;
            IsDaily = m.Schedule.Kind == ScheduleKind.Daily;
            IsAlternate = m.Schedule.Kind == ScheduleKind.EveryNDays;
            IsSomeDays = m.Schedule.Kind == ScheduleKind.Weekdays;
            IsAsNeeded = m.Schedule.Kind == ScheduleKind.AsNeeded;
            foreach (var chip in Days)
            {
                chip.IsSelected = m.Schedule.Days?.Contains(chip.Day) == true;
            }

            Times.Clear();
            foreach (var t in m.Schedule.Times.Order())
            {
                Times.Add(new DoseTime(t.ToTimeSpan()));
            }

            Amount = m.DoseAmount;
            UnitIndex = Math.Max(0, Array.IndexOf(MedicineText.Units, m.Unit));
            StockText = m.Stock?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
            StockSummary = MedicineText.Stock(m);
            Notes = m.Notes ?? "";
            HomeIndex = Math.Max(0, _homes.ToList().FindIndex(h => h.Id == m.HomeId));
        }
        else
        {
            HomeIndex = Math.Max(0, _homes.ToList().FindIndex(h => h.Id == current.Id));
        }

        ShowHomePicker = _homes.Count > 1 && _editing is null;
        await LoadPeopleAndPhonesAsync();
    }

    partial void OnHomeIndexChanged(int value) => _ = LoadPeopleAndPhonesAsync();

    /// <summary>People who already have medicines in the chosen home, and the phones that can ring.</summary>
    private async Task LoadPeopleAndPhonesAsync()
    {
        if (HomeIndex < 0 || HomeIndex >= _homes.Count)
        {
            return;
        }

        var homeId = _homes[HomeIndex].Id;
        People.Clear();
        foreach (var person in (await medicines.GetAllAsync()).Where(m => m.HomeId == homeId).Select(m => m.Person).Distinct().Order())
        {
            People.Add(person);
        }

        var me = homes.Me;
        _phones = [(me.DeviceId, string.Format(AppResources.Med_ThisPhone, me.Name))];
        foreach (var device in (await homes.GetDevicesAsync(homeId)).Where(d => d.DeviceId != me.DeviceId).OrderBy(d => d.Name))
        {
            _phones.Add((device.DeviceId, device.Name));
        }

        PhoneNames = _phones.Select(p => p.Name).ToList();
        var ringing = _editing?.ReminderDeviceId ?? me.DeviceId;
        PhoneIndex = Math.Max(0, _phones.FindIndex(p => p.DeviceId == ringing));
    }

    [RelayCommand]
    private void PickPerson(string person) => Person = person;

    [RelayCommand]
    private void ToggleDay(DayChip chip) => chip.IsSelected = !chip.IsSelected;

    [RelayCommand]
    private void AddTime() => Times.Add(new DoseTime(Times.Count == 0 ? new TimeSpan(9, 0, 0) : new TimeSpan(21, 0, 0)));

    [RelayCommand]
    private void RemoveTime(DoseTime time)
    {
        if (Times.Count > 1)
        {
            Times.Remove(time);
        }
    }

    [RelayCommand]
    private void ChangeAmount(string delta) =>
        Amount = Math.Clamp(Amount + decimal.Parse(delta, CultureInfo.InvariantCulture), 0.25m, 50m);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Person) || string.IsNullOrWhiteSpace(Name))
        {
            await dialogs.AlertAsync(Title, AppResources.Med_NeedNames);
            return;
        }

        var days = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList();
        if (IsSomeDays && days.Count == 0)
        {
            await dialogs.AlertAsync(Title, AppResources.Med_NeedDays);
            return;
        }

        decimal? stock = null;
        if (!string.IsNullOrWhiteSpace(StockText))
        {
            if (!decimal.TryParse(StockText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || parsed < 0)
            {
                await dialogs.AlertAsync(Title, AppResources.Med_BadStock);
                return;
            }

            stock = parsed;
        }

        var times = IsAsNeeded ? [] : Times.Select(t => TimeOnly.FromTimeSpan(t.Time)).Distinct().Order().ToList();
        var schedule = IsAsNeeded ? new DoseSchedule(ScheduleKind.AsNeeded, [])
            : IsAlternate ? new DoseSchedule(ScheduleKind.EveryNDays, times, IntervalDays: 2)
            : IsSomeDays ? new DoseSchedule(ScheduleKind.Weekdays, times, Days: days)
            : new DoseSchedule(ScheduleKind.Daily, times);

        var home = _homes[Math.Clamp(HomeIndex, 0, _homes.Count - 1)];
        var medicine = (_editing ?? new Medicine
        {
            Id = Medicine.NewId(),
            HomeId = home.Id,
            Person = Person.Trim(),
            Name = Name.Trim(),
            Schedule = schedule,
            StartDate = DateOnly.FromDateTime(clock.Now.LocalDateTime),
            AddedBy = homes.Me.Name,
        }) with
        {
            Person = Person.Trim(),
            Name = Name.Trim(),
            Schedule = schedule,
            DoseAmount = Amount,
            Unit = MedicineText.Units[Math.Clamp(UnitIndex, 0, MedicineText.Units.Length - 1)],
            Stock = stock,
            ReminderDeviceId = PhoneIndex >= 0 && PhoneIndex < _phones.Count ? _phones[PhoneIndex].DeviceId : homes.Me.DeviceId,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        };

        // New medicine or changed times: doses earlier today don't count as missed.
        if (_editing is null || !SameSchedule(_editing.Schedule, schedule))
        {
            medicine = medicine with { FirstDoseFrom = clock.Now.LocalDateTime };
        }

        await medicines.SaveAsync(medicine);
        await Shell.Current.GoToAsync("..");
    }

    private static bool SameSchedule(DoseSchedule a, DoseSchedule b) =>
        a.Kind == b.Kind && a.IntervalDays == b.IntervalDays
        && a.Times.Order().SequenceEqual(b.Times.Order())
        && (a.Days ?? []).Order().SequenceEqual((b.Days ?? []).Order());

    [RelayCommand]
    private async Task BoughtMoreAsync()
    {
        if (_editing is null)
        {
            return;
        }

        var text = await dialogs.PromptAsync(AppResources.Med_BoughtMore, string.Format(AppResources.Med_BoughtMoreHint, MedicineText.UnitName(_editing.Unit, 2)));
        if (text is null || !decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            return;
        }

        await medicines.AddStockAsync(_editing, amount);
        _editing = await medicines.GetAsync(_editing.Id) ?? _editing;
        StockText = _editing.Stock?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        StockSummary = MedicineText.Stock(_editing);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_editing is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            string.Format(AppResources.Med_DeleteTitle, _editing.Name),
            AppResources.Med_DeleteText,
            AppResources.Common_Delete,
            AppResources.Common_Cancel);
        if (confirmed)
        {
            await medicines.DeleteAsync(_editing);
            await Shell.Current.GoToAsync("..");
        }
    }
}
