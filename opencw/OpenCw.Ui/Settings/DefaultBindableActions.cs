using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class DefaultBindableActions
{
    private const string Context = "Gameplay";

    private static readonly string[] ReloadShare = { "rotate_carried" };
    private static readonly string[] RotateShare = { "reload" };

    // Requires: mirrors project.godot and PlayerInput.EnsureActions.
    public static IReadOnlyList<BindableAction> All { get; } = new[]
    {
        new BindableAction("move_forward", "Move forward", Context, "W"),
        new BindableAction("move_back", "Move backward", Context, "S"),
        new BindableAction("move_left", "Move left", Context, "A"),
        new BindableAction("move_right", "Move right", Context, "D"),
        new BindableAction("jump", "Jump", Context, "Space", null, "Pad A"),
        new BindableAction("sprint", "Sprint", Context, "Shift", null, "Pad LeftStick"),
        new BindableAction("sneak", "Sneak", Context, "C", "Ctrl", "Pad B"),
        new BindableAction("toggle_run", "Toggle run", Context, "CapsLock", null, "Pad DpadUp"),
        new BindableAction("walk_modifier", "Walk", Context, "Alt"),
        new BindableAction("toggle_pov", "Toggle point of view", Context, "V", null, "Pad RightStick"),
        new BindableAction("interact", "Interact", Context, "E", null, "Pad X"),
        new BindableAction("rotate_carried", "Rotate carried object", Context, "R", null, "Pad DpadLeft", null, RotateShare),
        new BindableAction("auto_move", "Auto move", Context, "Z", null, "Pad DpadRight"),
        new BindableAction("vats", "VATS", Context, "Q", null, "Pad Y"),
        new BindableAction("attack", "Attack", Context, "Mouse Left", null, KeyBindingInputMap.TriggerRight),
        new BindableAction("aim", "Aim", Context, "Mouse Right", null, KeyBindingInputMap.TriggerLeft),
        new BindableAction("reload", "Reload", Context, "R", null, "Pad DpadDown", null, ReloadShare),
        new BindableAction("draw_weapon", "Draw weapon", Context, "F", null, "Pad LeftShoulder"),
        new BindableAction("quick_save", "Quick save", Context, "F5"),
        new BindableAction("quick_load", "Quick load", Context, "F9"),
    };

    // Requires: the table that version 1 binding files were written against.
    internal static IReadOnlyDictionary<string, BindableAction> LegacyV1 { get; } = new[]
    {
        new BindableAction("move_forward", "", Context, "W", "Up"),
        new BindableAction("move_back", "", Context, "S", "Down"),
        new BindableAction("move_left", "", Context, "A", "Left"),
        new BindableAction("move_right", "", Context, "D", "Right"),
        new BindableAction("jump", "", Context, "Space", null, "Pad A"),
        new BindableAction("sprint", "", Context, "Shift", null, "Pad LeftStick"),
        new BindableAction("sneak", "", Context, "Ctrl", null, "Pad RightStick"),
        new BindableAction("interact", "", Context, "E", null, "Pad X"),
        new BindableAction("attack", "", Context, "Mouse Left", null, "Pad RightShoulder"),
        new BindableAction("aim", "", Context, "Mouse Right", null, "Pad LeftShoulder"),
        new BindableAction("reload", "", Context, "R", null, "Pad Y"),
        new BindableAction("quick_save", "", Context, "F5"),
        new BindableAction("quick_load", "", Context, "F9"),
    }.ToDictionary(a => a.Id);
}
