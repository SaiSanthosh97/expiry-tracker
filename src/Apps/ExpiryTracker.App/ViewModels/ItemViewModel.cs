using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpiryTracker.App.Resources.Strings;
using ExpiryTracker.App.Services;
using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;

namespace ExpiryTracker.App.ViewModels;

public sealed record DetailRow(string Label, string Value);

public sealed partial class ItemViewModel(ItemService items, UndoService undo, Dialogs dialogs) : ObservableObject, IQueryAttributable
{
    private static readonly ShelfLifeDuration[] OpeningChoices =
    [
        ShelfLifeDuration.Days(2),
        ShelfLifeDuration.Weeks(1),
        ShelfLifeDuration.Weeks(2),
        ShelfLifeDuration.Months(1),
        ShelfLifeDuration.Months(3),
    ];

    private ItemEvaluation? _evaluation;
    private string? _id;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Category { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial Color StatusColor { get; set; } = Palette.NoDate;

    [ObservableProperty]
    public partial string LastDayText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanMarkOpened { get; set; }

    public ObservableCollection<DetailRow> Details { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && id is string itemId)
        {
            _id = itemId;
        }
    }

    public async Task LoadAsync()
    {
        if (_id is null || await items.GetAsync(_id) is not { } evaluation)
        {
            return;
        }

        _evaluation = evaluation;
        var item = evaluation.Item;
        Name = item.Name;
        Category = Text.Category(item.CategoryId);
        StatusText = Text.Status(evaluation);
        StatusColor = Text.StatusColor(evaluation.Status.State);
        LastDayText = evaluation.Expiry.LastUsableDay is { } last ? string.Format(AppResources.Item_LastDay, Text.Date(last)) : "";
        // Loose items (sweets, leftovers) are counted from the day they were added; "opened" means nothing for them.
        CanMarkOpened = item.OpenedDate is null && item.ShelfLifeFromAdded is null;

        Details.Clear();
        Details.Add(new DetailRow(AppResources.Item_Added, Text.Date(item.AddedDate)));
        if (item.PrintedExpiry is { } printed)
        {
            Details.Add(new DetailRow(AppResources.Item_PackExpiry, Text.Date(printed)));
        }

        if (item.ManufactureDate is { } made)
        {
            var goodFor = item.BestBeforeFromManufacture is { } bb ? $" + {Text.Duration(bb)}" : "";
            Details.Add(new DetailRow(AppResources.Item_MadeOn, Text.Date(made) + goodFor));
        }

        if (item.OpenedDate is { } opened)
        {
            var rule = item.AfterOpening ?? evaluation.Category?.AfterOpening;
            Details.Add(new DetailRow(AppResources.Item_Opened, Text.Date(opened) + (rule is { } r ? $" + {Text.Duration(r)}" : "")));
        }

        if (item.Quantity > 1)
        {
            Details.Add(new DetailRow(AppResources.Item_Quantity, item.Quantity.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        }
    }

    [RelayCommand]
    private async Task MarkOpenedAsync()
    {
        if (_evaluation is null)
        {
            return;
        }

        ShelfLifeDuration? choice = null;
        if (_evaluation.Category?.AfterOpening is null)
        {
            var labels = OpeningChoices.Select(Text.Duration).Append(AppResources.Opened_KeepPackDate).ToArray();
            var picked = await dialogs.ChooseAsync(AppResources.Opened_AskTitle, labels);
            if (picked is null)
            {
                return;
            }

            var index = Array.IndexOf(labels, picked);
            choice = index < OpeningChoices.Length ? OpeningChoices[index] : null;
        }

        await items.MarkOpenedAsync(_evaluation.Item.Id, choice);
        await LoadAsync();
    }

    [RelayCommand]
    private Task UsedUpAsync() => CloseAsync(ClosedReason.UsedUp, AppResources.Undo_UsedUp);

    [RelayCommand]
    private Task ThrownAsync() => CloseAsync(ClosedReason.ThrownAway, AppResources.Undo_Thrown);

    private async Task CloseAsync(ClosedReason reason, string undoFormat)
    {
        if (_evaluation is null)
        {
            return;
        }

        await items.CloseAsync(_evaluation.Item.Id, reason);
        undo.Offer(_evaluation.Item.Id, string.Format(undoFormat, _evaluation.Item.Name));
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task EditAsync() =>
        _evaluation is null ? Task.CompletedTask : Shell.Current.GoToAsync($"add?id={_evaluation.Item.Id}");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_evaluation is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            string.Format(AppResources.Item_DeleteTitle, _evaluation.Item.Name),
            AppResources.Item_DeleteText,
            AppResources.Common_Delete,
            AppResources.Common_Cancel);
        if (confirmed)
        {
            await items.DeleteAsync(_evaluation.Item.Id);
            await Shell.Current.GoToAsync("..");
        }
    }
}
