using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record EquippedArmor(string Id, ItemState State, uint Mask);

public sealed record EquippedWeapon(string Id, ItemState State);

// Requires: main thread only.
public sealed class PlayerEquipment
{
    private readonly List<EquippedArmor> _armor = new();

    public EquippedWeapon? Weapon { get; private set; }

    public IReadOnlyList<EquippedArmor> Armor => _armor;

    public bool IsEquipped(string id) =>
        (Weapon is { } weapon && Same(weapon.Id, id)) || _armor.Exists(worn => Same(worn.Id, id));

    public bool IsEquipped(string id, ItemState state) =>
        (Weapon is { } weapon && Same(weapon.Id, id) && weapon.State.Equals(state))
        || _armor.Exists(worn => Same(worn.Id, id) && worn.State.Equals(state));

    public void SetWeapon(string id, ItemState state) => Weapon = new EquippedWeapon(id, state);

    public bool ClearWeapon()
    {
        var had = Weapon is not null;
        Weapon = null;
        return had;
    }

    public IReadOnlyList<EquippedArmor> Wear(string id, ItemState state, uint mask)
    {
        var displaced = _armor.Where(worn => (worn.Mask & mask) != 0 || (Same(worn.Id, id) && worn.State.Equals(state))).ToArray();
        foreach (var worn in displaced) _armor.Remove(worn);
        _armor.Add(new EquippedArmor(id, state, mask));
        return displaced;
    }

    public void Restore(IEnumerable<EquippedArmor> worn, PlayerItems items)
    {
        _armor.Clear();
        foreach (var piece in worn) Wear(piece.Id, piece.State, piece.Mask);
        Reconcile(items);
    }

    public bool Remove(string id) => _armor.RemoveAll(worn => Same(worn.Id, id)) > 0;

    public void Reconcile(PlayerItems items)
    {
        if (Weapon is { } weapon && items.CountOf(weapon.Id, weapon.State) == 0) Weapon = null;
        _armor.RemoveAll(worn => items.CountOf(worn.Id, worn.State) == 0);
    }

    public void Clear()
    {
        Weapon = null;
        _armor.Clear();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
