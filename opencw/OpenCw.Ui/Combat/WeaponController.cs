using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Combat;

public enum WeaponState { None, Sheathed, Drawing, Ready, Attacking, Reloading, Sheathing }

public enum FireResult { Fired, NotReady, NoWeapon, Empty, Busy }

public enum AttackKind { Shot, Swing, Throw, Mine }

public sealed record WeaponAttack(string WeaponKey, AttackKind Kind, double Damage, double AmmoDamage, int Pellets, double Reach, double MaxRange, WeaponRecoil? Recoil = null)
{
    public double TotalDamage => Damage + AmmoDamage;
}

// Requires: main thread only; Update(dt) drives every timer. Draw/sheathe durations come from the caller (animation events).
public sealed class WeaponController(PlayerItems store, double drawSeconds = 0, double sheatheSeconds = 0, IObjectModRecords? mods = null)
{
    private double _timer;
    private bool _pendingDraw;
    private WeaponInfo? _unmodded;

    public string? WeaponKey { get; private set; }
    public WeaponInfo? Weapon { get; private set; }
    public ItemState Item { get; private set; } = ItemState.Plain;
    public WeaponState State { get; private set; }
    public int Clip { get; private set; }

    public event Action<WeaponAttack>? Attacked;

    public bool IsDrawn => State is WeaponState.Drawing or WeaponState.Ready or WeaponState.Attacking or WeaponState.Reloading;

    public int Reserve => Weapon?.Kind switch
    {
        WeaponKind.Ranged when Weapon.AmmoKey is { } ammo => store.CountOf(ammo),
        WeaponKind.Thrown or WeaponKind.Mine => store.CountOf(WeaponKey!),
        _ => 0,
    };

    public bool Equip(string weaponKey, WeaponInfo info, int clip = 0, ItemState? item = null)
    {
        if (info.Kind == WeaponKind.Other || info.NotPlayable) return false;
        var wasDrawn = IsDrawn || _pendingDraw;
        var same = string.Equals(WeaponKey, weaponKey, StringComparison.OrdinalIgnoreCase);
        var keepState = same && State is WeaponState.Attacking or WeaponState.Drawing;
        var timer = _timer;
        var state = State;
        WeaponKey = weaponKey;
        Item = item ?? ItemState.Plain;
        _unmodded = info;
        Weapon = Modded(info, Item);
        Clip = Weapon.Kind == WeaponKind.Ranged ? Math.Clamp(clip, 0, Weapon.Capacity) : 0;
        _pendingDraw = false;
        if (keepState) { _timer = timer; State = state; return true; }
        _timer = 0;
        State = WeaponState.Sheathed;
        if (wasDrawn) Draw();
        return true;
    }

    // Constraint: the equipped weapon must stay in the store; caller runs this each tick and after any store change.
    public void SyncHeld()
    {
        if (WeaponKey is not { } key) return;
        if (store.CountOf(key) == 0) { Unequip(); return; }
        if (store.CountOf(key, Item) > 0) return;
        Item = store.Entries.First(e => string.Equals(e.Id, key, StringComparison.OrdinalIgnoreCase)).State;
        if (_unmodded is null) return;
        Weapon = Modded(_unmodded, Item);
        Clip = Weapon.Kind == WeaponKind.Ranged ? Math.Min(Clip, Weapon.Capacity) : 0;
    }

    public WeaponSave? Capture() => WeaponKey is null ? null : new WeaponSave(WeaponKey, Clip, IsDrawn, Item);

    // Requires: player items already restored (the ammo reserve reads them).
    public void Restore(WeaponSave? save, Func<string, WeaponInfo?> lookup)
    {
        Unequip();
        if (save is null || store.CountOf(save.Key) == 0 || lookup(save.Key) is not { } info || !Equip(save.Key, info, save.Clip, save.Item)) return;
        SyncHeld();
        if (save.Drawn) Draw();
    }

    public void Unequip()
    {
        WeaponKey = null;
        Weapon = null;
        _unmodded = null;
        Item = ItemState.Plain;
        Clip = 0;
        _timer = 0;
        _pendingDraw = false;
        State = WeaponState.None;
    }

    public void Draw()
    {
        if (Weapon is null) return;
        if (State == WeaponState.Sheathing) { _pendingDraw = true; return; }
        if (State != WeaponState.Sheathed) return;
        _timer = drawSeconds;
        State = _timer > 0 ? WeaponState.Drawing : WeaponState.Ready;
    }

