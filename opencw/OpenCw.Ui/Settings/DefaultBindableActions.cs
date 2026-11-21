using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class DefaultBindableActions
{
    private const string Context = "Gameplay";

    public static IReadOnlyList<BindableAction> All { get; } = new[]
    {
        new BindableAction("move_forward", "Move forward", Context, "W", "Up"),
        new BindableAction("move_back", "Move backward", Context, "S", "Down"),
        new BindableAction("move_left", "Move left", Context, "A", "Left"),
        new BindableAction("move_right", "Move right", Context, "D", "Right"),
        new BindableAction("jump", "Jump", Context, "Space", null, "Pad A"),
        new BindableAction("sprint", "Sprint", Context, "Shift", null, "Pad LeftStick"),
        new BindableAction("sneak", "Sneak", Context, "Ctrl", null, "Pad RightStick"),
        new BindableAction("interact", "Interact", Context, "E", null, "Pad X"),
        new BindableAction("attack", "Attack", Context, "Mouse Left", null, "Pad RightShoulder"),
        new BindableAction("aim", "Aim", Context, "Mouse Right", null, "Pad LeftShoulder"),
        new BindableAction("reload", "Reload", Context, "R", null, "Pad Y"),
        new BindableAction("quick_save", "Quick save", Context, "F5"),
        new BindableAction("quick_load", "Quick load", Context, "F9"),
    };
}
