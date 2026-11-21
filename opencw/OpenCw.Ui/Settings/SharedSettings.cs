using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class SharedSettings
{
    public const double TitleSmaa = 2;

    private static readonly Dictionary<string, string> TitleIds = new(StringComparer.Ordinal)
    {
        ["display.mode"] = "window_mode",
        ["display.vsync"] = "vsync",
        ["display.render_scale"] = "render_scale",
        ["graphics.shadows"] = "shadow_quality",
        ["graphics.anti_aliasing"] = "anti_aliasing",
        ["audio.master"] = "master_volume",
        ["controls.invert_y"] = "invert_y",
    };

    private static readonly string[] Modes = { "windowed", "borderless", "fullscreen" };

    private static readonly string[] Shadows = { "low", "medium", "high", "high" };

    public static IEnumerable<string> Ids => TitleIds.Keys;

    public static bool IsShared(string id) => TitleIds.ContainsKey(id);

    public static string TitleId(string id) => TitleIds[id];

    public static SettingValue ToModel(string id, double title) => id switch
    {
        "display.mode" => SettingValue.Choice(Modes[Index(title, Modes.Length)]),
        "display.vsync" => SettingValue.Toggle(title >= 0.5),
        "display.render_scale" => SettingValue.Slider(Math.Clamp(title * 100, 50, 100)),
        "graphics.shadows" => SettingValue.Choice(Shadows[Index(title, Shadows.Length)]),
        "graphics.anti_aliasing" => SettingValue.Toggle(title >= 0.5),
        "audio.master" => SettingValue.Slider(Math.Clamp(title * 100, 0, 100)),
        "controls.invert_y" => SettingValue.Toggle(title > 0),
        _ => throw new ArgumentException($"'{id}' is not a shared setting.", nameof(id)),
    };

    public static double ToTitle(string id, SettingValue value, double currentTitle) => id switch
    {
        "display.mode" => Math.Max(0, Array.IndexOf(Modes, value.Text)),
        "display.vsync" => value.Flag ? (currentTitle >= 0.5 ? currentTitle : 1) : 0,
        "display.render_scale" => value.Number / 100,
        "graphics.shadows" => value.Text switch
        {
            "medium" => 1,
            "high" => currentTitle >= 3 ? 3 : 2,
            _ => 0,
        },
        "graphics.anti_aliasing" => value.Flag ? (currentTitle >= 0.5 ? currentTitle : TitleSmaa) : 0,
        "audio.master" => value.Number / 100,
        "controls.invert_y" => value.Flag ? 1 : 0,
        _ => throw new ArgumentException($"'{id}' is not a shared setting.", nameof(id)),
    };

    private static int Index(double title, int count) => Math.Clamp((int)Math.Round(title), 0, count - 1);
}
