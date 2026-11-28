using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PlayerStatusFacts
{
    public const string ActionPointsKey = "fact:ap";
    public const string WeaponKey = "fact:weapon";
    public const string ArmorKeyPrefix = "fact:armor:";
    public const string WeightKey = "fact:weight";

    // Constraint: a row exists only when its source is known; the weight row needs a record weight for every carried stack.
    public static IReadOnlyList<StatusRow> Build(IEnumerable<InventoryStack> stacks, IItemRecords items, PlayerEquipment? equipment,
                                                 double? actionPoints, double? maxActionPoints)
    {
        var rows = new List<StatusRow>();
        if (actionPoints is { } ap && maxActionPoints is { } max && double.IsFinite(ap) && double.IsFinite(max) && max > 0)
            rows.Add(new StatusRow(ActionPointsKey, "Action points", Whole(Math.Clamp(ap, 0, max)) + "/" + Whole(max)));
        if (equipment?.Weapon is { } weapon)
            rows.Add(new StatusRow(WeaponKey, "Weapon", NameOf(items, weapon.Id)));
        if (equipment is not null)
            foreach (var worn in equipment.Armor)
                rows.Add(new StatusRow(ArmorKeyPrefix + worn.Id, "Armor", NameOf(items, worn.Id)));
        if (Weight(stacks, items) is { } weight)
            rows.Add(new StatusRow(WeightKey, "Carry weight", weight.ToString("0.##", CultureInfo.InvariantCulture)));
        return rows;
    }

    // Invariant: the total of weight times count over every stack, null when any carried stack has no record weight; an empty inventory weighs 0.
    public static double? Weight(IEnumerable<InventoryStack> stacks, IItemRecords items)
    {
        var total = 0.0;
        foreach (var stack in stacks.Where(stack => stack.Count > 0))
        {
            if (items.Find(stack.Id)?.Weight is not { } weight || !double.IsFinite(weight))
                return null;
            total += weight * stack.Count;
        }
        return total;
    }

    private static string NameOf(IItemRecords items, string id) => items.Find(id)?.Name is { Length: > 0 } name ? name : id;

    private static string Whole(double value) => Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
}
