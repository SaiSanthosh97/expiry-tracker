using HomeTracker.Core.Items;
using HomeTracker.Core.ShelfLife;
using SQLite;

namespace HomeTracker.Storage.Sqlite;

/// <summary>Database shape of <see cref="TrackedItem"/>. Dates are stored as day numbers, durations as amount + unit.</summary>
[Table("Items")]
internal sealed class ItemRow
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    [Indexed]
    public string? HomeId { get; set; }

    public string Name { get; set; } = "";

    public string CategoryId { get; set; } = "";

    public int Quantity { get; set; }

    public string? AddedBy { get; set; }

    // New columns are added to existing databases automatically by sqlite-net (CreateTable migrates).
    [Indexed]
    public string? Barcode { get; set; }

    public int AddedDate { get; set; }

    public int? PrintedExpiry { get; set; }

    public int? ManufactureDate { get; set; }

    public int? BestBeforeAmount { get; set; }

    public int? BestBeforeUnit { get; set; }

    public int? ShelfLifeAmount { get; set; }

    public int? ShelfLifeUnit { get; set; }

    public int? OpenedDate { get; set; }

    public int? AfterOpeningAmount { get; set; }

    public int? AfterOpeningUnit { get; set; }

    public int? ReminderLeadDays { get; set; }

    public int? SnoozedUntil { get; set; }

    public int? ClosedDate { get; set; }

    public int? ClosedReason { get; set; }

    public string? PhotoPath { get; set; }

    public long UpdatedAtMs { get; set; }

    [Indexed]
    public bool IsDeleted { get; set; }

    public static ItemRow From(TrackedItem item) => new()
    {
        Id = item.Id,
        HomeId = item.HomeId,
        Name = item.Name,
        CategoryId = item.CategoryId,
        Quantity = item.Quantity,
        AddedBy = item.AddedBy,
        Barcode = item.Barcode,
        AddedDate = item.AddedDate.DayNumber,
        PrintedExpiry = item.PrintedExpiry?.DayNumber,
        ManufactureDate = item.ManufactureDate?.DayNumber,
        BestBeforeAmount = item.BestBeforeFromManufacture?.Amount,
        BestBeforeUnit = (int?)item.BestBeforeFromManufacture?.Unit,
        ShelfLifeAmount = item.ShelfLifeFromAdded?.Amount,
        ShelfLifeUnit = (int?)item.ShelfLifeFromAdded?.Unit,
        OpenedDate = item.OpenedDate?.DayNumber,
        AfterOpeningAmount = item.AfterOpening?.Amount,
        AfterOpeningUnit = (int?)item.AfterOpening?.Unit,
        ReminderLeadDays = item.ReminderLeadDays,
        SnoozedUntil = item.SnoozedUntil?.DayNumber,
        ClosedDate = item.ClosedDate?.DayNumber,
        ClosedReason = (int?)item.ClosedReason,
        PhotoPath = item.PhotoPath,
        UpdatedAtMs = item.UpdatedAt.ToUnixTimeMilliseconds(),
        IsDeleted = item.IsDeleted,
    };

    public TrackedItem ToItem() => new()
    {
        Id = Id,
        HomeId = HomeId,
        Name = Name,
        CategoryId = CategoryId,
        Quantity = Quantity,
        AddedBy = AddedBy,
        Barcode = Barcode,
        AddedDate = DateOnly.FromDayNumber(AddedDate),
        PrintedExpiry = ToDate(PrintedExpiry),
        ManufactureDate = ToDate(ManufactureDate),
        BestBeforeFromManufacture = ToDuration(BestBeforeAmount, BestBeforeUnit),
        ShelfLifeFromAdded = ToDuration(ShelfLifeAmount, ShelfLifeUnit),
        OpenedDate = ToDate(OpenedDate),
        AfterOpening = ToDuration(AfterOpeningAmount, AfterOpeningUnit),
        ReminderLeadDays = ReminderLeadDays,
        SnoozedUntil = ToDate(SnoozedUntil),
        ClosedDate = ToDate(ClosedDate),
        ClosedReason = (Core.Items.ClosedReason?)ClosedReason,
        PhotoPath = PhotoPath,
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtMs),
        IsDeleted = IsDeleted,
    };

    private static DateOnly? ToDate(int? dayNumber) => dayNumber is { } n ? DateOnly.FromDayNumber(n) : null;

    private static ShelfLifeDuration? ToDuration(int? amount, int? unit) =>
        amount is { } a && unit is { } u ? new ShelfLifeDuration(a, (Core.ShelfLife.ShelfLifeUnit)u) : null;
}
