using System.Globalization;
using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Medicines;

namespace ExpiryTracker.App.Services;

/// <summary>Plain sentences about medicines: amounts, schedules, stock.</summary>
public static partial class MedicineText
{
    /// <summary>Units offered when adding a medicine; stored by these keys.</summary>
    public static readonly string[] Units = ["tablet", "capsule", "ml", "drop", "puff", "sachet", "injection"];

    public static string UnitName(string unit, decimal amount)
    {
        var key = "Unit_" + unit + (amount == 1 ? "" : "_Many");
        return Text.Get(key) is { } name && name != key ? name : unit;
    }

    /// <summary>"1 tablet", "½ tablet", "5 ml".</summary>
    public static string Amount(Medicine medicine) => Number(medicine.DoseAmount) + " " + UnitName(medicine.Unit, medicine.DoseAmount);

    public static string Number(decimal value) => value switch
    {
        0.5m => "½",
        0.25m => "¼",
        _ when value % 1 == 0.5m => ((int)value).ToString(CultureInfo.CurrentCulture) + "½",
        _ => value.ToString("0.##", CultureInfo.CurrentCulture),
    };

    public static string Time(TimeOnly time) => time.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>"Every day at 9:00 AM and 9:00 PM", "Mon, Thu at 8:00 AM", "Only when needed".</summary>
    public static string Schedule(DoseSchedule schedule)
    {
        var times = string.Join(", ", schedule.Times.Order().Select(Time));
        return schedule.Kind switch
        {
            ScheduleKind.Daily => string.Format(AppResources.Schedule_Daily, times),
            ScheduleKind.EveryNDays when schedule.IntervalDays == 2 => string.Format(AppResources.Schedule_AlternateDays, times),
            ScheduleKind.EveryNDays => string.Format(AppResources.Schedule_EveryNDays, schedule.IntervalDays, times),
            ScheduleKind.Weekdays => string.Format(AppResources.Schedule_Weekdays,
                string.Join(", ", (schedule.Days ?? []).OrderBy(d => ((int)d + 6) % 7).Select(d => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)d])), times),
            _ => AppResources.Schedule_AsNeeded,
        };
    }

    /// <summary>"12 tablets left · about 6 days", or empty when not counted.</summary>
    public static string Stock(Medicine medicine)
    {
        if (medicine.Stock is not { } stock)
        {
            return "";
        }

        var left = string.Format(AppResources.Stock_Left, Number(stock), UnitName(medicine.Unit, stock));
        return DoseStock.DaysLeft(medicine) is { } days ? left + " · " + string.Format(AppResources.Stock_Days, days) : left;
    }

    public static string Refill(Medicine medicine) => string.Format(AppResources.Refill_Text, Stock(medicine));

    public static string State(DoseSlot slot) => slot.State switch
    {
        DoseState.Taken when string.IsNullOrEmpty(slot.Event?.By) => "✓ " + AppResources.Dose_Taken,
        DoseState.Taken => string.Format(AppResources.DoseState_Taken, slot.Event!.By),
        DoseState.Skipped => AppResources.DoseState_Skipped,
        DoseState.Missed => AppResources.DoseState_Missed,
        DoseState.Due when slot.Event is { Status: DoseStatus.Later } => AppResources.DoseState_Later,
        DoseState.Due => AppResources.DoseState_Due,
        _ => AppResources.DoseState_Upcoming,
    };

    public static Color StateColor(DoseState state) => state switch
    {
        DoseState.Taken => Palette.Fresh,
        DoseState.Missed => Palette.Expired,
        DoseState.Due => Palette.UseSoon,
        _ => Palette.NoDate,
    };
}
