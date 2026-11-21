using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PlayerInventoryItems
{
    public const string WeaponSlot = "Weapon";
    public const string ArmorMaskPath = "ARMO\\FirstPersonFlags";

    public static IReadOnlyList<InventoryItem> Build(IEnumerable<InventoryStack> stacks, IItemRecords records,
                                                     PlayerEquipment? equipment = null, IRecordReader? reader = null,
                                                     Func<string, bool>? usable = null,
                                                     Func<string, IReadOnlyList<ConsumableEffect>?>? effects = null,
                                                     PlayerItems? items = null) =>
        stacks.Where(stack => stack.Count > 0).Select(stack =>
        {
            var record = records.Find(stack.Id);
            var signature = record?.Signature;
            var armorMask = signature == "ARMO" ? ArmorMaskOf(reader, stack.Id) : null;
            var slot = equipment is null ? null : signature == "WEAP" ? WeaponSlot : armorMask is { } mask ? ArmorSlot(mask) : null;
            return new InventoryItem(stack.Id, record?.Name ?? stack.Id, CategoryOf(signature), record?.Weight ?? 0, record?.Value ?? 0, stack.Count,
                                     Equipped: equipment?.IsEquipped(stack.Id) == true, EquipSlot: slot,
                                     Equippable: slot is not null, Usable: signature is "BOOK" or "NOTE" || signature == "ALCH" && usable?.Invoke(stack.Id) == true,
                                     Stats: signature == "ALCH" ? EffectStats(effects?.Invoke(stack.Id), reader) : null,
                                     Copies: items?.Entries.Count(entry => string.Equals(entry.Id, stack.Id, StringComparison.OrdinalIgnoreCase)) ?? 1);
        }).ToArray();

    public static IReadOnlyList<ItemStat>? EffectStats(IReadOnlyList<ConsumableEffect>? effects, IRecordReader? reader)
    {
        if (effects is not { Count: > 0 }) return null;
        var stats = new List<ItemStat>();
        foreach (var effect in effects)
        {
            var name = reader?.Name(effect.EffectId) ?? reader?.EditorId(effect.EffectId) ?? effect.EffectId;
            if (effect.Conditional) name += " (conditional)";
            stats.Add(new ItemStat(name, effect.Magnitude));
            if (effect.Duration > 0) stats.Add(new ItemStat(name + " duration", effect.Duration));
        }
        return stats;
    }

    public static string CategoryOf(string? signature) => ItemRecordTypes.Category(signature);

    public static string? BaseTypeOf(string? signature) => ItemRecordTypes.PickupBaseType(signature);

    private static uint? ArmorMaskOf(IRecordReader? reader, string id) =>
        uint.TryParse(reader?.Scalar(id, ArmorMaskPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask) && mask != 0 ? mask : null;

    private static string ArmorSlot(uint mask) =>
        string.Create(CultureInfo.InvariantCulture, $"Armor {System.Numerics.BitOperations.TrailingZeroCount(mask)}");
}
