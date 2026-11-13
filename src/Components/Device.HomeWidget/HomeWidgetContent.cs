namespace HomeTracker.Device.HomeWidget;

/// <summary>One line on the widget.</summary>
/// <param name="Id">Passed back when the row or its button is tapped.</param>
/// <param name="Title">Big text, e.g. the item name.</param>
/// <param name="Detail">Small coloured text, e.g. "Expires tomorrow".</param>
/// <param name="Argb">Colour of the detail text and the dot, as 0xAARRGGBB.</param>
public sealed record HomeWidgetRow(string Id, string Title, string Detail, int Argb);

/// <param name="Title">Header, e.g. "2 need attention".</param>
/// <param name="Rows">Shown in order; only the first <see cref="HomeWidget.MaxRows"/> fit.</param>
/// <param name="EmptyText">Shown when there are no rows.</param>
public sealed record HomeWidgetContent(string Title, IReadOnlyList<HomeWidgetRow> Rows, string EmptyText);

/// <param name="ActionLabel">Text of the button on each row, e.g. "Used".</param>
/// <param name="AddLabel">Text of the header button, e.g. "+ Add".</param>
public sealed record HomeWidgetOptions(string ActionLabel, string AddLabel);

/// <summary>What the widget shows and what its row button does. Implemented by the app.</summary>
public interface IHomeWidgetSource
{
    Task<HomeWidgetContent> BuildAsync(DateOnly today);

    /// <summary>The row button was tapped.</summary>
    /// <returns>A short confirmation to show (e.g. "Milk: used up"), or null.</returns>
    Task<string?> HandleActionAsync(string rowId);
}

/// <summary>What the user tapped on the widget to open the app.</summary>
/// <param name="RowId">The tapped row, or null when the Add button was tapped.</param>
public sealed record HomeWidgetOpenRequest(string? RowId)
{
    public bool IsAdd => RowId is null;
}
