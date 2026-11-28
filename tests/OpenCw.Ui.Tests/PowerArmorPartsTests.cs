using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PowerArmorPartsTests
{
    private static PowerArmorSession Entered(out PowerArmorFrame frame)
    {
        var s = new PowerArmorSession(t => true, k => k == "core", new PlayerItems());
        frame = s.Frame("ref1");
        s.Enter(frame);
        return s;
    }

    [Fact]
    public void A_fresh_frame_shows_all_six_parts_at_full_condition_in_the_hud()
    {
        var s = Entered(out _);
        var hud = new PowerArmorHudModel();
        hud.Sync(s);
        Assert.All(hud.Parts, p => { Assert.True(p.Present); Assert.Equal(1, p.Fill); Assert.False(p.Broken); });
    }

    [Fact]
    public void Damage_lowers_the_part_and_zero_breaks_it()
    {
        var s = Entered(out var frame);
        s.DamagePart(PowerArmorPartId.LeftLeg, 0.9);
        Assert.Equal(0.1, frame.ConditionOf(PowerArmorPartId.LeftLeg)!.Value, 6);
        s.DamagePart(PowerArmorPartId.LeftLeg, 5);
        var hud = new PowerArmorHudModel();
        hud.Sync(s);
        Assert.True(hud.Parts[(int)PowerArmorPartId.LeftLeg].Broken);
        Assert.Equal(1, hud.Parts[(int)PowerArmorPartId.Head].Fill);
    }

    [Fact]
    public void Damage_is_ignored_outside_the_armor_for_missing_pieces_and_bad_amounts()
    {
        var s = new PowerArmorSession(t => true, _ => false, new PlayerItems());
        var frame = s.Frame("ref1");
        s.DamagePart(PowerArmorPartId.Torso, 0.5);
        Assert.Equal(1, frame.ConditionOf(PowerArmorPartId.Torso));
        s.Enter(frame);
        frame.SetCondition(PowerArmorPartId.Head, null);
        s.DamagePart(PowerArmorPartId.Head, 0.5);
        s.DamagePart(PowerArmorPartId.Torso, -1);
        s.DamagePart(PowerArmorPartId.Torso, double.NaN);
        Assert.Null(frame.ConditionOf(PowerArmorPartId.Head));
        Assert.Equal(1, frame.ConditionOf(PowerArmorPartId.Torso));
        Assert.Equal(5, frame.Parts.Count);
    }

    [Fact]
    public void Part_keywords_map_to_hud_parts()
    {
        Assert.Equal(PowerArmorPartId.Head, PowerArmorParts.PartOf(["ArmorTypePower", "dn_PowerArmor_Helmet"]));
        Assert.Equal(PowerArmorPartId.RightLeg, PowerArmorParts.PartOf(["dn_powerarmor_rightleg"]));
        Assert.Null(PowerArmorParts.PartOf(["ArmorTypePower"]));
        Assert.Equal(PowerArmorHudModel.PartCount, PowerArmorParts.Keywords.Values.Distinct().Count());
    }

    [Fact]
    public void Part_condition_survives_a_save_and_old_saves_load_at_full()
    {
        var s = Entered(out var frame);
        s.DamagePart(PowerArmorPartId.RightArm, 0.4);
        frame.SetCondition(PowerArmorPartId.Head, null);
        var sections = ContainerStateSave.Sections(new ContainerLedger { PowerArmor = s.Capture() }, new PlayerItems());
        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(sections, loaded, new PlayerItems());
        var restored = new PowerArmorSession(t => true, _ => false, new PlayerItems());
        restored.Restore(loaded.PowerArmor);
        var back = restored.Frame("ref1");
        Assert.Equal(0.6, back.ConditionOf(PowerArmorPartId.RightArm)!.Value, 6);
        Assert.Null(back.ConditionOf(PowerArmorPartId.Head));
        Assert.Equal(1, back.ConditionOf(PowerArmorPartId.Torso));

        var legacy = new PowerArmorSave(null, [new PowerArmorFrameSave("ref2", "", null, 0)]);
        restored.Restore(legacy);
        Assert.Equal(6, restored.Frame("ref2").Parts.Count);
    }

    [Fact]
    public void Default_capacity_turns_the_drain_on_for_vanilla_data_without_the_gmst()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:fo4.esm", "GMST", "fPowerArmorPowerDrainPerSecondRunning").Scalars[PickpocketSettings.FloatPath] = "0.05";
        var model = PowerArmorDrain.Read(reader);
        Assert.Equal(100, model.Capacity);
        var items = new PlayerItems();
        items.Add("core", 1);
        var s = new PowerArmorSession(t => true, k => k == "core", items) { Model = model };
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", 1, items);
        s.Enter(frame);
        s.Drain(PowerArmorDrainKind.Running, 100);
        Assert.Equal(0.95, frame.CoreCharge, 6);
    }

    [Fact]
    public void Entry_eases_the_camera_in_and_exit_swings_out_and_back()
    {
        var t = new PowerArmorCameraTransition(1, 0.5, 2);
        Assert.False(t.Active);
        Assert.Equal((0.0, 0.0), t.Offset);
        t.Begin(true);
        Assert.Equal((0.5, 2.0), t.Offset);
        t.Tick(0.5);
        Assert.Equal(0.25, t.Offset.Up, 6);
        t.Tick(0.6);
        Assert.False(t.Active);
        Assert.Equal((0.0, 0.0), t.Offset);

        t.Begin(false);
        Assert.Equal(0, t.Pullback, 6);
        t.Tick(0.5);
        Assert.Equal(1, t.Pullback, 6);
        t.Tick(0.4);
        Assert.True(t.Active);
        Assert.True(t.Pullback < 0.4);
        t.Tick(0.2);
        Assert.False(t.Active);
    }

    [Fact]
    public void Transition_ignores_bad_ticks_and_a_zero_length_never_starts()
    {
        var t = new PowerArmorCameraTransition(1);
        t.Begin(true);
        t.Tick(-1);
        t.Tick(double.NaN);
        Assert.Equal(0, t.Progress);
        Assert.False(new PowerArmorCameraTransition(0) is { } z && Begin(z));
    }

    private static bool Begin(PowerArmorCameraTransition t)
    {
        t.Begin(true);
        return t.Active;
    }
}
