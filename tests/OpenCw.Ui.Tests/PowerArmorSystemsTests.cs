using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PowerArmorSystemsTests
{
    private static readonly PowerArmorJetpackSettings Jet = new();

    private static ActivationTarget Frame(string key = "ref1", string baseKey = "base1") =>
        new(key, baseKey, "Furniture", new HashSet<string> { "furniture" });

    private static (PowerArmorSession Session, PowerArmorFrame Frame) Charged(double charge = 1)
    {
        var items = new PlayerItems();
        var s = new PowerArmorSession(t => true, k => k == "core", items);
        items.Add("core", 1);
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", charge, items);
        s.Enter(frame);
        return (s, frame);
    }

    [Fact]
    public void Jetpack_takeoff_bursts_at_the_initial_thrust_then_holds_the_sustained_thrust()
    {
        var jet = new PowerArmorJetpack(Jet);
        Assert.Equal(350, jet.Tick(0.1, true, true, true));
        Assert.Equal(100 - 4.5, jet.Fuel, 6);
        for (var i = 0; i < 5; i++) Assert.Equal(350, jet.Tick(0.1, true, false, false));
        Assert.Equal(85, jet.Tick(0.1, true, false, false));
        Assert.Equal(100 - 0.6 * 45 - 0.1 * 16, jet.Fuel, 6);
    }

    [Fact]
    public void Jetpack_started_in_the_air_skips_the_initial_burst_and_needs_a_press_on_the_ground()
    {
        var jet = new PowerArmorJetpack(Jet);
        Assert.Null(jet.Tick(0.1, true, false, true));
        Assert.Equal(85, jet.Tick(0.1, true, false, false));
        var every = new PowerArmorJetpack(Jet with { InitialOnlyOnTakeoff = false });
        Assert.Equal(350, every.Tick(0.1, true, false, false));
    }

    [Fact]
    public void Jetpack_needs_minimum_fuel_runs_dry_and_refills_only_on_the_ground()
    {
        var jet = new PowerArmorJetpack(Jet with { FuelCapacity = 10, MinFuelRequired = 3, DrainSustained = 5, TimeToSustained = 0, RefillPerSecond = 10 });
        Assert.NotNull(jet.Tick(1, true, true, true));
        Assert.Equal(5, jet.Fuel, 6);
        Assert.NotNull(jet.Tick(0.5, true, false, false));
        Assert.Equal(2.5, jet.Fuel, 6);
        Assert.Null(jet.Tick(1, true, false, false));
        Assert.Equal(0, jet.Fuel);
        Assert.False(jet.Thrusting);
        Assert.Null(jet.Tick(1, true, false, false));
        Assert.Equal(0, jet.Fuel);
        Assert.Null(jet.Tick(0.1, false, false, true));
        Assert.Equal(1, jet.Fuel, 6);
        Assert.Null(jet.Tick(0.1, true, true, true));
        Assert.Equal(2, jet.Fuel, 6);
        Assert.NotNull(jet.Tick(0.2, true, true, true));
        Assert.Equal(3, jet.Fuel, 6);
    }

    [Fact]
    public void Jetpack_ignores_bad_steps_and_stays_off_when_disabled()
    {
        var jet = new PowerArmorJetpack(Jet);
        Assert.Null(jet.Tick(0, true, true, true));
        Assert.Null(jet.Tick(double.NaN, true, true, true));
        Assert.Equal(100, jet.Fuel);
        var off = new PowerArmorJetpack(Jet with { Enabled = false });
        Assert.Null(off.Tick(0.1, true, true, true));
    }

    [Fact]
    public void Landing_reports_the_peak_fall_speed_once_and_ground_steps_report_nothing()
    {
        var landing = new PowerArmorLanding();
        Assert.Null(landing.Update(true, 0));
        Assert.Null(landing.Update(false, -3));
        Assert.Null(landing.Update(false, -9));
        Assert.Null(landing.Update(false, -5));
        Assert.Equal(9, landing.Update(true, 0));
        Assert.Null(landing.Update(true, 0));
        Assert.Null(landing.Update(false, 4));
        Assert.Null(landing.Update(true, 0));
    }

    [Fact]
    public void Stomp_damage_scales_with_speed_over_the_minimum_and_falls_off_with_distance()
    {
        var s = new PowerArmorStompSettings();
        Assert.Equal(0, PowerArmorStomp.Damage(s, 6.4, 0));
        Assert.Equal(17.5, PowerArmorStomp.Damage(s, 10, 0), 6);
        Assert.Equal(0, PowerArmorStomp.Damage(s, 4.43, 0));
        Assert.Equal(8.75, PowerArmorStomp.Damage(s, 10, 1.5), 6);
        Assert.Equal(0, PowerArmorStomp.Damage(s, 10, 3.1));
        Assert.Equal(0, PowerArmorStomp.Damage(s with { Enabled = false }, 10, 0));
        Assert.Equal(0, PowerArmorStomp.Damage(s, double.NaN, 0));
        Assert.Equal(0, PowerArmorStomp.Damage(s, 10, -1));
    }

    [Fact]
    public void Settings_read_the_game_settings_and_default_the_missing_ones_to_the_exe_values()
    {
        var reader = new FakeRecordReader();
        var d = PowerArmorSettings.Read(reader);
        Assert.Equal(1, d.PartDamageMultiplier);
        Assert.Equal(new PowerArmorJetpackSettings(), d.Jetpack);
        reader.Add("000001:fo4.esm", "GMST", "fJetpackThrustInitial").Scalars[PickpocketSettings.FloatPath] = "400";
        reader.Add("000002:fo4.esm", "GMST", "fPowerArmorPCArmorDamageMultiplier").Scalars[PickpocketSettings.FloatPath] = "2";
        reader.Add("000003:fo4.esm", "GMST", "bJetpackInitialThrustOnlyOnTakeoff").Scalars[PickpocketSettings.FloatPath] = "0";
        var read = PowerArmorSettings.Read(reader);
        Assert.Equal(400, read.Jetpack.ThrustInitial);
        Assert.Equal(85, read.Jetpack.ThrustSustained);
        Assert.Equal(2, read.PartDamageMultiplier);
        Assert.False(read.Jetpack.InitialOnlyOnTakeoff);
    }

    [Fact]
    public void Health_lost_in_the_armor_is_spread_evenly_over_the_standing_pieces()
    {
        var (s, frame) = Charged();
        var feed = new PowerArmorDamageFeed(s);
        feed.Tick(100, 100);
        feed.Tick(88, 100);
        Assert.All(frame.Parts, p => Assert.Equal(1 - 0.12 / 6, p.Condition, 6));
        frame.SetCondition(PowerArmorPartId.Head, 0);
        s.DamageSpread(0.5);
        Assert.Equal(0, frame.ConditionOf(PowerArmorPartId.Head));
        Assert.Equal(1 - 0.12 / 6 - 0.1, frame.ConditionOf(PowerArmorPartId.Torso)!.Value, 6);
        feed.Tick(98, 100);
        Assert.Equal(1 - 0.12 / 6 - 0.1, frame.ConditionOf(PowerArmorPartId.Torso)!.Value, 6);
    }

    [Fact]
    public void Damage_feed_scales_by_the_multiplier_and_ignores_damage_outside_the_armor()
    {
        var items = new PlayerItems();
        var s = new PowerArmorSession(t => true, _ => false, items) { Settings = new PowerArmorSettings { PartDamageMultiplier = 2 } };
        var frame = s.Frame("ref1");
        var feed = new PowerArmorDamageFeed(s);
        feed.Tick(100, 100);
        feed.Tick(50, 100);
        Assert.All(frame.Parts, p => Assert.Equal(1, p.Condition));
        s.Enter(frame);
        feed.Tick(40, 100);
        Assert.All(frame.Parts, p => Assert.Equal(1 - 0.2 / 6, p.Condition, 6));
        feed.Tick(40, 0);
        feed.Tick(60, 100);
        Assert.All(frame.Parts, p => Assert.Equal(1 - 0.2 / 6, p.Condition, 6));
    }

    [Fact]
    public void Power_is_lost_without_a_core_or_at_zero_charge_and_the_hud_says_so()
    {
        var items = new PlayerItems();
        var bare = new PowerArmorSession(t => true, _ => false, items);
        Assert.False(bare.PowerLost);
        bare.Enter(bare.Frame("ref1"));
        Assert.True(bare.PowerLost);
        Assert.Equal(PowerArmorHudModel.NoPowerWarning, bare.Current!.WarningText);

        var (s, frame) = Charged(0.5);
        Assert.False(s.PowerLost);
        Assert.Equal("", s.Current!.WarningText);
        frame.CoreCharge = 0;
        Assert.True(s.PowerLost);
        var hud = new PowerArmorHudModel();
        hud.Sync(s);
        Assert.Equal(PowerArmorHudModel.NoPowerWarning, hud.WarningText);
        Assert.True(hud.WarningActive);
        s.Exit();
        Assert.False(s.PowerLost);
    }

    [Fact]
    public void Present_pieces_come_from_the_part_keywords_and_a_bare_frame_has_none()
    {
        var present = PowerArmorParts.Present([["ArmorTypePower", "dn_PowerArmor_Torso"], ["dn_PowerArmor_Torso"], ["dn_PowerArmor_LeftArm"], ["Other"]]);
        Assert.Equal([PowerArmorPartId.Torso, PowerArmorPartId.LeftArm], present);
        Assert.Empty(PowerArmorParts.Present([]));
    }

    [Fact]
    public void A_new_frame_takes_its_pieces_from_the_source_and_a_bare_frame_shows_none()
    {
        var s = new PowerArmorSession(t => true, _ => false, new PlayerItems());
        s.Pieces = key => key == "bare" ? [] : key == "unreadable" ? null : [PowerArmorPartId.Torso, PowerArmorPartId.Head];
        s.Activate(Frame("a", "armored"));
        Assert.Equal([PowerArmorPartId.Head, PowerArmorPartId.Torso], s.Current!.Parts!.Select(p => p.Id));
        Assert.Null(s.Occupied!.ConditionOf(PowerArmorPartId.LeftLeg));
        s.Exit();
        s.Activate(Frame("b", "bare"));
        Assert.Empty(s.Current!.Parts!);
        s.Exit();
        s.Activate(Frame("c", "unreadable"));
        Assert.Equal(6, s.Current!.Parts!.Count);
    }

    [Fact]
    public void A_frame_keeps_its_damaged_pieces_when_entered_again()
    {
        var s = new PowerArmorSession(t => true, _ => false, new PlayerItems());
        var calls = 0;
        s.Pieces = _ => { calls++; return [PowerArmorPartId.Torso]; };
        s.Activate(Frame());
        s.DamagePart(PowerArmorPartId.Torso, 0.4);
        s.Exit();
        s.Activate(Frame());
        Assert.Equal(0.6, s.Occupied!.ConditionOf(PowerArmorPartId.Torso)!.Value, 6);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Restoring_a_save_flags_the_entry_so_the_camera_skips_the_transition_and_keeps_saved_pieces()
    {
        var s = new PowerArmorSession(t => true, _ => false, new PlayerItems());
        s.Pieces = _ => [PowerArmorPartId.Head];
        var seen = new List<bool>();
        s.Entered += _ => seen.Add(s.Restoring);
        s.Exited += _ => seen.Add(s.Restoring);
        s.Activate(Frame());
        var save = s.Capture();
        s.Exit();
        seen.Clear();
        Assert.False(s.Restoring);
        s.Restore(save);
        Assert.True(s.InArmor);
        Assert.Equal([true], seen);
        Assert.False(s.Restoring);
        Assert.Equal([PowerArmorPartId.Head], s.Current!.Parts!.Select(p => p.Id));
        s.Exit();
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void A_dead_core_is_replaced_by_a_fresh_one_but_kept_when_none_is_carried()
    {
        var items = new PlayerItems();
        var s = new PowerArmorSession(t => true, k => k == "core", items);
        var frame = s.Frame("ref1");
        items.Add("core", 1);
        Assert.Equal(PowerArmorRefusal.None, s.InsertCore(frame, "core", 0, items));
        Assert.False(s.AutoLoadCore(frame));
        Assert.True(frame.HasCore);
        Assert.Equal(PowerArmorRefusal.NoneInInventory, s.InsertCore(frame, "core", 1, items));
        Assert.True(frame.HasCore);
        items.Add("core", 2);
        Assert.True(s.AutoLoadCore(frame));
        Assert.Equal(1, frame.CoreCharge);
        Assert.Equal(1, items.CountOf("core"));
        Assert.False(s.AutoLoadCore(frame));
        frame.CoreCharge = 0;
        Assert.Equal(PowerArmorRefusal.None, s.InsertCore(frame, "core", 0.5, items));
        Assert.Equal(0.5, frame.CoreCharge);
        Assert.Equal(0, items.CountOf("core"));
        Assert.Equal(PowerArmorRefusal.CoreAlreadyInstalled, s.InsertCore(frame, "core", 1, items));
    }

    [Fact]
    public void Stomp_does_no_damage_to_non_hostile_targets()
    {
        var s = new PowerArmorStompSettings();
        Assert.Equal(17.5, PowerArmorStomp.Damage(s, 10, 0, true), 6);
        Assert.Equal(0, PowerArmorStomp.Damage(s, 10, 0, false));
    }

    [Fact]
    public void The_damage_feed_rebaselines_after_a_restore_and_on_reset()
    {
        var (s, frame) = Charged();
        var feed = new PowerArmorDamageFeed(s);
        feed.Tick(100, 100);
        s.Restore(s.Capture());
        feed.Tick(40, 100);
        Assert.All(s.Occupied!.Parts, p => Assert.Equal(1, p.Condition));
        feed.Tick(30, 100);
        Assert.All(s.Occupied!.Parts, p => Assert.Equal(1 - 0.1 / 6, p.Condition, 6));
        feed.Reset();
        feed.Tick(100, 100);
        Assert.All(s.Occupied!.Parts, p => Assert.Equal(1 - 0.1 / 6, p.Condition, 6));
    }
}
