using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Combat;

public enum WeaponState { None, Sheathed, Drawing, Ready, Attacking, Reloading, Sheathing }

public enum FireResult { Fired, NotReady, NoWeapon, Empty, Busy, Unsupported }

public enum AttackKind { Shot, Swing, Throw, Mine }

public enum WeaponRestore { None, Restored, Deferred, Dropped }

public sealed record WeaponAttack(string WeaponKey, AttackKind Kind, double Damage, double AmmoDamage, int Pellets, double Reach, double MaxRange, WeaponRecoil? Recoil = null, double SpreadDegrees = 0)
{
    public double TotalDamage => Damage + AmmoDamage;
}

// Requires: main thread only; Update(dt) drives every timer. Draw/sheathe durations come from the caller (animation events).
public sealed class WeaponController(PlayerItems store, double drawSeconds = 0, double sheatheSeconds = 0, IObjectModRecords? mods = null)
{
    public const int MaxBurstShots = 8;
    public const double RetrySeconds = 2;
    public const int MaxModRetries = 5;
    private const int MaxLookupMisses = 30;

    private double _timer;
    private double _carry;
    private bool _pendingDraw;
    private WeaponInfo? _unmodded;
    private WeaponSave? _deferred;
    private Func<string, WeaponInfo?>? _lookup;
    private bool _modsIncomplete;
    private int _attempts;
    private int _lookupMisses;
    private double _retryIn;

    public string? WeaponKey { get; private set; }
    public WeaponInfo? Weapon { get; private set; }
    public ItemState Item { get; private set; } = ItemState.Plain;
    public WeaponState State { get; private set; }
    public int Clip { get; private set; }

    public event Action<WeaponAttack>? Attacked;

    public event Action<string>? Notice;

    public Func<AttackKind, bool> Performs { get; set; } = _ => true;

    public bool IsDrawn => State is WeaponState.Drawing or WeaponState.Ready or WeaponState.Attacking or WeaponState.Reloading;

    public int Reserve => Weapon?.Kind switch
    {
        WeaponKind.Ranged when Weapon.AmmoKey is { } ammo => store.CountOf(ammo),
        WeaponKind.Thrown or WeaponKind.Mine => store.CountOf(WeaponKey!),
        _ => 0,
    };

    public bool Equip(string weaponKey, WeaponInfo info, int clip = 0, ItemState? item = null)
    {
        if (_deferred is not null)
        {
            if (ResolveDeferred() == WeaponRestore.Deferred) return false;
            if (WeaponKey is not null)
            {
                if (string.Equals(WeaponKey, weaponKey, StringComparison.OrdinalIgnoreCase) && Item.Equals(item ?? ItemState.Plain)) clip = Math.Max(clip, Clip);
                else RefundClip(Clip);
            }
        }
        if (info.Kind == WeaponKind.Other || info.NotPlayable) return false;
        var wasDrawn = IsDrawn || _pendingDraw;
        var same = string.Equals(WeaponKey, weaponKey, StringComparison.OrdinalIgnoreCase);
        var keepState = same && State is WeaponState.Attacking or WeaponState.Drawing;
        var timer = _timer;
        var state = State;
        WeaponKey = weaponKey;
        Item = item ?? ItemState.Plain;
        _unmodded = info;
        _attempts = 0;
        Weapon = Modded(info, Item);
        Clip = Weapon.Kind != WeaponKind.Ranged ? 0 : _modsIncomplete ? Math.Max(clip, 0) : Math.Clamp(clip, 0, Weapon.Capacity);
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
        if (store.CountOf(key) == 0) { RefundClip(Clip); Unequip(); return; }
        if (store.CountOf(key, Item) > 0) return;
        Item = store.Entries.First(e => string.Equals(e.Id, key, StringComparison.OrdinalIgnoreCase)).State;
        Remod();
    }

    public void Retarget(string id, ItemState from, ItemState to)
    {
        if (WeaponKey is not { } key || !string.Equals(key, id, StringComparison.OrdinalIgnoreCase) || !Item.Equals(from)) return;
        Item = to;
        Remod();
    }

    // Invariant: while a mod is unreadable the clip is kept as given; only a complete read, or the last failed retry, clamps and refunds it.
    private void Remod(bool retry = false)
    {
        if (_unmodded is null) return;
        if (!retry) _attempts = 0;
        Weapon = Modded(_unmodded, Item);
        if (_modsIncomplete)
        {
            if (!retry || ++_attempts < MaxModRetries) return;
            _modsIncomplete = false;
            Notice?.Invoke($"{WeaponKey}: an installed modification could not be read after {MaxModRetries} tries; its stats are left out");
        }
        var kept = Weapon.Kind == WeaponKind.Ranged ? Math.Min(Clip, Weapon.Capacity) : 0;
        RefundClip(Clip - kept);
        Clip = kept;
    }

    private void RefundClip(int rounds)
    {
        if (rounds > 0 && Weapon is { Kind: WeaponKind.Ranged, AmmoKey: { } ammo }) store.Add(ammo, rounds);
    }

    public WeaponSave? Capture() => WeaponKey is null ? _deferred : new WeaponSave(WeaponKey, Clip, IsDrawn, Item);

