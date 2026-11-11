namespace HomeTracker.Core.ShelfLife;

/// <summary>
/// Built-in categories. Conservative, editable starting points shown to the user;
/// households can change any of them.
/// </summary>
public static class DefaultPresets
{
    // Items usually bought without a printed date.
    public static readonly ShelfLifeCategory Sweets = new("sweets", CategoryGroup.Food, ShelfLife: ShelfLifeDuration.Days(3));
    public static readonly ShelfLifeCategory MilkSweets = new("milk-sweets", CategoryGroup.Food, ShelfLife: ShelfLifeDuration.Days(2));
    public static readonly ShelfLifeCategory Leftovers = new("leftovers", CategoryGroup.Food, ShelfLife: ShelfLifeDuration.Days(1));
    public static readonly ShelfLifeCategory Bakery = new("bakery", CategoryGroup.Food, ShelfLife: ShelfLifeDuration.Days(3));
    public static readonly ShelfLifeCategory CutFruit = new("cut-fruit", CategoryGroup.Food, ShelfLife: ShelfLifeDuration.Days(1));

    // Packaged items that change once opened.
    public static readonly ShelfLifeCategory Milk = new("milk", CategoryGroup.Food, AfterOpening: ShelfLifeDuration.Days(2));
    public static readonly ShelfLifeCategory SaucesAndKetchup = new("sauces", CategoryGroup.Food, AfterOpening: ShelfLifeDuration.Days(30));
    public static readonly ShelfLifeCategory JamAndSpreads = new("jam", CategoryGroup.Food, AfterOpening: ShelfLifeDuration.Days(30));
    public static readonly ShelfLifeCategory PackagedFood = new("packaged-food", CategoryGroup.Food);
    public static readonly ShelfLifeCategory Chocolates = new("chocolates", CategoryGroup.Food);

    // Medicines: only the widely printed eye/ear drop rule; everything else comes from the pack.
    public static readonly ShelfLifeCategory Tablets = new("tablets", CategoryGroup.Medicine);
    public static readonly ShelfLifeCategory Syrups = new("syrups", CategoryGroup.Medicine);
    public static readonly ShelfLifeCategory EyeEarDrops = new("eye-ear-drops", CategoryGroup.Medicine, AfterOpening: ShelfLifeDuration.Days(28));
    public static readonly ShelfLifeCategory Ointments = new("ointments", CategoryGroup.Medicine);

    public static readonly ShelfLifeCategory Cosmetics = new("cosmetics", CategoryGroup.Other);
    public static readonly ShelfLifeCategory OtherItems = new("other", CategoryGroup.Other);

    public static IReadOnlyList<ShelfLifeCategory> All { get; } =
    [
        Sweets, MilkSweets, Leftovers, Bakery, CutFruit,
        Milk, SaucesAndKetchup, JamAndSpreads, PackagedFood, Chocolates,
        Tablets, Syrups, EyeEarDrops, Ointments,
        Cosmetics, OtherItems,
    ];
}
