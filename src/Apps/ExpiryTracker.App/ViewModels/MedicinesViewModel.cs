using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Medicines;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.ViewModels;

/// <summary>One dose on today's list.</summary>
public sealed record DoseRow(DoseSlot Slot, string Time, string Title, string Detail, string StateText, Color StateColor);

/// <summary>One medicine in the lists below today's doses.</summary>
public sealed record MedicineRow(Medicine Medicine, string Title, string Detail, string StockText, Color StockColor)
{
    public bool HasStock => StockText.Length > 0;
}

/// <summary>Medicines tab: today's doses for everyone in the shown home(s), when-needed medicines, and all medicines.</summary>
public sealed partial class MedicinesViewModel : ObservableObject
{
    private readonly MedicineService _medicines;
    private readonly HomeService _homes;
    private readonly FamilySyncService _sync;
    private readonly Dialogs _dialogs;
    private readonly ReminderHealth _health;

    public MedicinesViewModel(MedicineService medicines, HomeService homes, FamilySyncService sync, Dialogs dialogs, ReminderHealth health)
    {
        _health = health;
        _medicines = medicines;
        _homes = homes;
        _sync = sync;
        _dialogs = dialogs;
        _medicines.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
        _homes.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
        _sync.Synced += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
    }

    public ObservableCollection<HomeChip> HomeChips { get; } = [];

    public ObservableCollection<DoseRow> Today { get; } = [];

    public ObservableCollection<MedicineRow> WhenNeeded { get; } = [];

    public ObservableCollection<MedicineRow> All { get; } = [];

    [ObservableProperty]
    public partial bool HasManyHomes { get; set; }

    [ObservableProperty]
    public partial bool HasToday { get; set; }

    [ObservableProperty]
    public partial bool HasWhenNeeded { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool NeedsOnTimeAlarms { get; set; }

    [ObservableProperty]
    public partial bool NeedsNotifications { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    public async Task LoadAsync()
    {
        var homes = await _homes.GetHomesAsync();
        var current = await _homes.GetCurrentAsync();
        var viewAll = HomeService.ViewAll && homes.Count > 1;
        HasManyHomes = homes.Count > 1;
        HomeChips.Clear();
        if (HasManyHomes)
        {
            HomeChips.Add(new HomeChip(null, AppResources.Home_AllHomes) { IsSelected = viewAll });
            foreach (var h in homes)
            {
                HomeChips.Add(new HomeChip(h.Id, h.Name) { IsSelected = !viewAll && h.Id == current.Id });
            }
        }

        var homeNames = homes.ToDictionary(h => h.Id, h => h.Name);
        var meds = (await _medicines.GetAllAsync())
            .Where(m => viewAll || m.HomeId == current.Id)
            .OrderBy(m => m.Person, StringComparer.CurrentCulture)
            .ThenBy(m => m.Name, StringComparer.CurrentCulture)
            .ToList();
        string Where(Medicine m) => viewAll && homeNames.TryGetValue(m.HomeId, out var name) ? name + " · " : "";

        Today.Clear();
        var slots = await _medicines.GetTodayAsync(meds);
        foreach (var slot in slots)
        {
            Today.Add(new DoseRow(
                slot,
                slot.ScheduledAt.ToString("t", CultureInfo.CurrentCulture),
                $"{slot.Medicine.Person} · {slot.Medicine.Name}",
                Where(slot.Medicine) + MedicineText.Amount(slot.Medicine),
                MedicineText.State(slot),
                MedicineText.StateColor(slot.State)));
        }

        var taken = slots.Count(s => s.State == DoseState.Taken);
        Summary = slots.Count == 0 ? "" : string.Format(AppResources.Meds_Summary, taken, slots.Count);

        WhenNeeded.Clear();
        All.Clear();
        foreach (var m in meds)
        {
            var row = new MedicineRow(m, $"{m.Person} · {m.Name}", Where(m) + MedicineText.Schedule(m.Schedule),
                MedicineText.Stock(m), DoseStock.NeedsRefill(m) ? Palette.Expired : Palette.NoDate);
            All.Add(row);
            if (m.Schedule.Kind == ScheduleKind.AsNeeded)
            {
                WhenNeeded.Add(row);
            }
        }

        HasToday = Today.Count > 0;
        HasWhenNeeded = WhenNeeded.Count > 0;
        IsEmpty = All.Count == 0;
        NeedsNotifications = !IsEmpty && !_health.NotificationsEnabled;
        NeedsOnTimeAlarms = !IsEmpty && !NeedsNotifications && !TimedReminders.CanRingOnTime;
    }

    [RelayCommand]
    private async Task SelectHomeAsync(HomeChip chip)
    {
        if (chip.Id is null)
        {
            _homes.SetViewAll();
        }
        else
        {
            _homes.SetCurrent(chip.Id);
        }

        await LoadAsync();
    }

    /// <summary>Mark a dose from the list (e.g. Appa took it while his phone was in another room).</summary>
    [RelayCommand]
    private async Task MarkAsync(DoseRow row)
    {
        var choice = await _dialogs.ChooseAsync(
            string.Format(AppResources.Meds_MarkTitle, row.Slot.Medicine.Person, row.Slot.Medicine.Name, row.Time),
            AppResources.Dose_Taken,
            AppResources.Dose_Skip);
        if (choice is null)
        {
            return;
        }

        await _medicines.RecordAsync(row.Slot.Medicine, row.Slot.ScheduledAt, choice == AppResources.Dose_Taken ? DoseStatus.Taken : DoseStatus.Skipped);
    }

    [RelayCommand]
    private async Task TakeNowAsync(MedicineRow row)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            row.Title,
            string.Format(AppResources.Meds_TakeNowConfirm, MedicineText.Amount(row.Medicine)),
            AppResources.Dose_Taken,
            AppResources.Common_Cancel);
        if (confirmed)
        {
            await _medicines.TakeNowAsync(row.Medicine);
        }
    }

    [RelayCommand]
    private Task OpenAsync(MedicineRow row) => Shell.Current.GoToAsync($"medicine?id={row.Medicine.Id}");

    [RelayCommand]
    private Task AddAsync() => Shell.Current.GoToAsync("medicine");

    [RelayCommand]
    private async Task FixNotificationsAsync()
    {
        if (!await _health.RequestNotificationsAsync())
        {
            _health.OpenAppSettings();
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void FixOnTimeAlarms()
    {
        if (Platform.CurrentActivity is { } activity)
        {
            TimedReminders.OpenOnTimeSettings(activity);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _sync.RefreshAsync();
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task HomesAsync() => Shell.Current.GoToAsync("homes");
}
