using System;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class ContainerText
{
    public const string Empty = "Empty";
    public const string Owned = "Owned";
    public const string Theft = "Owned. Taking is theft.";
    public const string OverEncumbered = "Over-encumbered";
    public const string Changed = "Inventory changed.";
    public const string Unavailable = "unavailable";
    public const string Failed = "That didn't work.";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string Take = "Take";
    public const string Store = "Store";
    public const string Steal = "Steal";
    public const string Place = "Place";
    public const string Unload = "Unload";
    public const string Load = "Load";
    public const string TakeAll = "Take all";
    public const string UnloadAll = "Unload all";
    public const string StealAll = "Steal all";

    public static string Mode(ContainerMode mode) => mode switch
    {
        ContainerMode.Steal => "Steal",
        ContainerMode.Pickpocket => "Pickpocket",
        ContainerMode.Teammate => "Teammate",
        ContainerMode.PowerArmor => "Power armor",
        ContainerMode.Reload => "Reload",
        ContainerMode.Workbench => "Workbench",
        _ => "Loot",
    };

    public static string Verb(ContainerMode mode, bool fromContainer)
    {
        if (mode == ContainerMode.Reload) return fromContainer ? Unload : Load;
        if (mode == ContainerMode.Pickpocket) return fromContainer ? Steal : Place;
        if (mode == ContainerMode.Steal && fromContainer) return Steal;
        return fromContainer ? Take : Store;
    }

    public static string TakeAllLabel(ContainerMode mode) => mode switch
    {
        ContainerMode.Reload => UnloadAll,
        ContainerMode.Steal or ContainerMode.Pickpocket => StealAll,
        _ => TakeAll,
    };

    public static string Header(ContainerMode mode, double weight, double limit)
    {
        var text = "Weight " + NumberText.Format(weight) + (limit > 0 ? " / " + NumberText.Format(limit) : string.Empty);
        var owned = mode == ContainerMode.Steal ? " (" + Owned + ")" : string.Empty;
        return Mode(mode) + owned + " | " + text;
    }

    public static string Count(int count, double weight, int value) =>
        FormattableString.Invariant($"Count {count}, weight {NumberText.Format(weight)}, value {value}");

    public static string ValuePerWeight(double value) => "Value per weight " + NumberText.Format(value);

    public static string Chance(int chance, bool fromContainer) =>
        FormattableString.Invariant($"{chance}% to {(fromContainer ? "steal" : "place")}");

    public static string Quantity(int count, int available) => FormattableString.Invariant($"Quantity {count} of {available}");

    public static string Total(double weight, int value, int count) =>
        "Total weight " + NumberText.Format(weight * count) + ", value " + (value * (long)count).ToString(CultureInfo.InvariantCulture);
}
