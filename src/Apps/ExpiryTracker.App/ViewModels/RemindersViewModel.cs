using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Services;
using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.Items;

namespace ExpiryTracker.App.ViewModels;

/// <summary>"Needs your attention": today's reminder as a screen, with big buttons for each item and for snoozing.</summary>
public sealed partial class RemindersViewModel(ReminderSource reminders, ItemService items, HomeService homes) : ObservableObject
{
    public ObservableCollection<ItemCard> Due { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllDone))]
    public partial bool HasDue { get; set; }

    public bool AllDone => !HasDue;

    [ObservableProperty]
    public partial bool EveningAhead { get; set; }

    public async Task LoadAsync()
    {
        var due = await reminders.GetDueAsync();
        var all = await homes.GetHomesAsync();
        var homeNames = all.ToDictionary(h => h.Id, h => h.Name);
        Due.Clear();
        foreach (var e in due)
        {
            var details = Text.Category(e.Item.CategoryId);
            if (all.Count > 1 && e.Item.HomeId is { } homeId && homeNames.TryGetValue(homeId, out var homeName))
            {
                details = homeName + " · " + details;
            }

            var color = e.Status.State == ExpiryState.Unknown ? Palette.NoDate : Text.StatusColor(e.Status.State);
            Due.Add(new ItemCard(e.Item.Id, e.Item.Name, details, Text.Status(e), color, PhotoStore.ImageOf(e.Item.PhotoPath)));
        }

        HasDue = Due.Count > 0;
        EveningAhead = reminders.EveningAhead;
    }

    [RelayCommand]
    private Task UsedUpAsync(ItemCard card) => CloseItemAsync(card, ClosedReason.UsedUp);

    [RelayCommand]
    private Task ThrownAsync(ItemCard card) => CloseItemAsync(card, ClosedReason.ThrownAway);

    private async Task CloseItemAsync(ItemCard card, ClosedReason reason)
    {
        await items.CloseAsync(card.Id, reason);
        await LoadAsync();
        if (!HasDue)
        {
            await DoneAsync();
        }
    }

    [RelayCommand]
    private async Task SnoozeHourAsync()
    {
        ReminderSource.Snooze(DateTime.Now.AddHours(1));
        await LeaveAsync();
    }

    [RelayCommand]
    private async Task SnoozeEveningAsync()
    {
        reminders.SnoozeUntilEvening();
        await LeaveAsync();
    }

    [RelayCommand]
    private async Task TomorrowAsync()
    {
        await reminders.RemindTomorrowAsync();
        await LeaveAsync();
    }

    /// <summary>"Done": seen and dealt with, no evening repeat.</summary>
    [RelayCommand]
    private async Task DoneAsync()
    {
        reminders.MarkHandled();
        await LeaveAsync();
    }

    private static async Task LeaveAsync()
    {
        WidgetNavigation.ReleaseLockScreen?.Invoke();
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}
