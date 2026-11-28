using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCw.Ui.Tests.Combat;

public sealed class WeaponTests
{
    private static WeaponInfo Gun(int capacity = 6, double reload = 2, double fire = 0.5, int pellets = 1) =>
        new(9, "AMMO1", capacity, 20, 0, 3000, pellets, 0, fire, reload, 0, AmmoDamage: 5);

    private static WeaponInfo Bat() => new(4, null, 0, 12, 100, 0, 0, 0, 0, 0, 0.6);
    private static WeaponInfo Nade() => new(10, null, 0, 150, 0, 0, 0, 0, 0, 0, 0.3);
    private static WeaponInfo OfType(int type) => new(type, null, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static (WeaponController c, PlayerItems s, List<WeaponAttack> hits) Ready(WeaponInfo info, int ammo = 20, int clip = 0)
    {
        var store = new PlayerItems();
        store.Add("AMMO1", ammo);
        var c = new WeaponController(store);
        var hits = new List<WeaponAttack>();
        c.Attacked += hits.Add;
        Assert.True(c.Equip("W1", info, clip));
        c.Draw();
        return (c, store, hits);
    }

    // Invariant: catches TryConsumeRound firing an attack, ignoring an empty clip or working on a melee weapon.
    [Fact]
    public void TryConsumeRoundTakesAClipRoundWithoutAttackingAndRefusesEmptyOrMelee()
    {
        var (c, _, hits) = Ready(Gun(), clip: 2);

        Assert.True(c.TryConsumeRound());
        Assert.True(c.TryConsumeRound());
        Assert.False(c.TryConsumeRound());
        Assert.Equal(0, c.Clip);
        Assert.Empty(hits);
        Assert.Equal(WeaponState.Ready, c.State);
        Assert.False(Ready(Bat()).c.TryConsumeRound());
    }

    [Fact]
    public void ReadsRecordFieldsAndClassifiesKind()
    {
        var fields = new Dictionary<string, string>
        {
            ["WEAP\\AnimationType"] = "9", ["WEAP\\Capacity"] = "12", ["WEAP\\BaseDamage"] = "18",
            ["WEAP\\Flags"] = (1u << 15).ToString(), ["WEAP\\AnimationFireSeconds"] = "0.25",
            ["WEAP\\AnimationReloadSeconds"] = "1.8", ["WEAP\\ProjectileCount"] = "1", ["WEAP\\MaxRange"] = "2000",
        };
        var info = WeaponInfo.Read(p => fields.GetValueOrDefault(p), p => p == "WEAP\\Ammo" ? "0001F276:Fallout4.esm" : null, k => 4);
        Assert.Equal(WeaponKind.Ranged, info.Kind);
        Assert.Equal(12, info.Capacity);
        Assert.Equal(18, info.BaseDamage);
        Assert.True(info.Automatic);
        Assert.Equal("0001F276:Fallout4.esm", info.AmmoKey);
        Assert.Equal(4, info.AmmoDamage);
        Assert.Equal(1.8, info.ReloadSeconds);
        Assert.Equal(0, WeaponInfo.Read(_ => null, _ => null).Capacity);
        Assert.Equal(WeaponKind.Melee, OfType(1).Kind);
        Assert.Equal(WeaponKind.Thrown, Nade().Kind);
        Assert.Equal(WeaponKind.Mine, OfType(11).Kind);
        Assert.Equal(WeaponKind.Other, OfType(12).Kind);
    }

    [Fact]
    public void ReadsTheActionPointCostAndTreatsAnUnreadableOneAsZero()
    {
        Assert.Equal(20.5, WeaponInfo.Read(p => p == "WEAP\\ActionPointCost" ? "20.5" : null, _ => null).ActionPointCost);
        Assert.Equal(0, WeaponInfo.Read(p => p == "WEAP\\ActionPointCost" ? "-3" : null, _ => null).ActionPointCost);
        Assert.Equal(0, WeaponInfo.Read(_ => null, _ => null).ActionPointCost);
    }

    [Fact]
    public void DrawAndSheatheRunTimers()
    {
        var c = new WeaponController(new PlayerItems(), drawSeconds: 0.5, sheatheSeconds: 0.3);
        Assert.False(c.Equip("W", OfType(12)));
        c.Equip("W", Bat());
        Assert.Equal(WeaponState.Sheathed, c.State);
        Assert.Equal(FireResult.NotReady, c.Fire());
        c.Draw();
        Assert.Equal(WeaponState.Drawing, c.State);
        Assert.Equal(FireResult.NotReady, c.Fire());
        c.Update(0.5);
        Assert.Equal(WeaponState.Ready, c.State);
        c.Sheathe();
        Assert.Equal(WeaponState.Sheathing, c.State);
        c.Draw();
        c.Update(0.3);
        Assert.Equal(WeaponState.Drawing, c.State);
        c.Update(0.5);
        Assert.Equal(WeaponState.Ready, c.State);
        Assert.Equal(FireResult.NoWeapon, new WeaponController(new PlayerItems()).Fire());
    }

    [Fact]
    public void FiringSpendsClipAndRespectsCooldown()
    {
        var (c, store, hits) = Ready(Gun(), clip: 2);
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(1, c.Clip);
        Assert.Equal(FireResult.Busy, c.Fire());
        c.Update(0.5);
        Assert.Equal(FireResult.Fired, c.Fire());
        c.Update(0.5);
        Assert.Equal(FireResult.Empty, c.Fire());
        Assert.Equal(2, hits.Count);
        Assert.Equal(AttackKind.Shot, hits[0].Kind);
        Assert.Equal(25, hits[0].TotalDamage);
        Assert.Equal(20, store.CountOf("AMMO1"));
    }

    [Fact]
    public void ShotgunHandsOffEveryPellet()
    {
        var (c, _, hits) = Ready(Gun(pellets: 7), clip: 1);
        c.Fire();
        Assert.Equal(7, hits[0].Pellets);
    }

    [Fact]
    public void ReloadMovesAmmoOnlyWhenItFinishes()
    {
        var (c, store, _) = Ready(Gun(capacity: 6), ammo: 20, clip: 2);
        Assert.True(c.Reload());
        Assert.Equal(WeaponState.Reloading, c.State);
        Assert.Equal(FireResult.Busy, c.Fire());
        c.Update(1);
        Assert.Equal(2, c.Clip);
        Assert.Equal(20, store.CountOf("AMMO1"));
        c.Update(1);
        Assert.Equal(6, c.Clip);
        Assert.Equal(16, store.CountOf("AMMO1"));
        Assert.Equal(WeaponState.Ready, c.State);
        Assert.False(c.Reload());
    }

    [Fact]
    public void ReloadUsesOnlyWhatTheReserveHoldsAndNeedsSome()
    {
        var (c, store, _) = Ready(Gun(), ammo: 3, clip: 0);
        c.Reload();
        c.Update(2);
        Assert.Equal(3, c.Clip);
        Assert.Equal(0, store.CountOf("AMMO1"));
        c.Fire();
        c.Update(1);
        Assert.False(c.Reload());
        Assert.Equal(2, c.Clip);
    }

    [Fact]
    public void SheatheCancelsReloadWithoutSpendingAmmo()
    {
        var (c, store, _) = Ready(Gun(), clip: 0);
        c.Reload();
        c.Sheathe();
        c.Update(5);
        Assert.Equal(0, c.Clip);
        Assert.Equal(20, store.CountOf("AMMO1"));
        Assert.Equal(WeaponState.Sheathed, c.State);
    }

    [Fact]
    public void MeleeSwingNeedsNoAmmoAndHandsOffReach()
    {
        var (c, _, hits) = Ready(Bat());
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(FireResult.Busy, c.Fire());
        c.Update(0.6);
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.All(hits, h =>
        {
            Assert.Equal(AttackKind.Swing, h.Kind);
            Assert.Equal(12, h.TotalDamage);
            Assert.Equal(100, h.Reach);
        });
        Assert.False(c.Reload());
    }

    [Fact]
    public void RecordReachPassesThroughUnconvertedToTheSwing()
    {
        var fields = new Dictionary<string, string>
        {
            ["WEAP\\AnimationType"] = "1", ["WEAP\\BaseDamage"] = "10", ["WEAP\\Reach"] = "0.8",
        };
        var info = WeaponInfo.Read(p => fields.GetValueOrDefault(p), _ => null);
        Assert.Equal(0.8, info.Reach);
        Assert.Equal(0, WeaponInfo.Read(p => p == "WEAP\\Reach" ? "-1" : null, _ => null).Reach);
        var (c, _, hits) = Ready(info);
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(0.8, Assert.Single(hits).Reach);
    }

    [Fact]
    public void ThrowableConsumesItselfAndUnequipsAtZero()
    {
        var store = new PlayerItems();
        store.Add("NADE", 2);
        var c = new WeaponController(store);
        var hits = new List<WeaponAttack>();
        c.Attacked += hits.Add;
        c.Equip("NADE", Nade());
        c.Draw();
        Assert.Equal(2, c.Reserve);
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(1, store.CountOf("NADE"));
        c.Update(0.3);
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(WeaponState.None, c.State);
        Assert.Null(c.Weapon);
        Assert.Equal(0, store.CountOf("NADE"));
        Assert.Equal(AttackKind.Throw, hits[1].Kind);
        Assert.Equal(150, hits[1].TotalDamage);
    }

    [Fact]
    public void EquippingWhileDrawnStaysDrawn()
    {
        var (c, _, _) = Ready(Gun(), clip: 3);
        Assert.True(c.IsDrawn);
        c.Equip("W2", Bat());
        Assert.Equal(WeaponState.Ready, c.State);
        Assert.Equal("W2", c.WeaponKey);
        c.Unequip();
        Assert.Equal(WeaponState.None, c.State);
        Assert.False(c.IsDrawn);
    }

    [Fact]
    public void HudShowsClipAndReserveOnlyWhileDrawn()
    {
        var (c, _, _) = Ready(Gun(), ammo: 9, clip: 4);
        var hud = new HudState();
        c.SyncHud(hud);
        Assert.True(hud.Ammo.HasWeapon);
        Assert.Equal(4, hud.Ammo.Clip);
        Assert.Equal(9, hud.Ammo.Reserve);
        c.Sheathe();
        c.SyncHud(hud);
        Assert.False(hud.Ammo.HasWeapon);
        c.Equip("W2", Bat());
        c.Draw();
        c.SyncHud(hud);
        Assert.False(hud.Ammo.HasWeapon);
    }

    [Fact]
    public void StoreRemoveRefusesMoreThanHeld()
    {
        var s = new PlayerItems();
        s.Add("A", 2);
        Assert.False(s.Remove("A", 3));
        Assert.True(s.Remove("A", 2));
        Assert.Equal(0, s.CountOf("A"));
    }

    [Fact]
    public void SheatheAfterQueuedDrawStaysSheathed()
    {
        var c = new WeaponController(new PlayerItems(), drawSeconds: 0.5, sheatheSeconds: 0.3);
        c.Equip("W", Bat());
        c.Draw();
        c.Update(0.5);
        c.Sheathe();
        c.Draw();
        c.Sheathe();
        c.Update(0.3);
        Assert.Equal(WeaponState.Sheathed, c.State);
    }

    [Fact]
    public void ReEquippingTheSameWeaponKeepsItsCooldown()
    {
        var (c, _, _) = Ready(Bat());
        c.Fire();
        c.Equip("W1", Bat());
        Assert.Equal(WeaponState.Attacking, c.State);
        Assert.Equal(FireResult.Busy, c.Fire());
    }

    [Fact]
    public void BadTimeStepsAreIgnoredAndRemoveOfZeroIsRefused()
    {
        var (c, store, _) = Ready(Bat());
        c.Fire();
        c.Update(double.NaN);
        c.Update(-1);
        Assert.Equal(WeaponState.Attacking, c.State);
        Assert.False(store.Remove("absent", 0));
        store.Add("A", 1);
        store.Remove("A", 1);
        Assert.Equal(0, store.CountOf("A"));
    }

    [Fact]
    public void HandlerUnequippingDuringAThrowDoesNotCrash()
    {
        var store = new PlayerItems();
        store.Add("NADE", 1);
        var c = new WeaponController(store);
        c.Attacked += _ => c.Unequip();
        c.Equip("NADE", Nade());
        c.Draw();
        Assert.Equal(FireResult.Fired, c.Fire());
    }

    private static WeaponInfo GunWithAim(Dictionary<string, string> aim, string? aimKey = "00ABCD:Fallout4.esm")
    {
        var fields = new Dictionary<string, string> { ["WEAP\\AnimationType"] = "9", ["WEAP\\Capacity"] = "6" };
        return WeaponInfo.Read(p => fields.GetValueOrDefault(p), p => p == "WEAP\\AimModel" ? aimKey : null, null,
            (key, path) => key == "00ABCD:Fallout4.esm" ? aim.GetValueOrDefault(path) : null);
    }

    [Fact]
    public void RecoilReadsFromTheLinkedAimModelRecord()
    {
        var info = GunWithAim(new()
        {
            ["AMDL\\RecoilMinPerShot"] = "0.5", ["AMDL\\RecoilMaxPerShot"] = "2", ["AMDL\\RecoilHipMult"] = "1.5",
            ["AMDL\\RecoilDiminishSpringForce"] = "30", ["AMDL\\RecoilDiminishSightsMult"] = "0.8",
            ["AMDL\\RecoilArc"] = "45", ["AMDL\\RecoilArcRotate"] = "-90", ["AMDL\\RunawayRecoilShots"] = "4",
        });
        Assert.Equal(new WeaponRecoil(0.5, 2, 1.5, 30, 0.8, 45, -90, 4), info.Recoil);
    }

    [Fact]
    public void RecoilIsAbsentWithoutAnAimModelOrItsFields()
    {
        Assert.Null(GunWithAim(new() { ["AMDL\\RecoilMinPerShot"] = "1" }, aimKey: null).Recoil);
        Assert.Null(GunWithAim(new()).Recoil);
        Assert.Null(WeaponInfo.Read(_ => null, p => p == "WEAP\\AimModel" ? "00ABCD:Fallout4.esm" : null).Recoil);
        Assert.Equal(0, GunWithAim(new() { ["AMDL\\RecoilMaxPerShot"] = "-3" }).Recoil!.MaxPerShot);
    }

    [Fact]
    public void RecoilMagnitudeInterpolatesBetweenMinAndMaxAndClamps()
    {
        var r = new WeaponRecoil(1, 3, 1, 0, 0, 0, 0, 0);
        Assert.Equal(1, r.Magnitude(0));
        Assert.Equal(2, r.Magnitude(0.5));
        Assert.Equal(3, r.Magnitude(7));
        Assert.Equal(1, r.Magnitude(-1));
        Assert.Equal(1, r.Magnitude(double.NaN));
        Assert.Equal(1, new WeaponRecoil(3, 1, 1, 0, 0, 0, 0, 0).Magnitude(0));
    }

    [Fact]
    public void RecoilPitchKickIsDegreesInRadiansScaledByHipMultOnlyOffSights()
    {
        var r = new WeaponRecoil(1, 3, 1.5, 0, 0, 0, 0, 0);
        Assert.Equal(2 * Math.PI / 180, r.PitchKickRadians(0.5, aiming: true), 12);
        Assert.Equal(3 * Math.PI / 180, r.PitchKickRadians(0.5, aiming: false), 12);
    }

    [Fact]
    public void RecoilPitchKickTreatsAnUnsetHipMultAsOneAndClampsTheSample()
    {
        var r = new WeaponRecoil(1, 3, 0, 0, 0, 0, 0, 0);
        Assert.Equal(3 * Math.PI / 180, r.PitchKickRadians(9, aiming: false), 12);
        Assert.Equal(1 * Math.PI / 180, r.PitchKickRadians(double.NaN, aiming: false), 12);
    }

    [Fact]
    public void OnlyShotsCarryRecoilToTheHandOff()
    {
        var recoil = new WeaponRecoil(1, 2, 1, 0, 0, 0, 0, 0);
        var (gun, _, shots) = Ready(Gun() with { Recoil = recoil }, clip: 6);
        Assert.Equal(FireResult.Fired, gun.Fire());
        Assert.Equal(recoil, Assert.Single(shots).Recoil);
        var (bat, _, swings) = Ready(Bat() with { Recoil = recoil });
        Assert.Equal(FireResult.Fired, bat.Fire());
        Assert.Null(Assert.Single(swings).Recoil);
    }

    [Fact]
    public void StaffAnimationTypeIsClassedRanged()
    {
        Assert.Equal(WeaponKind.Ranged, OfType(8).Kind);
        Assert.True(new WeaponController(new PlayerItems()).Equip("STAFF", OfType(8)));
        Assert.Equal(WeaponKind.Other, OfType(12).Kind);
    }

    [Fact]
    public void SpeedReadsFromTheRecordAndScalesTheCooldown()
    {
        Assert.Equal(1.5, WeaponInfo.Read(p => p == "WEAP\\Speed" ? "1.5" : null, _ => null).Speed);
        Assert.Equal(0, WeaponInfo.Read(p => p == "WEAP\\Speed" ? "-1" : null, _ => null).Speed);
        var fast = Gun(fire: 0.5) with { Speed = 2 };
        Assert.Equal(0.25, fast.Scaled(0.5));
        Assert.Equal(0.5, Gun().Scaled(0.5));
        Assert.Equal(0.5, (Gun() with { Speed = double.NaN }).Scaled(0.5));
        var (c, _, _) = Ready(fast, clip: 2);
        Assert.Equal(FireResult.Fired, c.Fire());
        c.Update(0.25);
        Assert.Equal(FireResult.Fired, c.Fire());
    }

    [Fact]
    public void ReachScalesTheCombatDistanceSettingIntoMeters()
    {
        Assert.Equal(0.8 * 141 / 70, WeaponReach.Meters(0.8, 141, 70));
        Assert.Null(WeaponReach.Meters(0, 141, 70));
        Assert.Null(WeaponReach.Meters(0.8, 0, 70));
        Assert.Null(WeaponReach.Meters(-1, 141, 70));
        Assert.Null(WeaponReach.Meters(0.8, double.NaN, 70));
        Assert.Null(WeaponReach.Meters(0.8, 141, 0));
    }
}
