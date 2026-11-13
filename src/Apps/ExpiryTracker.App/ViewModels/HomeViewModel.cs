using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.ViewModels;

public sealed record ItemCard(string Id, string Name, string Details, string StatusText, Color StatusColor, ImageSource? Photo)
{
    public bool HasPhoto => Photo is not null;
}

public sealed class ItemGroup(string title, Color color, IEnumerable<ItemCard> cards) : List<ItemCard>(cards)
{
    public string Title { get; } = title;

    public Color Color { get; } = color;
}

public sealed partial class FilterChip(string label, CategoryGroup? group) : ObservableObject
{
    public string Label { get; } = label;

    public CategoryGroup? Group { get; } = group;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ItemService _items;
    private readonly HomeService _homes;
    private readonly UndoService _undo;
    private CancellationTokenSource? _undoTimer;
    private string? _undoItemId;

    public HomeViewModel(ItemService items, HomeService homes, FamilySyncService sync, UndoService undo)
    {
        _items = items;
        _homes = homes;
        _undo = undo;
        Filters =
        [
            new FilterChip(AppResources.Filter_All, null) { IsSelected = true },
            new FilterChip(AppResources.Filter_Food, CategoryGroup.Food),
            new FilterChip(AppResources.Filter_Medicines, CategoryGroup.Medicine),
            new FilterChip(AppResources.Filter_Other, CategoryGroup.Other),
        ];
        _items.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
        _homes.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
        sync.Synced += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
    }

    public ObservableCollection<FilterChip> Filters { get; }

    /// <summary>The app name, or the home's name when this phone is in more than one home.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = AppResources.AppName;

    public ObservableCollection<ItemGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo))]
    public partial string? UndoMessage { get; set; }

    public bool HasUndo => UndoMessage is not null;

    public async Task LoadAsync()
    {
        var group = Filters.First(f => f.IsSelected).Group;
        var home = await _homes.GetCurrentAsync();
        Title = (await _homes.GetHomesAsync()).Count > 1 ? home.Name : AppResources.AppName;
        var sections = ItemLists.ForHome(await _items.GetOpenAsync(home.Id), group);

        Groups.Clear();
        AddGroup(AppResources.Section_Expired, Palette.Expired, sections.Expired);
        AddGroup(AppResources.Section_UseSoon, Palette.UseSoon, sections.UseSoon);
        AddGroup(AppResources.Section_Fresh, Palette.Fresh, sections.Fresh);
        AddGroup(AppResources.Section_NoDate, Palette.NoDate, sections.NoDate);
        IsEmpty = Groups.Count == 0;

        ShowPendingUndo();
    }

    private void AddGroup(string title, Color color, IReadOnlyList<ItemEvaluation> evaluations)
    {
        if (evaluations.Count > 0)
        {
            Groups.Add(new ItemGroup(title, color, evaluations.Select(ToCard)));
        }
    }

    private static ItemCard ToCard(ItemEvaluation e)
    {
        var details = Text.Category(e.Item.CategoryId);
        if (e.Item.OpenedDate is not null)
        {
            details += " · " + AppResources.Status_Opened;
        }

        if (e.Item.Quantity > 1)
        {
            details += $" · ×{e.Item.Quantity}";
        }

        var color = e.Status.State == ExpiryState.Unknown ? Palette.NoDate : Text.StatusColor(e.Status.State);
        return new ItemCard(e.Item.Id, e.Item.Name, details, Text.Status(e), color, PhotoStore.ImageOf(e.Item.PhotoPath));
    }

    private void ShowPendingUndo()
    {
        if (_undo.Take() is not { } pending || (HasUndo && pending.ItemId == _undoItemId))
        {
            return;
        }

        _undoItemId = pending.ItemId;
        UndoMessage = pending.Message;

        _undoTimer?.Cancel();
        _undoTimer = new CancellationTokenSource();
        var token = _undoTimer.Token;
        _ = Task.Delay(UndoService.Window, token).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(HideUndo),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    // Only hides the bar: an action from the widget stays undoable for a few minutes (UndoService expires it).
    private void HideUndo()
    {
        UndoMessage = null;
        _undoItemId = null;
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_undoItemId is { } id)
        {
            _undoTimer?.Cancel();
            HideUndo();
            _undo.Clear();
            await _items.ReopenAsync(id);
        }
    }

    [RelayCommand]
    private async Task SelectFilterAsync(FilterChip chip)
    {
        foreach (var filter in Filters)
        {
            filter.IsSelected = filter == chip;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private Task AddAsync() => Shell.Current.GoToAsync("add");

    [RelayCommand]
    private Task OpenAsync(ItemCard card) => Shell.Current.GoToAsync($"item?id={card.Id}");

    [RelayCommand]
    private Task SettingsAsync() => Shell.Current.GoToAsync("settings");

    [RelayCommand]
    private Task FamilyAsync() => Shell.Current.GoToAsync("family");
}
