using System;

namespace OpenCommonwealth.Services.Ui.Menus;

public enum MenuInputAction
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
    PageUp,
    PageDown,
    Search,
    Sort,
    Compare,
    Favourite,
    TabPrev,
    TabNext,
}

public enum MenuDevice
{
    Keyboard,
    Controller,
    Mouse,
}

public enum MenuKey
{
    None,
    Up,
    Down,
    Left,
    Right,
    Enter,
    Space,
    Escape,
    Backspace,
    PageUp,
    PageDown,
    Tab,
    Slash,
    F,
    S,
    C,
    Q,
    E,
}

public enum MenuPadButton
{
    None,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    Start,
    Back,
}

public enum MenuPadAxis
{
    None,
    LeftX,
    LeftY,
    TriggerLeft,
    TriggerRight,
}

public enum MenuMouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
    WheelUp,
    WheelDown,
    WheelLeft,
    WheelRight,
}

[Flags]
public enum MenuModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

public enum MenuPhase
{
    Press,
    Release,
    Pulse,
}

public readonly record struct MenuActionChange(MenuInputAction Action, MenuPhase Phase);

public readonly record struct MenuInputEvent
{
    public MenuDevice Device { get; init; }
    public MenuKey Key { get; init; }
    public MenuPadButton Pad { get; init; }
    public MenuPadAxis Axis { get; init; }
    public MenuMouseButton Mouse { get; init; }
    public bool Pressed { get; init; }
    public bool Echo { get; init; }
    public double Value { get; init; }
    public MenuModifiers Modifiers { get; init; }

    public static MenuInputEvent FromKey(MenuKey key, bool pressed, MenuModifiers modifiers = MenuModifiers.None, bool echo = false) =>
        new() { Device = MenuDevice.Keyboard, Key = key, Pressed = pressed, Modifiers = modifiers, Echo = echo };

    public static MenuInputEvent FromPad(MenuPadButton button, bool pressed) =>
        new() { Device = MenuDevice.Controller, Pad = button, Pressed = pressed };

    public static MenuInputEvent FromAxis(MenuPadAxis axis, double value) =>
        new() { Device = MenuDevice.Controller, Axis = axis, Value = value };

    public static MenuInputEvent FromMouse(MenuMouseButton button, bool pressed, MenuModifiers modifiers = MenuModifiers.None) =>
        new() { Device = MenuDevice.Mouse, Mouse = button, Pressed = pressed, Modifiers = modifiers };
}
