using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.ExpiryEngine;
using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.ViewModels;

/// <summary>A home to show on the main screen; Id null means "All homes".</summary>
public sealed partial class HomeChip(string? id, string label) : ObservableObject
{
    public string? Id { get; } = id;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

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
    private readonly FamilySyncService _sync;
    private readonly UndoService _undo;
    private CancellationTokenSource? _undoTimer;
    private string? _undoItemId;

    public HomeViewModel(ItemService items, HomeService homes, FamilySyncService sync, UndoService undo)
    {
        _items = items;
        _homes = homes;
        _sync = sync;
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

    [ObservableProperty]
    public partial string Title { get; set; } = AppResources.AppName;

    /// <summary>"All homes" plus each home; shown when this phone is in more than one home.</summary>
    public ObservableCollection<HomeChip> HomeChips { get; } = [];

    [ObservableProperty]
    public partial bool HasManyHomes { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

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
        var all = await _homes.GetHomesAsync();
        var home = await _homes.GetCurrentAsync();
        var viewAll = HomeService.ViewAll && all.Count > 1;
        HasManyHomes = all.Count > 1;
        HomeChips.Clear();
        if (HasManyHomes)
        {
            HomeChips.Add(new HomeChip(null, AppResources.Home_AllHomes) { IsSelected = viewAll });
            foreach (var h in all)
            {
                HomeChips.Add(new HomeChip(h.Id, h.Name) { IsSelected = !viewAll && h.Id == home.Id });
            }
        }

        _homeNames = viewAll ? all.ToDictionary(h => h.Id, h => h.Name) : null;
        _myName = _homes.Me.Name;
        var sections = ItemLists.ForHome(viewAll ? await _items.GetOpenAsync() : await _items.GetOpenAsync(home.Id), group);

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

    private Dictionary<string, string>? _homeNames;
    private string _myName = "";

    private ItemCard ToCard(ItemEvaluation e)
    {
        var details = Text.Category(e.Item.CategoryId);
        // "All homes": say which home; and who added it, when it wasn't this person.
        if (_homeNames is not null && e.Item.HomeId is { } homeId && _homeNames.TryGetValue(homeId, out var homeName))
        {
            details = homeName + " · " + details;
        }

        if (!string.IsNullOrEmpty(e.Item.AddedBy) && e.Item.AddedBy != _myName)
        {
            details += " · " + string.Format(AppResources.Item_By, e.Item.AddedBy);
        }

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

    /// <summary>Fetches family changes now instead of waiting for the next automatic check.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _sync.SyncNowAsync();
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task AddAsync() => Shell.Current.GoToAsync("add");

    [RelayCommand]
    private Task OpenAsync(ItemCard card) => Shell.Current.GoToAsync($"item?id={card.Id}");

    [RelayCommand]
    private Task SettingsAsync() => Shell.Current.GoToAsync("settings");

    [RelayCommand]
    private Task FamilyAsync() => Shell.Current.GoToAsync("homes");
}
