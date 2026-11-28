using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCw.Ui.Tests;

public class PowerArmorSessionTests
{
    private static ActivationTarget Frame(string key = "ref1") =>
        new(key, "base1", "Furniture", new HashSet<string> { "furniture" });

    private static PowerArmorSession Session(double drain = 0, PlayerItems? items = null) =>
        new(t => t.BaseKey == "base1", k => k == "core", items ?? new PlayerItems(), drain);

    [Fact]
    public void Activating_a_frame_enters_and_publishes_hud_state()
    {
        var s = Session();
        var entered = 0;
        s.Entered += _ => entered++;
        Assert.True(s.Handles(Frame()));
        Assert.False(s.Handles(new ActivationTarget("r", "other", "Furniture", new HashSet<string>())));
        Assert.Equal(ActivationStatus.Handled, s.Activate(Frame()).Status);
        Assert.True(s.InArmor);
        Assert.Equal(1, entered);
        Assert.False(s.Current!.HasCore);
    }

    [Fact]
    public void Activating_the_occupied_frame_again_exits()
    {
        var s = Session();
        s.Activate(Frame());
        Assert.Equal(ActivationStatus.Handled, s.Activate(Frame()).Status);
        Assert.False(s.InArmor);
    }

    [Fact]
    public void Cannot_enter_twice_and_exit_clears_state()
    {
        var s = Session();
        s.Activate(Frame());
        Assert.Equal(ActivationStatus.Blocked, s.Activate(Frame("ref2")).Status);
        Assert.Equal(PowerArmorRefusal.None, s.Exit());
        Assert.Null(s.Current);
        Assert.Equal(PowerArmorRefusal.NotInArmor, s.Exit());
    }

    [Fact]
    public void Core_insert_checks_item_kind_inventory_and_slot()
    {
        var items = new PlayerItems();
        var s = Session(items: items);
        var frame = s.Frame("ref1");
        Assert.Equal(PowerArmorRefusal.NotACore, s.InsertCore(frame, "junk", 1, items));
        Assert.Equal(PowerArmorRefusal.NoneInInventory, s.InsertCore(frame, "core", 1, items));
        items.Add("core", 2);
        Assert.Equal(PowerArmorRefusal.None, s.InsertCore(frame, "core", 0.5, items));
        Assert.Equal(1, items.CountOf("core"));
        Assert.Equal(PowerArmorRefusal.CoreAlreadyInstalled, s.InsertCore(frame, "core", 1, items));
    }

    [Fact]
    public void Drain_only_runs_inside_the_armor_with_a_core_and_keeps_charge_on_exit()
    {
        var items = new PlayerItems();
        var s = Session(drain: 0.1, items: items);
        items.Add("core", 1);
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", 1, items);
        s.Tick(5);
        Assert.Equal(1, frame.CoreCharge);
        s.Enter(frame);
        s.Tick(5);
        Assert.Equal(0.5, frame.CoreCharge, 6);
        Assert.Equal(1, s.Current!.CoreMinutesRemaining);
        s.Exit();
        s.Tick(100);
        Assert.Equal(0.5, frame.CoreCharge, 6);
        s.Enter(frame);
        s.Tick(100);
        Assert.Equal(0, frame.CoreCharge);
    }

    private static (PowerArmorSession Session, PowerArmorFrame Frame) Charged(PowerArmorDrain? drain, bool enter = true)
    {
        var items = new PlayerItems();
        var s = new PowerArmorSession(t => t.BaseKey == "base1", k => k == "core", items, 0, drain);
        items.Add("core", 1);
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", 1, items);
        if (enter) s.Enter(frame);
        return (s, frame);
    }

