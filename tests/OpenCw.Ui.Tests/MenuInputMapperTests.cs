using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuInputMapperTests
{
    static MenuActionChange Press(MenuInputAction action) => new(action, MenuPhase.Press);

    static MenuActionChange Release(MenuInputAction action) => new(action, MenuPhase.Release);

    static MenuActionChange Pulse(MenuInputAction action) => new(action, MenuPhase.Pulse);

    [Theory]
    [InlineData(MenuDevice.Keyboard)]
    [InlineData(MenuDevice.Controller)]
    [InlineData(MenuDevice.Mouse)]
    public void Every_action_is_reachable_from_every_device(MenuDevice device)
    {
        var covered = device switch
        {
            MenuDevice.Keyboard => MenuInputBindings.Keyboard.Select(b => b.Action),
            MenuDevice.Controller => MenuInputBindings.Pad.Select(b => b.Action).Concat(MenuInputBindings.Axes.Select(b => b.Action)),
            _ => MenuInputBindings.Mouse.Select(b => b.Action),
        };

        foreach (var action in Enum.GetValues<MenuInputAction>())
        {
            Assert.Contains(action, covered);
            Assert.False(string.IsNullOrEmpty(MenuInputBindings.PrimaryLabel(action, device)), $"{action} has no {device} label");
        }
    }

    [Fact]
    public void Every_keyboard_binding_maps_to_its_declared_action()
    {
        foreach (var binding in MenuInputBindings.Keyboard)
        {
            var changes = new MenuInputMapper().Map(MenuInputEvent.FromKey(binding.Key, true, binding.Modifiers));

            Assert.Equal(new[] { Press(binding.Action) }, changes.ToArray());
        }
    }

    [Fact]
    public void Every_controller_button_binding_maps_to_its_declared_action()
    {
        foreach (var binding in MenuInputBindings.Pad)
        {
            var changes = new MenuInputMapper().Map(MenuInputEvent.FromPad(binding.Button, true));

            Assert.Equal(new[] { Press(binding.Action) }, changes.ToArray());
        }
    }

    [Fact]
    public void Every_controller_axis_binding_maps_to_its_declared_action()
    {
        foreach (var binding in MenuInputBindings.Axes)
        {
            var changes = new MenuInputMapper().Map(MenuInputEvent.FromAxis(binding.Axis, binding.Positive ? 1.0 : -1.0));

            Assert.Equal(new[] { Press(binding.Action) }, changes.ToArray());
        }
    }

    [Fact]
    public void Every_mouse_binding_maps_to_its_declared_action()
    {
        foreach (var binding in MenuInputBindings.Mouse)
        {
            var changes = new MenuInputMapper().Map(MenuInputEvent.FromMouse(binding.Button, true, binding.Modifiers));
            var wheel = binding.Button is MenuMouseButton.WheelUp or MenuMouseButton.WheelDown
                or MenuMouseButton.WheelLeft or MenuMouseButton.WheelRight;

            Assert.Equal(new[] { wheel ? Pulse(binding.Action) : Press(binding.Action) }, changes.ToArray());
        }
    }

    [Fact]
    public void No_two_bindings_on_one_device_share_the_same_trigger()
    {
        var keys = MenuInputBindings.Keyboard.Select(b => (b.Key, b.Modifiers));
        var mouse = MenuInputBindings.Mouse.Select(b => (b.Button, b.Modifiers));
        var pad = MenuInputBindings.Pad.Select(b => b.Button);
        var axes = MenuInputBindings.Axes.Select(b => (b.Axis, b.Positive));

        Assert.Equal(keys.Count(), keys.Distinct().Count());
        Assert.Equal(mouse.Count(), mouse.Distinct().Count());
        Assert.Equal(pad.Count(), pad.Distinct().Count());
        Assert.Equal(axes.Count(), axes.Distinct().Count());
    }

    [Fact]
    public void A_key_release_releases_the_action_it_pressed_even_if_modifiers_changed()
    {
        var mapper = new MenuInputMapper();

        var down = mapper.Map(MenuInputEvent.FromKey(MenuKey.F, true, MenuModifiers.Ctrl));
        var up = mapper.Map(MenuInputEvent.FromKey(MenuKey.F, false));

        Assert.Equal(new[] { Press(MenuInputAction.Search) }, down.ToArray());
        Assert.Equal(new[] { Release(MenuInputAction.Search) }, up.ToArray());
    }

    [Fact]
    public void Plain_f_is_favourite_and_ctrl_f_is_search()
    {
        var plain = new MenuInputMapper().Map(MenuInputEvent.FromKey(MenuKey.F, true));
        var ctrl = new MenuInputMapper().Map(MenuInputEvent.FromKey(MenuKey.F, true, MenuModifiers.Ctrl));

        Assert.Equal(new[] { Press(MenuInputAction.Favourite) }, plain.ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Search) }, ctrl.ToArray());
    }

    [Fact]
    public void Key_echo_events_and_duplicate_presses_are_ignored()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, true));

        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, true, echo: true)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, true)));
        Assert.Equal(new[] { Release(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, false)).ToArray());
        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, false)));
    }

    [Fact]
    public void Unmapped_keys_and_modifier_combinations_do_nothing()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.None, true)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Up, true, MenuModifiers.Ctrl)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Up, false)));
    }

    [Fact]
    public void Stick_presses_at_the_press_threshold_and_releases_below_the_release_threshold()
    {
        var mapper = new MenuInputMapper();

        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.3)));
        Assert.Equal(new[] { Press(MenuInputAction.Up) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.6)).ToArray());
        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.4)));
        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.9)));
        Assert.Equal(new[] { Release(MenuInputAction.Up) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.2)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.Up) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, -0.7)).ToArray());
    }

    [Fact]
    public void Flicking_the_stick_through_centre_releases_one_direction_and_presses_the_other()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, 0.9));

        var changes = mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftX, -0.9));

        Assert.Equal(new[] { Press(MenuInputAction.Left) }.Concat(new[] { Release(MenuInputAction.Right) }).OrderBy(c => c.Action).ToArray(),
            changes.OrderBy(c => c.Action).ToArray());
    }

    [Fact]
    public void Triggers_page_up_and_down_and_ignore_unbound_axes()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(new[] { Press(MenuInputAction.PageUp) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerLeft, 0.8)).ToArray());
        Assert.Equal(new[] { Press(MenuInputAction.PageDown) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.TriggerRight, 1.0)).ToArray());
        Assert.Empty(mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.None, 1.0)));
    }

    [Fact]
    public void Wheel_events_pulse_once_and_ignore_the_release()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(new[] { Pulse(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, true)).ToArray());
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, false)));
        Assert.Equal(new[] { Pulse(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.WheelDown, true)).ToArray());
    }

    [Fact]
    public void Mouse_modifiers_select_the_action()
    {
        var mapper = new MenuInputMapper();

        Assert.Equal(Press(MenuInputAction.Accept), mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, true)).Single());
        mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, false));
        Assert.Equal(Press(MenuInputAction.Sort), mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Shift)).Single());
        Assert.Equal(Release(MenuInputAction.Sort), mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, false)).Single());
        Assert.Empty(mapper.Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Shift | MenuModifiers.Ctrl)));
    }

    [Fact]
    public void A_modified_click_released_without_the_modifier_does_not_kill_later_modified_clicks()
    {
        var driver = new MenuInputDriver();

        var first = driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Shift));
        var release = driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, false));
        var second = driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Shift));

        Assert.Equal(new[] { MenuInputAction.Sort }, first.ToArray());
        Assert.Empty(release);
        Assert.Equal(new[] { MenuInputAction.Sort }, second.ToArray());
    }

    [Fact]
    public void An_unmatched_release_produces_no_actions_in_the_driver_and_the_mapper()
    {
        var driver = new MenuInputDriver();

        Assert.Empty(driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, false)));
        Assert.Empty(new MenuInputMapper().Map(MenuInputEvent.FromMouse(MenuMouseButton.Left, false)));
        Assert.Equal(new[] { MenuInputAction.Compare },
            driver.Handle(MenuInputEvent.FromMouse(MenuMouseButton.Left, true, MenuModifiers.Ctrl)).ToArray());
    }

    [Fact]
    public void Reset_forgets_held_inputs()
    {
        var mapper = new MenuInputMapper();
        mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, true));
        mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, 0.9));

        mapper.Reset();

        Assert.Empty(mapper.Map(MenuInputEvent.FromKey(MenuKey.Down, false)));
        Assert.Equal(new[] { Press(MenuInputAction.Down) }, mapper.Map(MenuInputEvent.FromAxis(MenuPadAxis.LeftY, 0.9)).ToArray());
    }

    [Fact]
    public void Pad_button_lookup_for_hints_returns_the_bound_button_or_nothing_for_axis_only_actions()
    {
        Assert.Equal(MenuPadButton.A, MenuInputBindings.PadButtonFor(MenuInputAction.Accept));
        Assert.Null(MenuInputBindings.PadButtonFor(MenuInputAction.PageUp));
    }
}
