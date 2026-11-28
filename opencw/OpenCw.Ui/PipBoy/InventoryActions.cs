using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum InventoryActionStatus { Done, Refused }

public sealed record InventoryActionResult(InventoryActionStatus Status, string Message)
{
    public IReadOnlyList<string> Skipped { get; init; } = [];

    public bool Done => Status == InventoryActionStatus.Done;
}

public sealed record ConsumableEffect(string EffectId, double Magnitude, int Duration, bool Conditional = false);

public sealed record DroppedItem(string Id, string BaseType, ItemState State, int Count);

public interface IInventoryHost
{
    bool EquipWeapon(string id, ItemState state);

    void UnequipWeapon();

    // Requires: the spawned ref is activatable through ItemPickup; false means nothing was placed and the item stays carried.
    bool SpawnDrop(DroppedItem drop);

    IReadOnlyList<ConsumableEffect>? EffectsOf(string id);

    // Requires: true only when at least one effect took hold; Unapplied names every effect that did not and why.
    bool ApplyEffects(string id, IReadOnlyList<ConsumableEffect> effects);

    IReadOnlyList<string> Unapplied(string id, IReadOnlyList<ConsumableEffect> effects) => [];
}

// Requires: main thread only.
public sealed class InventoryActions(PlayerItems items, IItemRecords records, IRecordReader reader, PlayerEquipment equipment, IInventoryHost host)
{
    public InventoryActionResult Apply(InventoryEvent command) => command.Kind switch
    {
        InventoryEventKind.Equip => Equip(command.ItemId),
        InventoryEventKind.Unequip => Unequip(command.ItemId),
        InventoryEventKind.Use => Use(command.ItemId),
        InventoryEventKind.Drop => Drop(command.ItemId, command.Count),
        _ => Refuse("not an inventory action"),
    };

    public InventoryActionResult Equip(string id, ItemState? state = null)
    {
        var entry = items.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase) && (state is null || e.State.Equals(state)));
        if (entry is null) return Refuse("not carried");
        switch (records.Find(id)?.Signature)
        {
            case "WEAP":
                if (!host.EquipWeapon(entry.Id, entry.State)) return Refuse("cannot equip this weapon");
                equipment.SetWeapon(entry.Id, entry.State);
                return Done("equipped");
            case "ARMO":
                if (ArmorMaskOf(id) is not { } mask) return Refuse("armor slots unreadable");
                equipment.Wear(entry.Id, entry.State, mask);
                return Done("equipped");
            default:
                return Refuse("not equippable");
        }
    }

    public InventoryActionResult Unequip(string id)
    {
        if (equipment.Weapon is { } weapon && string.Equals(weapon.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            host.UnequipWeapon();
            equipment.ClearWeapon();
            return Done("unequipped");
        }
        return equipment.Remove(id) ? Done("unequipped") : Refuse("not equipped");
    }

    public InventoryActionResult Use(string id)
    {
        if (items.CountOf(id) < 1) return Refuse("not carried");
        if (records.Find(id)?.Signature != "ALCH") return Refuse("not usable");
        if (host.EffectsOf(id) is not { } effects) return Refuse("effects unreadable");
        var unapplied = host.Unapplied(id, effects);
        if (!host.ApplyEffects(id, effects)) return Refuse(Detail("effects not applied", unapplied)) with { Skipped = unapplied };
        items.Remove(id, 1);
        equipment.Reconcile(items);
        return Done(Detail("used", unapplied, "; not applied: ")) with { Skipped = unapplied };
    }

    public InventoryActionResult Drop(string id, int count)
    {
        if (count <= 0) return Refuse("nothing to drop");
        if (records.Find(id) is not { } record || PlayerInventoryItems.BaseTypeOf(record.Signature) is not { } baseType) return Refuse("cannot be dropped");
        var drops = new List<DroppedItem>();
        var left = count;
        var carried = 0;
        foreach (var entry in items.Entries.Where(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.State.IsPlain ? 0 : 1).ToArray())
        {
            carried += entry.Count;
            var free = entry.Count - (equipment.IsEquipped(entry.Id, entry.State) ? 1 : 0);
            var take = Math.Min(left, free);
            if (take <= 0) continue;
            drops.Add(new DroppedItem(entry.Id, baseType, entry.State, take));
            left -= take;
        }
        if (left > 0) return Refuse(carried >= count ? "unequip it first" : "not enough to drop");
        var dropped = 0;
        foreach (var drop in drops)
        {
            if (!host.SpawnDrop(drop)) break;
            items.Remove(drop.Id, drop.State, drop.Count);
            dropped += drop.Count;
        }
        if (dropped == 0) return Refuse("cannot be placed here");
        return Done(string.Create(CultureInfo.InvariantCulture, $"dropped {dropped}"));
    }

    private uint? ArmorMaskOf(string id) =>
        uint.TryParse(reader.Scalar(id, PlayerInventoryItems.ArmorMaskPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask) && mask != 0 ? mask : null;

    private static string Detail(string head, IReadOnlyList<string> reasons, string join = ": ") =>
        reasons.Count == 0 ? head : head + join + string.Join("; ", reasons);

    private static InventoryActionResult Done(string message) => new(InventoryActionStatus.Done, message);

    private static InventoryActionResult Refuse(string message) => new(InventoryActionStatus.Refused, message);
}