    [Fact]
    public void Event_drain_subtracts_rate_times_damage_rate_over_capacity()
    {
        var rates = new PowerArmorDrainRates(PerSecondRunning: 0.5, PerActionPoint: 0.1, PerJump: 4, PerMeleeAttack: 2, PerImpactLand: 10);
        var (s, frame) = Charged(new PowerArmorDrain(rates, Capacity: 100, DamageRate: 2));
        s.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(0.8, frame.CoreCharge, 6);
        s.Drain(PowerArmorDrainKind.ActionPoint, 25);
        Assert.Equal(0.75, frame.CoreCharge, 6);
        s.Drain(PowerArmorDrainKind.Jump);
        s.Drain(PowerArmorDrainKind.MeleeAttack);
        Assert.Equal(0.63, frame.CoreCharge, 6);
        s.Tick(10, running: true);
        Assert.Equal(0.53, frame.CoreCharge, 6);
        s.Tick(10);
        Assert.Equal(0.53, frame.CoreCharge, 6);
    }

    [Fact]
    public void Event_drain_clamps_at_zero_and_ignores_bad_input()
    {
        var rates = new PowerArmorDrainRates(PerImpactLand: 60);
        var (s, frame) = Charged(new PowerArmorDrain(rates, 100));
        s.Drain(PowerArmorDrainKind.ImpactLand, 0);
        s.Drain(PowerArmorDrainKind.ImpactLand, -1);
        s.Drain(PowerArmorDrainKind.ImpactLand, double.NaN);
        Assert.Equal(1, frame.CoreCharge);
        s.Drain(PowerArmorDrainKind.ImpactLand);
        s.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(0, frame.CoreCharge);
    }

    [Fact]
    public void Event_drain_needs_a_capacity_a_core_and_the_armor_worn()
    {
        var rates = new PowerArmorDrainRates(PerImpactLand: 10);
        var (none, noneFrame) = Charged(null);
        none.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(1, noneFrame.CoreCharge);
        var (zero, zeroFrame) = Charged(new PowerArmorDrain(rates, 0));
        zero.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(1, zeroFrame.CoreCharge);
        var (outside, outFrame) = Charged(new PowerArmorDrain(rates, 100), enter: false);
        outside.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(1, outFrame.CoreCharge);
        var empty = new PowerArmorSession(t => true, _ => false, new PlayerItems(), 0, new PowerArmorDrain(rates, 100));
        empty.Enter(empty.Frame("f"));
        empty.Drain(PowerArmorDrainKind.ImpactLand);
        Assert.Equal(0, empty.Occupied!.CoreCharge);
    }

    [Fact]
    public void Rates_read_from_gmst_records_and_default_missing_ones_to_zero()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:fo4.esm", "GMST", "fPowerArmorPowerDrainPerSecondRunning").Scalars[PickpocketSettings.FloatPath] = "0.05";
        reader.Add("000002:fo4.esm", "GMST", "fPowerArmorPowerDrainPerActionPoint").Scalars[PickpocketSettings.FloatPath] = "0.25";
        reader.Add("000003:fo4.esm", "GMST", "fPowerArmorPowerDrainPerJump").Scalars[PickpocketSettings.FloatPath] = "not a number";
        var rates = PowerArmorDrainRates.Read(reader);
        Assert.Equal(0.05, rates.PerSecondRunning, 6);
        Assert.Equal(0.25, rates.PerActionPoint, 6);
        Assert.Equal(new PowerArmorDrainRates(rates.PerSecondRunning, rates.PerActionPoint), rates);
    }

    [Fact]
    public void Zero_drain_never_drains()
    {
        var items = new PlayerItems();
        var s = Session(items: items);
        items.Add("core", 1);
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", 1, items);
        s.Enter(frame);
        s.Tick(1000);
        Assert.Equal(1, frame.CoreCharge);
        Assert.Equal(-1, s.Current!.CoreMinutesRemaining);
    }

    [Fact]
    public void Entering_auto_loads_a_core_from_the_inventory()
    {
        var items = new PlayerItems();
        items.Add("junk", 3);
        items.Add("core", 1);
        var s = Session(items: items);
        s.Activate(Frame());
        Assert.True(s.Current!.HasCore);
        Assert.Equal(0, items.CountOf("core"));
        Assert.Equal(3, items.CountOf("junk"));
    }

    [Fact]
    public void Entering_without_a_core_item_leaves_the_frame_empty()
    {
        var s = Session();
        s.Activate(Frame());
        Assert.False(s.Current!.HasCore);
    }
}
