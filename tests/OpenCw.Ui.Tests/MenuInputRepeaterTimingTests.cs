using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuInputRepeaterTimingTests
{
    static readonly MenuRepeatTiming Timing = new(0.5, 0.25);

    static MenuInputAction[] Advance(MenuInputRepeater repeater, double delta) => repeater.Advance(delta).ToArray();

    static MenuInputAction[] Advance(MenuInputDriver driver, double delta) => driver.Advance(delta).ToArray();

    [Fact]
    public void The_default_timing_is_a_400_ms_delay_then_80_ms_intervals()
    {
        Assert.Equal(new MenuRepeatTiming(0.4, 0.08), new MenuRepeatTiming());

        var early = new MenuInputRepeater();
        early.Press(MenuInputAction.Down);
        Assert.Empty(Advance(early, 0.39));

        var repeater = new MenuInputRepeater();
        repeater.Press(MenuInputAction.Down);
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.4));
        Assert.Empty(Advance(repeater, 0.04));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.04));
    }

    [Fact]
    public void A_huge_frame_fires_exactly_ten_times_and_then_waits_one_full_interval()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);

        Assert.Equal(Enumerable.Repeat(MenuInputAction.Up, 10).ToArray(), Advance(repeater, 1000.0));
        Assert.Empty(Advance(repeater, 0.125));
        Assert.Equal(new[] { MenuInputAction.Up }, Advance(repeater, 0.125));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void A_zero_or_negative_interval_is_floored_at_one_millisecond(double interval)
    {
        var repeater = new MenuInputRepeater(new MenuRepeatTiming(0.5, interval));
        repeater.Press(MenuInputAction.Down);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.5));
        Assert.Equal(3, Advance(repeater, 0.0035).Length);
    }

    [Fact]
    public void Releasing_a_direction_that_is_not_the_latest_leaves_the_running_delay_alone()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);
        repeater.Press(MenuInputAction.Down);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Release(MenuInputAction.Up);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Releasing_a_direction_that_is_not_held_changes_nothing()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Release(MenuInputAction.Up);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Pressing_a_direction_that_is_already_held_does_not_restart_its_delay()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Press(MenuInputAction.Down);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Pressing_a_non_repeatable_action_leaves_the_running_repeat_alone()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Press(MenuInputAction.Accept);

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Pressing_a_second_direction_restarts_the_initial_delay_for_it()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Press(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Releasing_the_middle_of_three_held_directions_keeps_the_latest_and_releasing_it_resumes_the_oldest()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);
        repeater.Press(MenuInputAction.Down);
        repeater.Press(MenuInputAction.Left);
        Assert.Empty(Advance(repeater, 0.25));

        repeater.Release(MenuInputAction.Down);
        Assert.Equal(new[] { MenuInputAction.Left }, Advance(repeater, 0.25));

        repeater.Release(MenuInputAction.Left);
        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Up }, Advance(repeater, 0.25));
    }

    [Fact]
    public void Clear_forgets_every_held_direction_and_a_later_press_starts_a_fresh_delay()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Up);
        repeater.Press(MenuInputAction.Down);

        repeater.Clear();

        Assert.Empty(Advance(repeater, 5.0));
        repeater.Press(MenuInputAction.Left);
        Assert.Empty(Advance(repeater, 0.25));
        Assert.Equal(new[] { MenuInputAction.Left }, Advance(repeater, 0.25));
    }

    [Fact]
    public void An_infinite_delta_is_ignored_without_consuming_the_delay()
    {
        var repeater = new MenuInputRepeater(Timing);
        repeater.Press(MenuInputAction.Down);

        Assert.Empty(Advance(repeater, double.PositiveInfinity));
        Assert.Empty(Advance(repeater, double.NegativeInfinity));

        Assert.Equal(new[] { MenuInputAction.Down }, Advance(repeater, 0.5));
    }

    [Fact]
    public void The_driver_can_be_told_the_device_without_an_event()
    {
        var driver = new MenuInputDriver(Timing);

        driver.NoteDevice(MenuDevice.Mouse);
        Assert.Equal(MenuDevice.Mouse, driver.LastDevice);
        Assert.False(driver.UsingController);

        driver.NoteDevice(MenuDevice.Controller);
        Assert.True(driver.UsingController);
    }

    [Fact]
    public void Events_that_map_to_nothing_do_not_change_the_last_device()
    {
        var driver = new MenuInputDriver(Timing);
        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.A, true));

        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true, echo: true)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.None, true)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, false)));

        Assert.Equal(MenuDevice.Controller, driver.LastDevice);
    }

    [Fact]
    public void A_release_that_maps_to_a_held_action_updates_the_last_device()
    {
        var driver = new MenuInputDriver(Timing);
        driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true));
        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.A, true));
        Assert.Equal(MenuDevice.Controller, driver.LastDevice);

        driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, false));

        Assert.Equal(MenuDevice.Keyboard, driver.LastDevice);
    }

    [Fact]
    public void Two_keys_bound_to_one_action_emit_it_once_and_both_must_release_before_it_can_fire_again()
    {
        var driver = new MenuInputDriver(Timing);

        Assert.Equal(new[] { MenuInputAction.Accept }, driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, true)).ToArray());
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Space, true)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, false)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, true)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Enter, false)));
        Assert.Empty(driver.Handle(MenuInputEvent.FromKey(MenuKey.Space, false)));

        Assert.Equal(new[] { MenuInputAction.Accept }, driver.Handle(MenuInputEvent.FromKey(MenuKey.Space, true)).ToArray());
    }

    [Fact]
    public void Releasing_one_of_two_inputs_holding_a_direction_keeps_the_repeat_running()
    {
        var driver = new MenuInputDriver(Timing);
        driver.Handle(MenuInputEvent.FromKey(MenuKey.Up, true));
        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.DpadUp, true));

        driver.Handle(MenuInputEvent.FromKey(MenuKey.Up, false));
        Assert.Equal(new[] { MenuInputAction.Up }, Advance(driver, 0.5));

        driver.Handle(MenuInputEvent.FromPad(MenuPadButton.DpadUp, false));
        Assert.Empty(Advance(driver, 5.0));
    }
}
