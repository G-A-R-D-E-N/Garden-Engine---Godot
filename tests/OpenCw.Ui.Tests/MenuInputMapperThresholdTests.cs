using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuInputMapperThresholdTests
{
    static MenuActionChange Press(MenuInputAction action) => new(action, MenuPhase.Press);

    static MenuActionChange Release(MenuInputAction action) => new(action, MenuPhase.Release);

    static MenuActionChange Pulse(MenuInputAction action) => new(action, MenuPhase.Pulse);

    [Fact]
    public void The_axis_thresholds_are_half_to_press_and_just_over_a_third_to_release()
    {
        Assert.Equal(0.5, MenuInputMapper.AxisPress);
        Assert.Equal(0.35, MenuInputMapper.AxisRelease);
    }

    [Fact]
    public void A_stick_presses_at_exactly_the_press_threshold_and_not_below_it()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.49)));
        Assert.Equal(new[] { Press(MenuInputAction.Up) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.5)).ToArray());
    }

    [Fact]
    public void A_held_stick_stays_pressed_at_exactly_the_release_threshold_and_releases_just_below_it()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -1.0));

        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.35)));
        Assert.Equal(new[] { Release(MenuInputAction.Up) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.349)).ToArray());
    }

    [Fact]
    public void A_stick_pushed_down_presses_down_only_and_one_pushed_right_presses_right_only()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(new[] { Press(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, 0.9)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Right) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, 0.9)).ToArray());
        Assert.Equal(new[] { Release(MenuInputAction.Right) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, 0.0)).ToArray());
    }

    [Fact]
    public void A_trigger_pulled_the_wrong_way_does_nothing()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerLeft, -1.0)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerRight, -1.0)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerLeft, 0.49)));
    }

    [Fact]
    public void A_held_trigger_releases_when_it_drops_below_the_release_threshold()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerRight, 0.5));

        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerRight, 0.35)));
        Assert.Equal(new[] { Release(MenuInputAction.PageDown) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerRight, 0.3)).ToArray());
    }

    [Fact]
    public void Controller_events_with_neither_a_button_nor_an_axis_and_unknown_devices_map_to_nothing()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(new MenuInputEvent { Device = MenuDevice.Controller, Pressed = true }));
        Assert.Empty(mapper.Map(new MenuInputEvent { Device = (MenuDevice)99, Key = MenuKey.Down, Pressed = true }));
    }

    [Fact]
    public void Pad_buttons_ignore_duplicate_presses_and_unmatched_releases_and_reset_forgets_them()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromPad(MenuPadButton.A, false)));
        Assert.Equal(new[] { Press(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromPad(MenuPadButton.A, true)).ToArray());
        Assert.Empty(mapper.Map(MenuInputEvent.FromPad(MenuPadButton.A, true)));

        mapper.Reset();

        Assert.Empty(mapper.Map(MenuInputEvent.FromPad(MenuPadButton.A, false)));
        Assert.Equal(new[] { Press(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromPad(MenuPadButton.A, true)).ToArray());
    }

    [Fact]
    public void Mouse_buttons_with_no_binding_or_an_unbound_modifier_do_nothing()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.None, true)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Alt | MenuModifiers.Ctrl)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelLeft, true, MenuModifiers.Shift)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelUp, true, MenuModifiers.Alt)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelUp, false, MenuModifiers.Ctrl)));
    }

    [Fact]
    public void Horizontal_wheel_and_the_other_buttons_map_to_their_actions()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(new[] { Pulse(MenuInputAction.Left) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelLeft, true)).ToArray());
        Assert.Equal(new[] { Pulse(MenuInputAction.Right) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelRight, true)).ToArray());
        Assert.Equal(new[] { Pulse(MenuInputAction.Right) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, true, MenuModifiers.Shift)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Back) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Right, true)).ToArray());
        Assert.Equal(new[] { Release(MenuInputAction.Back) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Right, false)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Favourite) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Middle, true)).ToArray());
    }

    [Fact]
    public void A_key_echo_is_ignored_even_for_a_release_and_the_real_release_still_arrives()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, true));

        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, false, echo: true)));
        Assert.Equal(new[] { Release(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, false)).ToArray());
    }

    [Fact]
    public void Two_keys_bound_to_one_action_are_tracked_separately_by_the_mapper()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(new[] { Press(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Enter, true)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Space, true)).ToArray());
        Assert.Equal(new[] { Release(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Enter, false)).ToArray());
        Assert.Equal(new[] { Release(MenuInputAction.Accept) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Space, false)).ToArray());
    }

    [Theory]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Up, "Up")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Down, "Down")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Left, "Left")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Right, "Right")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Accept, "Enter")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Back, "Esc")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.PageUp, "PgUp")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.PageDown, "PgDn")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Search, "/")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Sort, "S")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Compare, "C")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.Favourite, "F")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.TabPrev, "Shift+Tab")]
    [InlineData(MenuDevice.Keyboard, MenuInputAction.TabNext, "Tab")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Up, "Up")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Accept, "A")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Back, "B")]
    [InlineData(MenuDevice.Controller, MenuInputAction.PageUp, "LT")]
    [InlineData(MenuDevice.Controller, MenuInputAction.PageDown, "RT")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Search, "View")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Sort, "Menu")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Compare, "Y")]
    [InlineData(MenuDevice.Controller, MenuInputAction.Favourite, "X")]
    [InlineData(MenuDevice.Controller, MenuInputAction.TabPrev, "LB")]
    [InlineData(MenuDevice.Controller, MenuInputAction.TabNext, "RB")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Up, "Wheel up")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Down, "Wheel down")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Left, "Shift+Wheel up")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Right, "Shift+Wheel down")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.PageUp, "Ctrl+Wheel up")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.PageDown, "Ctrl+Wheel down")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Accept, "Click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Back, "Right click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Search, "Alt+Click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Sort, "Shift+Click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Compare, "Ctrl+Click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.Favourite, "Middle click")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.TabPrev, "Mouse back")]
    [InlineData(MenuDevice.Mouse, MenuInputAction.TabNext, "Mouse forward")]
    public void The_primary_label_is_the_first_binding_listed_for_the_action_on_that_device(
        MenuDevice device, MenuInputAction action, string expected)
    {
        Assert.Equal(expected, MenuInputBindings.PrimaryLabel(action, device));
    }

    [Fact]
    public void An_unknown_device_has_no_label_and_throws()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MenuInputBindings.PrimaryLabel(MenuInputAction.Up, (MenuDevice)42));

        Assert.Equal("device", thrown.ParamName);
    }

    [Theory]
    [InlineData(MenuInputAction.Up, MenuPadButton.DpadUp)]
    [InlineData(MenuInputAction.Down, MenuPadButton.DpadDown)]
    [InlineData(MenuInputAction.Left, MenuPadButton.DpadLeft)]
    [InlineData(MenuInputAction.Right, MenuPadButton.DpadRight)]
    [InlineData(MenuInputAction.Back, MenuPadButton.B)]
    [InlineData(MenuInputAction.Search, MenuPadButton.Back)]
    [InlineData(MenuInputAction.Sort, MenuPadButton.Start)]
    [InlineData(MenuInputAction.Compare, MenuPadButton.Y)]
    [InlineData(MenuInputAction.Favourite, MenuPadButton.X)]
    [InlineData(MenuInputAction.TabPrev, MenuPadButton.LeftShoulder)]
    [InlineData(MenuInputAction.TabNext, MenuPadButton.RightShoulder)]
    public void Pad_button_lookup_returns_the_button_bound_to_each_action(MenuInputAction action, MenuPadButton expected)
    {
        Assert.Equal(expected, MenuInputBindings.PadButtonFor(action));
    }

    [Fact]
    public void Only_the_trigger_actions_have_no_pad_button()
    {
        Assert.Null(MenuInputBindings.PadButtonFor(MenuInputAction.PageDown));
        Assert.Equal(new[] { MenuInputAction.PageUp, MenuInputAction.PageDown },
            Enum.GetValues<MenuInputAction>().Where(a => MenuInputBindings.PadButtonFor(a) is null).ToArray());
    }
}
