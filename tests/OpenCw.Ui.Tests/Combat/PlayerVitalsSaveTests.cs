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

        var read = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(vitals.Capture(), limbs));

        var fresh = new PlayerVitals(100, 100, 90, 90);
        var freshLimbs = new PlayerLimbs();
        PlayerVitalsSave.Apply(read, fresh, freshLimbs);
        Assert.Equal(vitals.Health, fresh.Health);
        Assert.Equal(25, fresh.Rads);
        Assert.Equal(30, fresh.PendingHealth, 6);
        Assert.Equal(vitals.MaxHealth, fresh.MaxHealth, 6);
        Assert.True(freshLimbs.Get(LimbKind.LeftLeg)!.Crippled);
        Assert.Equal(0.5, freshLimbs.Get(LimbKind.Head)!.Fraction);
        Assert.Null(freshLimbs.Get(LimbKind.RightArm));
    }

    [Fact]
    public void A_detached_player_keeps_loaded_vitals_through_an_immediate_resave_and_attach()
    {
        PlayerHealth.Detach();
        try
        {
            var original = new PlayerVitals(180, 150, 120, 70);
            original.AddRads(25);
            original.QueueHeal(20, 10);
            var loaded = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(original.Capture(), new PlayerLimbs()));
            PlayerHealth.ApplySnapshot(loaded!.Vitals);
            var carried = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(PlayerHealth.Snapshot, new PlayerLimbs()));
            Assert.Equal(150, carried!.Vitals!.Health);
            var attached = PlayerHealth.Attach(100, 100, 90, 90)!;
            Assert.Equal(180, attached.MaxHealth);
            Assert.Equal(150, attached.Health);
            Assert.Equal(70, attached.ActionPoints);
            Assert.Equal(20, attached.PendingHealth, 6);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Pending_saved_health_can_attach_when_the_current_record_has_no_valid_health(double? recordHealth)
    {
        PlayerHealth.Detach();
        try
        {
            var saved = new PlayerVitals(180, 150, 120, 70);
            PlayerHealth.ApplySnapshot(saved.Capture());
            var attached = PlayerHealth.EnsureFrom(_ => recordHealth);
            Assert.NotNull(attached);
            Assert.Equal(150, attached.Health);
            Assert.Equal(180, attached.MaxHealth);
            Assert.Equal(70, attached.ActionPoints);
            PlayerHealth.Detach();
            saved.AddHealth(-150);
            PlayerHealth.ApplySnapshot(saved.Capture());
            var dead = PlayerHealth.EnsureFrom(_ => recordHealth);
            Assert.NotNull(dead);
            Assert.Equal(0, dead.Health);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Fact]
    public void Loading_a_legacy_save_recomputes_the_attached_players_maximum_for_the_loaded_level()
    {
        PlayerHealth.Detach();
        try
        {
            double? Record(string key) => key switch { "Health" => 100, "ActionPoints" => 90, "Endurance" => 4, "Agility" => 3, _ => null };
            var live = PlayerHealth.EnsureFrom(Record, 1)!;
            live.Restore(new VitalsSnapshot(180, 120, 150, 70, 0, [], []));
            PlayerHealth.ApplySnapshot(null, 2);
            Assert.Same(live, PlayerHealth.Current);
            Assert.Equal(PlayerHealth.MaxHealthFor(4, 2), live.MaxHealth);
            Assert.Equal(live.MaxHealth, live.Health);
            Assert.Equal(PlayerHealth.MaxActionPointsFor(3), live.MaxActionPoints);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Fact]
    public void Replacing_an_attached_players_save_restores_the_saved_maximum_and_zero_health()
    {
        var saved = new PlayerVitals(180, 1, 120, 70);
        saved.AddHealth(-1);
        var live = new PlayerVitals(100, 60, 90, 90);
        live.Restore(saved.Capture());
        Assert.Equal(180, live.MaxHealth);
        Assert.Equal(0, live.Health);
        Assert.Equal(120, live.MaxActionPoints);
    }

    [Fact]
    public void Loading_over_a_damaged_player_replaces_the_live_values()
    {
        var saved = new PlayerVitals(100, 90, 90, 90);
        var read = PlayerVitalsSave.Read(PlayerVitalsSave.Sections(saved.Capture(), new PlayerLimbs()));
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
    [InlineData("{\"version\":2,\"version\":2,\"limbs\":[]}")]
    [InlineData("[]")]
    [InlineData("{\"version\":3,\"limbs\":[]}")]
    [InlineData("{\"version\":1,\"limbs\":[]}")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":2,\"baseMaxHealth\":100,\"limbs\":[]}")]
    [InlineData("{\"version\":2,\"limbs\":[{\"limb\":\"Tail\",\"fraction\":1}]}")]
    [InlineData("{\"version\":2,\"limbs\":[{\"limb\":\"Head\",\"fraction\":\"x\"}]}")]
    [InlineData("{\"version\":2,\"baseMaxHealth\":100,\"baseMaxActionPoints\":90,\"health\":1,\"actionPoints\":1,\"rads\":0,\"buffs\":[],\"limbs\":[]}")]
    [InlineData("{\"version\":2,\"baseMaxHealth\":100,\"baseMaxActionPoints\":90,\"health\":1,\"actionPoints\":1,\"rads\":0,\"heals\":[{\"rate\":1,\"left\":1,\"target\":\"Nope\"}],\"buffs\":[],\"limbs\":[]}")]
    public void A_malformed_section_is_refused_as_a_format_error(string json) =>
        Assert.Throws<FormatException>(() => PlayerVitalsSave.Read(Json(json)));

    [Fact]
    public void Controller_action_points_and_recovery_delay_round_trip()
    {
        var saved = new ControllerActionSnapshot(35, 0.75f, true);
        var loaded = ControllerActionSave.Read(ControllerActionSave.Sections(saved));
        Assert.Equal(saved, loaded);
    }

    [Theory]
    [InlineData("{\"version\":1,\"actionPoints\":-1,\"recoveryDelay\":0,\"exhausted\":false}")]
    [InlineData("{\"version\":1,\"actionPoints\":2,\"recoveryDelay\":-1,\"exhausted\":false}")]
    [InlineData("{\"version\":1,\"version\":1,\"actionPoints\":1,\"recoveryDelay\":0,\"exhausted\":false}")]
    public void Malformed_controller_action_state_is_refused(string json) =>
        Assert.Throws<FormatException>(() => ControllerActionSave.Read(new Dictionary<string, byte[]>
        {
            [ControllerActionSave.SectionName] = Encoding.UTF8.GetBytes(json),
        }));

    [Fact]
    public void Restoring_drops_rows_that_cannot_run_and_clamps_to_the_maxima()
    {
        var snapshot = new VitalsSnapshot(100, 90, 500, 500, -4, [new PendingHeal(1, 0, Vital.Health), new PendingHeal(double.NaN, 5, Vital.Health), new PendingHeal(-1, 5, Vital.Health)], [new ActiveBuff("Health", 10, 0), new ActiveBuff("", 10, 5)]);
        var live = new PlayerVitals(100, 100, 90, 90);
        live.Restore(snapshot);
        Assert.Equal(100, live.Health);
        Assert.Equal(90, live.ActionPoints);
        Assert.Equal(0, live.Rads);
        Assert.Equal(0, live.PendingHealth);
        Assert.Equal(0, live.Buff("Health"));
    }
}
