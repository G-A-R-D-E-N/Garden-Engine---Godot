using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class WeaponCadenceTests
{
    private static WeaponInfo Auto(double fire) => new(9, "AMMO", 5000, 10, 0, 3000, 1, 1u << 15, fire, 2, 0);

    private static (WeaponController c, int shots) Armed(WeaponInfo info, Func<AttackKind, bool>? performs = null)
    {
        var store = new PlayerItems();
        store.Add("AMMO", 5000);
        var c = new WeaponController(store);
        if (performs != null) c.Performs = performs;
        c.Equip("W", info, 5000);
        c.Draw();
        return (c, 0);
    }

    private static int Shots(double fire, double dt, double seconds)
    {
        var (c, _) = Armed(Auto(fire));
        var shots = 0;
        c.Attacked += _ => shots++;
        c.Update(dt);
        c.FireBurst(out _);
        shots = 0;
        for (var t = 0.0; t < seconds - 1e-9; t += dt)
        {
            c.Update(dt);
            c.FireBurst(out _);
        }
        return shots;
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(144.0)]
    public void AutomaticFireKeepsTheRecordCadenceAtAnyTickRate(double hz)
    {
        Assert.InRange(Shots(0.01, 1 / hz, 2.0), 199, 201);
    }

    [Fact]
    public void ABurstIsBoundedWhenATickCoversManyIntervals()
    {
        var (c, _) = Armed(Auto(0.001));
        var shots = 0;
        c.Attacked += _ => shots++;
        c.Update(0.2);
        c.FireBurst(out _, maxShots: 4);
        Assert.Equal(1, shots);
        c.Update(0.2);
        c.FireBurst(out _, maxShots: 4);
        Assert.Equal(5, shots);
    }

    [Fact]
    public void LeftoverTimeIsNotBankedAcrossAReleasedTrigger()
    {
        var (c, _) = Armed(Auto(0.5));
        var shots = 0;
        c.Attacked += _ => shots++;
        c.FireBurst(out _);
        c.Update(10);
        c.Update(0.016);
        c.FireBurst(out _);
        Assert.Equal(2, shots);
    }

    [Fact]
    public void AnEmptyClipEndsTheBurstWithEmpty()
    {
        var store = new PlayerItems();
        var c = new WeaponController(store);
        c.Equip("W", Auto(0.01), 2);
        c.Draw();
        c.Update(1);
        Assert.Equal(1, c.FireBurst(out var last));
        Assert.Equal(FireResult.Fired, last);
        c.Update(1);
        Assert.Equal(1, c.FireBurst(out last));
        c.Update(1);
        Assert.Equal(0, c.FireBurst(out last));
        Assert.Equal(FireResult.Empty, last);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void AThrowOrMineTheHostCannotPerformIsRefusedAndNotSpent(int type)
    {
        var store = new PlayerItems();
        store.Add("NADE", 2);
        var c = new WeaponController(store) { Performs = kind => kind is not (AttackKind.Throw or AttackKind.Mine) };
        var attacks = 0;
        c.Attacked += _ => attacks++;
        c.Equip("NADE", new WeaponInfo(type, null, 0, 150, 0, 0, 0, 0, 0, 0, 0.3));
        c.Draw();
        Assert.Equal(FireResult.Unsupported, c.Fire());
        Assert.Equal(2, store.CountOf("NADE"));
        Assert.Equal(0, attacks);
        Assert.Equal(WeaponState.Ready, c.State);
    }

    [Fact]
    public void RangedTimingThatCannotBeReadFallsBackToABoundedCadence()
    {
        static string? Field(string p) => p == "WEAP\\AnimationType" ? "9" : p == "WEAP\\AnimationFireSeconds" ? "-1" : null;
        var info = WeaponInfo.Read(Field, _ => null);
        Assert.Equal(WeaponInfo.FallbackFireSeconds, info.FireSeconds);
        Assert.Equal(WeaponInfo.FallbackReloadSeconds, info.ReloadSeconds);
        var melee = WeaponInfo.Read(p => p == "WEAP\\AnimationType" ? "4" : null, _ => null);
        Assert.Equal(0, melee.FireSeconds);
        Assert.Equal(0, melee.ReloadSeconds);
        var real = WeaponInfo.Read(p => p switch { "WEAP\\AnimationType" => "9", "WEAP\\AnimationFireSeconds" => "0.2", "WEAP\\AnimationReloadSeconds" => "1.5", _ => null }, _ => null);
        Assert.Equal(0.2, real.FireSeconds);
        Assert.Equal(1.5, real.ReloadSeconds);
    }
}
