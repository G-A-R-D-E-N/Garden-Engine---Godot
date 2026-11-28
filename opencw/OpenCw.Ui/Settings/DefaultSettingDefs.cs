using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class DefaultSettingDefs
{
    private static SettingChoice C(string id, string label) => new(id, label);

    public static IReadOnlyList<SettingDef> All { get; } = new[]
    {
        SettingDef.Choice("display.mode", SettingCategory.Display, "Window mode", "borderless",
            C("windowed", "Windowed"), C("borderless", "Borderless"), C("fullscreen", "Fullscreen")) with
        {
            Risky = true,
            Keywords = "screen fullscreen window",
        },
        SettingDef.Slider("display.render_scale", SettingCategory.Display, "Resolution scale", 100, 50, 100, 5) with
        {
            Risky = true,
            Keywords = "resolution scaling",
        },
        SettingDef.Toggle("display.vsync", SettingCategory.Display, "Vertical sync", true) with { Keywords = "vsync tearing" },
        SettingDef.Choice("display.fps_limit", SettingCategory.Display, "Frame rate limit", "60",
            C("30", "30"), C("60", "60"), C("120", "120"), C("0", "Unlimited")) with { Keywords = "fps framerate" },
        SettingDef.Slider("display.ui_scale", SettingCategory.Display, "Interface scale", 1, 0.75, 1.5, 0.05) with { Keywords = "hud size" },

        SettingDef.Choice("graphics.quality", SettingCategory.Graphics, "Quality preset", "high",
            C("low", "Low"), C("medium", "Medium"), C("high", "High"), C("ultra", "Ultra")) with { Keywords = "detail performance" },
        SettingDef.Slider("graphics.view_distance", SettingCategory.Graphics, "View distance", 70, 10, 100, 5) with { Keywords = "draw distance" },
        SettingDef.Choice("graphics.shadows", SettingCategory.Graphics, "Shadow quality", "medium",
            C("off", "Off"), C("low", "Low"), C("medium", "Medium"), C("high", "High")),
        SettingDef.Toggle("graphics.anti_aliasing", SettingCategory.Graphics, "Anti-aliasing", true) with { Keywords = "smoothing edges" },

        SettingDef.Slider("audio.master", SettingCategory.Audio, "Master volume", 80, 0, 100, 1) with { Keywords = "sound loudness" },
        SettingDef.Slider("audio.music", SettingCategory.Audio, "Music volume", 70, 0, 100, 1),
        SettingDef.Slider("audio.effects", SettingCategory.Audio, "Effects volume", 80, 0, 100, 1) with { Keywords = "sfx" },
        SettingDef.Slider("audio.voice", SettingCategory.Audio, "Voice volume", 90, 0, 100, 1) with { Keywords = "dialogue speech" },
        SettingDef.Toggle("audio.mute_unfocused", SettingCategory.Audio, "Mute when unfocused", true) with { Keywords = "background" },

        SettingDef.Slider("controls.mouse_sensitivity", SettingCategory.Controls, "Mouse sensitivity", 50, 1, 100, 1) with { Keywords = "look aim" },
        SettingDef.Toggle("controls.invert_y", SettingCategory.Controls, "Invert vertical look", false) with { Keywords = "mouse stick" },
        SettingDef.Slider("controls.stick_deadzone", SettingCategory.Controls, "Stick dead zone", 10, 0, 40, 1) with { Keywords = "controller gamepad drift" },
        SettingDef.Toggle("controls.vibration", SettingCategory.Controls, "Controller vibration", true) with { Keywords = "rumble haptic gamepad" },

        SettingDef.Choice("gameplay.difficulty", SettingCategory.Gameplay, "Difficulty", "normal",
            C("easy", "Easy"), C("normal", "Normal"), C("hard", "Hard"), C("survival", "Survival")),
        SettingDef.Toggle("gameplay.autosave", SettingCategory.Gameplay, "Autosave", true) with { Keywords = "save" },
        SettingDef.Choice("gameplay.autosave_interval", SettingCategory.Gameplay, "Autosave interval", "10",
            C("5", "5 minutes"), C("10", "10 minutes"), C("20", "20 minutes")) with { Keywords = "save timer" },
        SettingDef.Toggle("gameplay.hints", SettingCategory.Gameplay, "Show tutorial hints", true),

        SettingDef.Toggle("access.subtitles", SettingCategory.Accessibility, "Subtitles", true) with { Keywords = "captions dialogue" },
        SettingDef.Choice("access.subtitle_size", SettingCategory.Accessibility, "Subtitle size", "medium",
            C("small", "Small"), C("medium", "Medium"), C("large", "Large")) with { Keywords = "captions text" },
        SettingDef.Slider("access.text_scale", SettingCategory.Accessibility, "Text size", 1, 0.8, 1.6, 0.1) with { Keywords = "font readable" },
        SettingDef.Toggle("access.high_contrast", SettingCategory.Accessibility, "High contrast", false) with { Keywords = "colour color vision" },
        SettingDef.Toggle("access.reduce_motion", SettingCategory.Accessibility, "Reduce motion", false) with { Keywords = "camera shake animation" },
    };
}
