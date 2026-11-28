using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuInputRepeatTests
{
    static readonly MenuRepeatTiming Timing = new(0.5, 0.25);

    static MenuInputAction[] Advance(MenuInputRepeater repeater, double delta) => repeater.Advance(delta).ToArray();

    static MenuInputAction[] Advance(MenuInputDriver driver, double delta) => driver.Advance(delta).ToArray();

    [Fact]
    public void Nothing_repeats_before_the_initial_delay_then_one_action_fires_each_interval()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, 0.25));
        Assert.Empty(Advance(repeater, 0.125));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.125));
        Assert.Empty(Advance(repeater, 0.125));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.125));
    }

    [Fact]
    public void A_long_frame_fires_every_interval_it_covered()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);

        Assert.Equal(new[] { MenuInputAction.Up, MenuInputAction.Up, MenuInputAction.Up }, Advance(repeater, 1.0));
    }

    [Fact]
    public void A_huge_frame_is_capped_instead_of_flooding()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);

        var fired = Advance(repeater, 1000.0);

        Assert.InRange(fired.Length, 1, 10);
        Assert.Empty(Advance(repeater, 0.125));
    }

    [Fact]
    public void Releasing_stops_the_repeat_and_a_new_press_restarts_the_delay()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);
        Advance(repeater, 0.5);

        repeater.Release(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, 5.0));
        repeater.Press(MenuInputAction.Down);
        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Theory]
    [InlineData(MenuInputAction.Accept)]
    [InlineData(MenuInputAction.Back)]
    [InlineData(MenuInputAction.Search)]
    [InlineData(MenuInputAction.Sort)]
    [InlineData(MenuInputAction.Compare)]
    [InlineData(MenuInputAction.Favourite)]
    [InlineData(MenuInputAction.TabPrev)]
    [InlineData(MenuInputAction.TabNext)]
    public void Only_movement_actions_repeat(MenuInputAction action)
    {
        var repeater = new MenuInputRepeater(Timing);

        repeater.Press(action);

        Assert.False(MenuInputRepeater.IsRepeatable(action));
        Assert.Empty(Advance(repeater, 10.0));
    }

    [Fact]
    public void Movement_actions_repeat()
    {
        foreach (var action in new[] { MenuInputAction.Up, MenuInputAction.Down, MenuInputAction.Left, MenuInputAction.Right, MenuInputAction.PageUp, MenuInputAction.PageDown })
            Assert.True(MenuInputRepeater.IsRepeatable(action));
    }

    [Fact]
    public void The_latest_held_direction_repeats_and_the_earlier_one_resumes_after_a_fresh_delay()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);
        repeater.Press(MenuInputAction.Down);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.5));

        repeater.Release(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Up }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Bad_deltas_are_ignored()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, -1.0));
        Assert.Empty(Advance(repeater, double.NaN));
        Assert.Empty(Advance(repeater, 0.0));
        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Driver_emits_the_press_immediately_and_repeats_while_held()
    {
        var driver = new MenuInputDriver(Timing);

        var first = driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true));

        Assert.Equal(new[] { MenuInputAction.Down }, first.ToArray());
        Assert.Empty(Advance(driver, 0.25));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(driver, 0.25));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, false)));
        Assert.Empty(Advance(driver, 5.0));
    }

    [Fact]
    public void Driver_does_not_repeat_accept_or_wheel_pulses()
    {
        var driver = new MenuInputDriver(Timing);

        Assert.Equal(new[] { MenuInputAction.Accept }, driver.Handle(MenuInputEvent.FromPad(MenuPadButton.A, true)).ToArray());
        Assert.Equal(new[] { MenuInputAction.Down }, driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, true)).ToArray());
        Assert.Equal(new[] { MenuInputAction.Down }, driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, true)).ToArray());
        Assert.Empty(Advance(driver, 5.0));
    }

    [Fact]
    public void Dpad_and_stick_holding_the_same_direction_emit_once_and_repeat_until_both_release()
    {
        var driver = new MenuInputDriver(Timing);

        var dpad = driver.Handle(MenuInputEvent.FromPad(MenuPadButton.DpadUp, true));
        var stick = driver.Handle(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -1.0));

        Assert.Equal(new[] { MenuInputAction.Up }, dpad.ToArray());
        Assert.Empty(stick);

        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.DpadUp, false));
        Assert.Equal(new[] { MenuInputAction.Up }, Advance(driver, 0.5));

        driver.Handle(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, 0.0));
        Assert.Empty(Advance(driver, 5.0));
    }

    [Fact]
    public void Driver_tracks_the_last_device_that_produced_an_action()
    {
        var driver = new MenuInputDriver(Timing);
        Assert.False(driver.UsingController);

        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.A, true));
        Assert.Equal(MenuDevice.Controller, driver.LastDevice);
        Assert.True(driver.UsingController);

        driver.Handle(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, 0.1));
        Assert.True(driver.UsingController);

        driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.WheelUp, true));
        Assert.Equal(MenuDevice.Mouse, driver.LastDevice);

        driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, true));
        Assert.Equal(MenuDevice.Keyboard, driver.LastDevice);
    }

    [Fact]
    public void Stick_noise_below_the_threshold_does_not_flip_the_device()
    {
        var driver = new MenuInputDriver(Timing);
        driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, true));

        driver.Handle(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, 0.2));

        Assert.Equal(MenuDevice.Keyboard, driver.LastDevice);
    }

    [Fact]
    public void Reset_drops_held_directions()
    {
        var driver = new MenuInputDriver(Timing);
        driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true));

        driver.Reset();

        Assert.Empty(Advance(driver, 5.0));
        Assert.Equal(new[] { MenuInputAction.Down }, driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true)).ToArray());
    }
}
