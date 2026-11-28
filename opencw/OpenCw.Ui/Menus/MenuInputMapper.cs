using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class MenuInputMapper
{
    public const double AxisPress = 0.5;
    public const double AxisRelease = 0.35;

    private static readonly IReadOnlyList<MenuActionChange> None = Array.Empty<MenuActionChange>();

    private readonly Dictionary<(MenuDevice Device, int Code), MenuInputAction> _held = new();
    private readonly Dictionary<(MenuPadAxis Axis, bool Positive), MenuInputAction> _axisHeld = new();

    public IReadOnlyList<MenuActionChange> Map(MenuInputEvent e) => e.Device switch
    {
        MenuDevice.Keyboard => MapKey(e),
        MenuDevice.Controller => e.Axis != MenuPadAxis.None ? MapAxis(e) : MapPad(e),
        MenuDevice.Mouse => MapMouse(e),
        _ => None,
    };

    public void Reset()
    {
        _held.Clear();
        _axisHeld.Clear();
    }

    private IReadOnlyList<MenuActionChange> MapKey(MenuInputEvent e)
    {
        if (e.Key == MenuKey.None || e.Echo) return None;
        var action = FindKey(e.Key, e.Modifiers);
        return Track((MenuDevice.Keyboard, (int)e.Key), e.Pressed, action);
    }

    private IReadOnlyList<MenuActionChange> MapPad(MenuInputEvent e)
    {
        if (e.Pad == MenuPadButton.None) return None;
        MenuInputAction? action = null;
        foreach (var binding in MenuInputBindings.Pad)
        {
            if (binding.Button == e.Pad) { action = binding.Action; break; }
        }
        return Track((MenuDevice.Controller, (int)e.Pad), e.Pressed, action);
    }

    private IReadOnlyList<MenuActionChange> MapMouse(MenuInputEvent e)
    {
        if (e.Mouse == MenuMouseButton.None) return None;
        var action = FindMouse(e.Mouse, e.Modifiers);
        if (IsWheel(e.Mouse))
        {
            if (!e.Pressed || action is not { } wheel) return None;
            return new[] { new MenuActionChange(wheel, MenuPhase.Pulse) };
        }
        return Track((MenuDevice.Mouse, (int)e.Mouse), e.Pressed, action);
    }

    private IReadOnlyList<MenuActionChange> MapAxis(MenuInputEvent e)
    {
        List<MenuActionChange>? changes = null;
        foreach (var binding in MenuInputBindings.Axes)
        {
            if (binding.Axis != e.Axis) continue;
            var key = (binding.Axis, binding.Positive);
            var magnitude = binding.Positive ? e.Value : -e.Value;
            var held = _axisHeld.ContainsKey(key);
            if (!held && magnitude >= AxisPress)
            {
                _axisHeld[key] = binding.Action;
                (changes ??= new()).Add(new MenuActionChange(binding.Action, MenuPhase.Press));
            }
            else if (held && magnitude < AxisRelease)
            {
                _axisHeld.Remove(key);
                (changes ??= new()).Add(new MenuActionChange(binding.Action, MenuPhase.Release));
            }
        }
        return changes ?? None;
    }

    private IReadOnlyList<MenuActionChange> Track((MenuDevice, int) code, bool pressed, MenuInputAction? action)
    {
        if (pressed)
        {
            if (action is not { } found || _held.ContainsKey(code)) return None;
            _held[code] = found;
            return new[] { new MenuActionChange(found, MenuPhase.Press) };
        }
        if (!_held.Remove(code, out var released)) return None;
        return new[] { new MenuActionChange(released, MenuPhase.Release) };
    }

    private static bool IsWheel(MenuMouseButton button) =>
        button is MenuMouseButton.WheelUp or MenuMouseButton.WheelDown
            or MenuMouseButton.WheelLeft or MenuMouseButton.WheelRight;

    private static MenuInputAction? FindKey(MenuKey key, MenuModifiers modifiers)
    {
        foreach (var binding in MenuInputBindings.Keyboard)
        {
            if (binding.Key == key && binding.Modifiers == modifiers) return binding.Action;
        }
        return null;
    }

    private static MenuInputAction? FindMouse(MenuMouseButton button, MenuModifiers modifiers)
    {
        foreach (var binding in MenuInputBindings.Mouse)
        {
            if (binding.Button == button && binding.Modifiers == modifiers) return binding.Action;
        }
        return null;
    }
}