    // Requires: player items already restored (the ammo reserve reads them); a save whose record cannot be read stays pending, is captured unchanged and is retried by Update.
    public WeaponRestore Restore(WeaponSave? save, Func<string, WeaponInfo?> lookup)
    {
        Unequip();
        _deferred = save;
        _lookup = lookup;
        _lookupMisses = 0;
        if (save is null) return WeaponRestore.None;
        var result = ResolveDeferred();
        if (result == WeaponRestore.Deferred) Notice?.Invoke($"{save.Key}: the saved weapon record could not be read; its loaded rounds are kept and the restore is retried");
        return result;
    }

    public WeaponRestore ResolveDeferred()
    {
        if (_deferred is not { } save || _lookup is null) return WeaponRestore.None;
        if (_lookup(save.Key) is not { } info)
        {
            if (store.CountOf(save.Key) > 0 && ++_lookupMisses < MaxLookupMisses) return WeaponRestore.Deferred;
            _deferred = null;
            Notice?.Invoke($"{save.Key}: the saved weapon record stayed unreadable or the weapon is no longer carried; the restore was dropped");
            return WeaponRestore.Dropped;
        }
        _deferred = null;
        if (store.CountOf(save.Key) == 0 || !Equip(save.Key, info, save.Clip, save.Item))
        {
            if (info is { Kind: WeaponKind.Ranged, AmmoKey: { } ammo } && save.Clip > 0) store.Add(ammo, save.Clip);
            Notice?.Invoke($"{save.Key}: the saved weapon is no longer carried or cannot be equipped; its {save.Clip} loaded rounds went back to the reserve");
            return WeaponRestore.Dropped;
        }
        SyncHeld();
        if (save.Drawn) Draw();
        return WeaponRestore.Restored;
    }

    public void Unequip()
    {
        WeaponKey = null;
        Weapon = null;
        _unmodded = null;
        _modsIncomplete = false;
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
                var kind = w.Kind == WeaponKind.Mine ? AttackKind.Mine : AttackKind.Throw;
                if (!Performs(kind)) return FireResult.Unsupported;
                if (!store.Remove(key, Item, 1) && !store.Remove(key, 1)) return FireResult.Empty;
                var result = Attack(kind, w.Scaled(w.AttackSeconds));
                if (store.CountOf(key) == 0 && WeaponKey == key) Unequip();
                return result;
        }
    }

    // Requires: ready ranged weapon with a round in the clip; takes the round Fire takes but starts no attack, cooldown or Attacked event (Attacked would add a crosshair hitscan to the caller's own damage).
    public bool TryConsumeRound()
    {
        if (Weapon is not { Kind: WeaponKind.Ranged } || State != WeaponState.Ready || Clip <= 0) return false;
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

    // Requires: held-trigger automatic fire; each shot in the burst spends the time left over from the previous cooldown, so the cadence follows the interval rather than the tick rate.
    public int FireBurst(out FireResult last, int maxShots = MaxBurstShots)
    {
        var shots = 0;
        last = FireResult.NotReady;
        while (shots < maxShots)
        {
            last = Fire();
            if (last != FireResult.Fired) break;
            shots++;
            var carry = _carry;
            _carry = 0;
            if (!(carry > 0)) break;
            Advance(carry);
            if (State != WeaponState.Ready) break;
        }
        return shots;
    }

    public void Update(double dt)
    {
        if (dt > 0) _carry = 0;
        Advance(dt);
        if (dt > 0 && (_deferred is not null || _modsIncomplete)) Retry(dt);
    }

    private void Retry(double dt)
    {
        _retryIn -= dt;
        if (_retryIn > 0) return;
        _retryIn = RetrySeconds;
        if (_deferred is not null) ResolveDeferred();
        else Remod(retry: true);
    }

    private void Advance(double dt)
    {
        if (!(dt > 0) || _timer <= 0 || State is WeaponState.None or WeaponState.Sheathed or WeaponState.Ready) return;
        _timer -= dt;
        if (_timer > 0) return;
        if (State == WeaponState.Attacking) _carry = -_timer;
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

    // Constraint: mods apply in item key order; a mod with no rows adds nothing and a mod that could not be read marks the result incomplete.
    private WeaponInfo Modded(WeaponInfo info, ItemState item)
    {
        _modsIncomplete = false;
        if (mods is null || item.Mods.Count == 0) return info;
        var incomplete = false;
        var rows = item.Mods.SelectMany(key =>
        {
            var found = mods.Properties(key);
            if (found is null) incomplete = true;
            return found ?? [];
        }).ToList();
        _modsIncomplete = incomplete;
        return ObjectModMath.Apply(info, rows);
    }

    private FireResult Attack(AttackKind kind, double cooldown)
    {
        var w = Weapon!;
        _timer = cooldown;
        State = cooldown > 0 ? WeaponState.Attacking : WeaponState.Ready;
        Attacked?.Invoke(new WeaponAttack(WeaponKey!, kind, w.BaseDamage, kind == AttackKind.Shot ? w.AmmoDamage : 0,
            kind == AttackKind.Shot ? w.Pellets : 1, w.Reach, w.MaxRange, kind == AttackKind.Shot ? w.Recoil : null, w.SpreadDegrees));
        return FireResult.Fired;
    }

    private void FinishReload()
    {
        var take = Math.Min(Weapon!.Capacity - Clip, Reserve);
        if (take > 0 && store.Remove(Weapon.AmmoKey!, take)) Clip += take;
        State = WeaponState.Ready;
    }
}
