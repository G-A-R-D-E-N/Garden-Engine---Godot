using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Menus;

public readonly record struct KeyBinding(MenuKey Key, MenuModifiers Modifiers, MenuInputAction Action, string Label);

public readonly record struct PadBinding(MenuPadButton Button, MenuInputAction Action, string Label);

public readonly record struct AxisBinding(MenuPadAxis Axis, bool Positive, MenuInputAction Action, string Label);

public readonly record struct MouseBinding(MenuMouseButton Button, MenuModifiers Modifiers, MenuInputAction Action, string Label);

public static class MenuInputBindings
{
    public static IReadOnlyList<KeyBinding> Keyboard { get; } =
    [
        new(MenuKey.Up, MenuModifiers.None, MenuInputAction.Up, "Up"),
        new(MenuKey.Down, MenuModifiers.None, MenuInputAction.Down, "Down"),
        new(MenuKey.Left, MenuModifiers.None, MenuInputAction.Left, "Left"),
        new(MenuKey.Right, MenuModifiers.None, MenuInputAction.Right, "Right"),
        new(MenuKey.Enter, MenuModifiers.None, MenuInputAction.Accept, "Enter"),
        new(MenuKey.Space, MenuModifiers.None, MenuInputAction.Accept, "Space"),
        new(MenuKey.Escape, MenuModifiers.None, MenuInputAction.Back, "Esc"),
        new(MenuKey.Backspace, MenuModifiers.None, MenuInputAction.Back, "Backspace"),
        new(MenuKey.PageUp, MenuModifiers.None, MenuInputAction.PageUp, "PgUp"),
        new(MenuKey.PageDown, MenuModifiers.None, MenuInputAction.PageDown, "PgDn"),
        new(MenuKey.Slash, MenuModifiers.None, MenuInputAction.Search, "/"),
        new(MenuKey.F, MenuModifiers.Ctrl, MenuInputAction.Search, "Ctrl+F"),
        new(MenuKey.S, MenuModifiers.None, MenuInputAction.Sort, "S"),
        new(MenuKey.C, MenuModifiers.None, MenuInputAction.Compare, "C"),
        new(MenuKey.F, MenuModifiers.None, MenuInputAction.Favourite, "F"),
        new(MenuKey.Tab, MenuModifiers.Shift, MenuInputAction.TabPrev, "Shift+Tab"),
        new(MenuKey.Q, MenuModifiers.None, MenuInputAction.TabPrev, "Q"),
        new(MenuKey.Tab, MenuModifiers.None, MenuInputAction.TabNext, "Tab"),
        new(MenuKey.E, MenuModifiers.None, MenuInputAction.TabNext, "E"),
    ];

    public static IReadOnlyList<PadBinding> Pad { get; } =
    [
        new(MenuPadButton.DpadUp, MenuInputAction.Up, "Up"),
        new(MenuPadButton.DpadDown, MenuInputAction.Down, "Down"),
        new(MenuPadButton.DpadLeft, MenuInputAction.Left, "Left"),
        new(MenuPadButton.DpadRight, MenuInputAction.Right, "Right"),
        new(MenuPadButton.A, MenuInputAction.Accept, "A"),
        new(MenuPadButton.B, MenuInputAction.Back, "B"),
        new(MenuPadButton.Back, MenuInputAction.Search, "View"),
        new(MenuPadButton.Start, MenuInputAction.Sort, "Menu"),
        new(MenuPadButton.Y, MenuInputAction.Compare, "Y"),
        new(MenuPadButton.X, MenuInputAction.Favourite, "X"),
        new(MenuPadButton.LeftShoulder, MenuInputAction.TabPrev, "LB"),
        new(MenuPadButton.RightShoulder, MenuInputAction.TabNext, "RB"),
    ];

    public static IReadOnlyList<AxisBinding> Axes { get; } =
    [
        new(MenuPadAxis.LeftY, false, MenuInputAction.Up, "Stick up"),
        new(MenuPadAxis.LeftY, true, MenuInputAction.Down, "Stick down"),
        new(MenuPadAxis.LeftX, false, MenuInputAction.Left, "Stick left"),
        new(MenuPadAxis.LeftX, true, MenuInputAction.Right, "Stick right"),
        new(MenuPadAxis.TriggerLeft, true, MenuInputAction.PageUp, "LT"),
        new(MenuPadAxis.TriggerRight, true, MenuInputAction.PageDown, "RT"),
    ];

    public static IReadOnlyList<MouseBinding> Mouse { get; } =
    [
        new(MenuMouseButton.WheelUp, MenuModifiers.None, MenuInputAction.Up, "Wheel up"),
        new(MenuMouseButton.WheelDown, MenuModifiers.None, MenuInputAction.Down, "Wheel down"),
        new(MenuMouseButton.WheelUp, MenuModifiers.Shift, MenuInputAction.Left, "Shift+Wheel up"),
        new(MenuMouseButton.WheelDown, MenuModifiers.Shift, MenuInputAction.Right, "Shift+Wheel down"),
        new(MenuMouseButton.WheelLeft, MenuModifiers.None, MenuInputAction.Left, "Wheel left"),
        new(MenuMouseButton.WheelRight, MenuModifiers.None, MenuInputAction.Right, "Wheel right"),
        new(MenuMouseButton.WheelUp, MenuModifiers.Ctrl, MenuInputAction.PageUp, "Ctrl+Wheel up"),
        new(MenuMouseButton.WheelDown, MenuModifiers.Ctrl, MenuInputAction.PageDown, "Ctrl+Wheel down"),
        new(MenuMouseButton.Left, MenuModifiers.None, MenuInputAction.Accept, "Click"),
        new(MenuMouseButton.Right, MenuModifiers.None, MenuInputAction.Back, "Right click"),
        new(MenuMouseButton.Left, MenuModifiers.Alt, MenuInputAction.Search, "Alt+Click"),
        new(MenuMouseButton.Left, MenuModifiers.Shift, MenuInputAction.Sort, "Shift+Click"),
        new(MenuMouseButton.Left, MenuModifiers.Ctrl, MenuInputAction.Compare, "Ctrl+Click"),
        new(MenuMouseButton.Middle, MenuModifiers.None, MenuInputAction.Favourite, "Middle click"),
        new(MenuMouseButton.X1, MenuModifiers.None, MenuInputAction.TabPrev, "Mouse back"),
        new(MenuMouseButton.X2, MenuModifiers.None, MenuInputAction.TabNext, "Mouse forward"),
    ];

    public static string? PrimaryLabel(MenuInputAction action, MenuDevice device) => device switch
    {
        MenuDevice.Keyboard => Keyboard.Where(b => b.Action == action).Select(b => b.Label).FirstOrDefault(),
        MenuDevice.Controller => Pad.Where(b => b.Action == action).Select(b => b.Label)
            .Concat(Axes.Where(b => b.Action == action).Select(b => b.Label)).FirstOrDefault(),
        MenuDevice.Mouse => Mouse.Where(b => b.Action == action).Select(b => b.Label).FirstOrDefault(),
        _ => throw new ArgumentOutOfRangeException(nameof(device), device, null),
    };

    public static MenuPadButton? PadButtonFor(MenuInputAction action) =>
        Pad.Where(b => b.Action == action).Select(b => (MenuPadButton?)b.Button).FirstOrDefault();
}
