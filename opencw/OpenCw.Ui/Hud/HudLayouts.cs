using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Layout;

namespace OpenCommonwealth.Services.Ui.Hud;

public static class HudSlots
{
    public const string Menu = "hud";

    public static string Key(HudElement element) => element switch
    {
        HudElement.Health => "health",
        HudElement.ActionPoints => "ap",
        HudElement.Ammo => "ammo",
        HudElement.Compass => "compass",
        HudElement.Crosshair => "crosshair",
        HudElement.Objective => "objective",
        HudElement.Interaction => "interaction",
        HudElement.Toasts => "toasts",
        HudElement.Subtitles => "subtitles",
        _ => throw new ArgumentOutOfRangeException(nameof(element)),
    };
}

public static class HudLayouts
{
    public static MenuLayout Default { get; } = Build();

    private static MenuLayout Build()
    {
        var rows = new List<LayoutRow>
        {
            Slot(HudElement.Compass, 440, 16, 400, 44),
            Slot(HudElement.Objective, 40, 24, 340, 64),
            Slot(HudElement.Toasts, 880, 24, 360, 200),
            Slot(HudElement.Crosshair, 624, 344, 32, 32),
            Slot(HudElement.Interaction, 480, 440, 320, 56),
            Slot(HudElement.Subtitles, 240, 568, 800, 80),
            Slot(HudElement.Health, 40, 664, 280, 20),
            Slot(HudElement.ActionPoints, 960, 664, 280, 20),
            Slot(HudElement.Ammo, 1080, 624, 160, 32),
        };
        return new MenuLayout(HudSlots.Menu, DefaultLayouts.State, DefaultLayouts.StageWidth, DefaultLayouts.StageHeight, [], rows);
    }

    private static LayoutRow Slot(HudElement element, double x, double y, double width, double height) =>
        new(HudSlots.Key(element), LayoutRowKind.Shape, 0, true, new LayoutRect(x, y, width, height), null);
}
