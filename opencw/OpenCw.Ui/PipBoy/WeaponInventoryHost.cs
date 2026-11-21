using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; lookup returns null for a record with no readable WEAP.
public sealed class WeaponInventoryHost(WeaponController weapons, PlayerItems items, Func<string, WeaponInfo?> lookup,
                                        PlayerVitals? vitals = null, IMagicEffectRecords? magic = null,
                                        Func<string, IReadOnlyList<ConsumableEffect>?>? effects = null,
                                        Func<DroppedItem, bool>? spawn = null,
                                        Func<string, string, InventoryActionResult?>? read = null) : IInventoryHost
{
    public bool EquipWeapon(string id, ItemState state)
    {
        if (lookup(id) is not { } info) return false;
        var same = string.Equals(weapons.WeaponKey, id, StringComparison.OrdinalIgnoreCase) && weapons.Item.Equals(state);
        var loaded = same ? default : Loaded();
        if (!weapons.Equip(id, info, same ? weapons.Clip : 0, state)) return false;
        Refund(loaded);
        return true;
    }

    public void UnequipWeapon()
    {
        Refund(Loaded());
        weapons.Unequip();
    }

    public bool SpawnDrop(DroppedItem drop) => spawn?.Invoke(drop) == true;

    public InventoryActionResult? Read(string id, string signature) => read?.Invoke(id, signature);

    public IReadOnlyList<ConsumableEffect>? EffectsOf(string id) => effects?.Invoke(id);

    public bool ApplyEffects(string id, IReadOnlyList<ConsumableEffect> applied) =>
        vitals is not null && magic is not null && VitalsEffects.Apply(vitals, applied, magic);

    public IReadOnlyList<string> Unapplied(string id, IReadOnlyList<ConsumableEffect> applied) =>
        vitals is null || magic is null ? ["no vitals store"] : VitalsEffects.Unapplied(applied, magic);

    public ConsumeOutcome Consume(string id, IReadOnlyList<ConsumableEffect> applied) =>
        vitals is null || magic is null ? new ConsumeOutcome(false, ["no vitals store"]) : VitalsEffects.Consume(vitals, applied, magic);

    private (string Ammo, int Rounds) Loaded() =>
        weapons.Weapon is { Kind: WeaponKind.Ranged, AmmoKey: { } ammo } ? (ammo, weapons.Clip) : default;

    private void Refund((string? Ammo, int Rounds) loaded)
    {
        if (loaded.Ammo is { } ammo) items.Add(ammo, loaded.Rounds);
    }
}
