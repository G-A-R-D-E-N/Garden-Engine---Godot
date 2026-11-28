using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PowerArmorDrainFeedTests
{
    private static (PowerArmorSession Session, PowerArmorFrame Frame) Armored(PowerArmorDrain? model)
    {
        var items = new PlayerItems();
        var s = new PowerArmorSession(t => true, k => k == "core", items) { Model = model };
        items.Add("core", 1);
        var frame = s.Frame("ref1");
        s.InsertCore(frame, "core", 1, items);
        s.Enter(frame);
        return (s, frame);
    }

    [Fact]
    public void Capacity_is_read_from_the_gmst_and_missing_uses_the_exe_default()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:fo4.esm", "GMST", "fPowerArmorPowerDrainPerSecondRunning").Scalars[PickpocketSettings.FloatPath] = "0.05";
        Assert.Equal(100, PowerArmorDrain.Read(reader).Capacity);
        reader.Add("000002:fo4.esm", "GMST", PowerArmorDrain.CapacitySetting).Scalars[PickpocketSettings.FloatPath] = "250";
        var model = PowerArmorDrain.Read(reader);
        Assert.Equal(250, model.Capacity);
        Assert.Equal(0.05, model.Rates.PerSecondRunning, 6);
    }

    [Fact]
    public void Model_assigned_after_construction_drains_and_none_stays_inert()
    {
        var (s, frame) = Armored(null);
        s.Drain(PowerArmorDrainKind.Running, 10);
        Assert.Equal(1, frame.CoreCharge);
        s.Model = new PowerArmorDrain(new PowerArmorDrainRates(PerSecondRunning: 5), 100);
        s.Drain(PowerArmorDrainKind.Running, 10);
        Assert.Equal(0.5, frame.CoreCharge, 6);
    }

    [Fact]
    public void Sprinting_drains_running_rate_per_second_and_walking_does_not()
    {
        var (s, frame) = Armored(new PowerArmorDrain(new PowerArmorDrainRates(PerSecondRunning: 10), 100));
        var feed = new PowerArmorDrainFeed(s);
        feed.Tick(0.5, false, 100);
        Assert.Equal(1, frame.CoreCharge);
        feed.Tick(0.5, true, 100);
        Assert.Equal(0.95, frame.CoreCharge, 6);
    }

    [Fact]
    public void Only_action_points_spent_since_the_last_tick_drain()
    {
        var (s, frame) = Armored(new PowerArmorDrain(new PowerArmorDrainRates(PerActionPoint: 1), 100));
        var feed = new PowerArmorDrainFeed(s);
        feed.Tick(0.1, false, 100);
        Assert.Equal(1, frame.CoreCharge);
        feed.Tick(0.1, false, 90);
        Assert.Equal(0.9, frame.CoreCharge, 6);
        feed.Tick(0.1, false, 95);
        Assert.Equal(0.9, frame.CoreCharge, 6);
        feed.Tick(0.1, false, 85);
        Assert.Equal(0.8, frame.CoreCharge, 6);
    }

    [Fact]
    public void Feed_outside_the_armor_does_nothing()
    {
        var (s, frame) = Armored(new PowerArmorDrain(new PowerArmorDrainRates(5, 5), 100));
        s.Exit();
        var feed = new PowerArmorDrainFeed(s);
        feed.Tick(1, true, 100);
        feed.Tick(1, true, 0);
        Assert.Equal(1, frame.CoreCharge);
    }
}