    public void Sheathe()
    {
        _pendingDraw = false;
        if (!IsDrawn) return;
        _timer = sheatheSeconds;
        State = _timer > 0 ? WeaponState.Sheathing : WeaponState.Sheathed;
    }

    public FireResult Fire()
    {
        if (Weapon is not { } w) return FireResult.NoWeapon;
        if (State is WeaponState.Attacking or WeaponState.Reloading) return FireResult.Busy;
        if (State != WeaponState.Ready) return FireResult.NotReady;
        switch (w.Kind)
        {
            case WeaponKind.Ranged:
                if (Clip <= 0) return FireResult.Empty;
                Clip--;
                return Attack(AttackKind.Shot, w.Scaled(w.FireSeconds));
            case WeaponKind.Melee:
                return Attack(AttackKind.Swing, w.Scaled(w.AttackSeconds));
            default:
                var key = WeaponKey!;
                if (!store.Remove(key, Item, 1) && !store.Remove(key, 1)) return FireResult.Empty;
                var kind = w.Kind == WeaponKind.Mine ? AttackKind.Mine : AttackKind.Throw;
                var result = Attack(kind, w.Scaled(w.AttackSeconds));
                if (store.CountOf(key) == 0 && WeaponKey == key) Unequip();
                return result;
        }
    }

    // Requires: ranged weapon with a round in the clip; takes the round Fire takes but starts no attack, cooldown or Attacked event.
    public bool TryConsumeRound()
    {
        if (Weapon is not { Kind: WeaponKind.Ranged } || Clip <= 0) return false;
        Clip--;
        return true;
    }

    public bool Reload()
    {
        if (Weapon is not { Kind: WeaponKind.Ranged } w || State != WeaponState.Ready) return false;
        if (Clip >= w.Capacity || Reserve <= 0) return false;
        _timer = w.ReloadSeconds;
        State = WeaponState.Reloading;
        if (_timer <= 0) FinishReload();
        return true;
    }

    public void Update(double dt)
    {
        if (!(dt > 0) || _timer <= 0 || State is WeaponState.None or WeaponState.Sheathed or WeaponState.Ready) return;
        _timer -= dt;
        if (_timer > 0) return;
        _timer = 0;
        switch (State)
        {
            case WeaponState.Drawing or WeaponState.Attacking: State = WeaponState.Ready; break;
            case WeaponState.Reloading: FinishReload(); break;
            case WeaponState.Sheathing:
                State = WeaponState.Sheathed;
                if (_pendingDraw) { _pendingDraw = false; Draw(); }
                break;
        }
    }

    public void SyncHud(HudState hud)
    {
        if (!IsDrawn || Weapon is null) { hud.ClearAmmo(); return; }
        switch (Weapon.Kind)
        {
            case WeaponKind.Ranged: hud.SetAmmo(Clip, Reserve); break;
            case WeaponKind.Thrown or WeaponKind.Mine: hud.SetAmmo(Reserve, 0); break;
            default: hud.ClearAmmo(); break;
        }
    }

    // Constraint: mods apply in sorted key order; a mod without records is skipped.
    private WeaponInfo Modded(WeaponInfo info, ItemState item)
    {
        if (mods is null || item.Mods.Count == 0) return info;
        return ObjectModMath.Apply(info, item.Mods.SelectMany(key => mods.Properties(key) ?? []));
    }

    private FireResult Attack(AttackKind kind, double cooldown)
    {
        var w = Weapon!;
        _timer = cooldown;
        State = cooldown > 0 ? WeaponState.Attacking : WeaponState.Ready;
        Attacked?.Invoke(new WeaponAttack(WeaponKey!, kind, w.BaseDamage, kind == AttackKind.Shot ? w.AmmoDamage : 0,
            kind == AttackKind.Shot ? w.Pellets : 1, w.Reach, w.MaxRange, kind == AttackKind.Shot ? w.Recoil : null));
        return FireResult.Fired;
    }

    private void FinishReload()
    {
        var take = Math.Min(Weapon!.Capacity - Clip, Reserve);
        if (take > 0 && store.Remove(Weapon.AmmoKey!, take)) Clip += take;
        State = WeaponState.Ready;
    }
}
