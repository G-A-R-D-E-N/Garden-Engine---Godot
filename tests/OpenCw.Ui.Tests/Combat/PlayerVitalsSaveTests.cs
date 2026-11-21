using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

[Collection("PlayerHealth")]
public class PlayerVitalsSaveTests
{
    private static IReadOnlyDictionary<string, byte[]> Json(string json) =>
        new Dictionary<string, byte[]> { [PlayerVitalsSave.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void Vitals_and_limbs_survive_a_save_and_load()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        vitals.AddHealth(-60);
        vitals.AddRads(25);
        vitals.QueueHeal(30, 10);
        vitals.AddBuff(VitalsEffects.HealthValue, 20, 60);
        var limbs = new PlayerLimbs();
        limbs.Set(LimbKind.LeftLeg, 0, true);
        limbs.Set(LimbKind.Head, 0.5);

        var read = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(vitals, limbs));

        var fresh = new PlayerVitals(100, 100, 90, 90);
        var freshLimbs = new PlayerLimbs();
        PlayerVitalsSave.Apply(read, fresh, freshLimbs);
        Assert.Equal(vitals.Health, fresh.Health);
        Assert.Equal(25, fresh.Rads);
        Assert.Equal(30, fresh.PendingHealth, 6);
        Assert.Equal(117, fresh.MaxHealth, 6);
        Assert.True(freshLimbs.Get(LimbKind.LeftLeg)!.Crippled);
        Assert.Equal(0.5, freshLimbs.Get(LimbKind.Head)!.Fraction);
        Assert.Null(freshLimbs.Get(LimbKind.RightArm));
    }

    [Fact]
    public void Loading_over_a_damaged_player_replaces_the_live_values()
    {
        var saved = new PlayerVitals(100, 90, 90, 90);
        var read = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(saved, new PlayerLimbs()));
        var live = new PlayerVitals(100, 10, 90, 5);
        live.AddRads(300);
        live.QueueRads(-50, 10);
        var liveLimbs = new PlayerLimbs();
        liveLimbs.Set(LimbKind.RightArm, 0, true);
        PlayerVitalsSave.Apply(read, live, liveLimbs);
        Assert.Equal(90, live.Health);
        Assert.Equal(90, live.ActionPoints);
        Assert.Equal(0, live.Rads);
        Assert.Equal(0, live.PendingRads);
        Assert.Null(liveLimbs.Get(LimbKind.RightArm));
    }

    [Fact]
    public void A_save_with_no_vitals_section_puts_the_live_player_back_to_full()
    {
        Assert.Null(PlayerVitalsSave.Read(new Dictionary<string, byte[]>()));
        var live = new PlayerVitals(100, 10, 90, 5);
        live.AddRads(300);
        var liveLimbs = new PlayerLimbs();
        liveLimbs.Set(LimbKind.Head, 0.1);
        PlayerVitalsSave.Apply(null, live, liveLimbs);
        Assert.Equal(100, live.Health);
        Assert.Equal(90, live.ActionPoints);
        Assert.Equal(0, live.Rads);
        Assert.Null(liveLimbs.Get(LimbKind.Head));
    }

    [Fact]
    public void Limbs_load_even_when_no_player_is_attached()
    {
        var limbs = new PlayerLimbs();
        PlayerVitalsSave.Apply(new VitalsSave(null, new Dictionary<LimbKind, LimbCondition> { [LimbKind.Head] = new(0.25) }), null, limbs);
        Assert.Equal(0.25, limbs.Get(LimbKind.Head)!.Fraction);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"limbs\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"limbs\":[{\"limb\":\"Tail\",\"fraction\":1}]}")]
    [InlineData("{\"version\":1,\"limbs\":[{\"limb\":\"Head\",\"fraction\":\"x\"}]}")]
    [InlineData("{\"version\":1,\"health\":1,\"actionPoints\":1,\"rads\":0,\"buffs\":[],\"limbs\":[]}")]
    [InlineData("{\"version\":1,\"health\":1,\"actionPoints\":1,\"rads\":0,\"heals\":[{\"rate\":1,\"left\":1,\"target\":\"Nope\"}],\"buffs\":[],\"limbs\":[]}")]
    public void A_malformed_section_is_refused_as_a_format_error(string json) =>
        Assert.Throws<FormatException>(() => PlayerVitalsSave.Read(Json(json)));

    [Fact]
    public void Restoring_drops_rows_that_cannot_run_and_clamps_to_the_maxima()
    {
        var snapshot = new VitalsSnapshot(500, 500, -4, [new PendingHeal(1, 0, Vital.Health), new PendingHeal(double.NaN, 5, Vital.Health)], [new ActiveBuff("Health", 10, 0), new ActiveBuff("", 10, 5)]);
        var live = new PlayerVitals(100, 100, 90, 90);
        live.Restore(snapshot);
        Assert.Equal(100, live.Health);
        Assert.Equal(90, live.ActionPoints);
        Assert.Equal(0, live.Rads);
        Assert.Equal(0, live.PendingHealth);
        Assert.Equal(0, live.Buff("Health"));
    }
}
