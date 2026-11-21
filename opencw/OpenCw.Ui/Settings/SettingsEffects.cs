using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Settings;

public enum WindowModeKind
{
    Windowed,
    Borderless,
    Fullscreen,
}

public enum ShadowLevel
{
    Off,
    Low,
    Medium,
    High,
}

public abstract record SettingEffect;

public sealed record SetWindowMode(WindowModeKind Mode) : SettingEffect;

public sealed record SetVSync(bool Enabled) : SettingEffect;

public sealed record SetFpsLimit(int Fps) : SettingEffect;

public sealed record SetRenderScale(double Scale) : SettingEffect;

public sealed record SetUiScale(double Scale) : SettingEffect;

public sealed record SetMeshDetail(double LodThreshold) : SettingEffect;

public sealed record SetViewDistance(double Fraction) : SettingEffect;

public sealed record SetShadowQuality(ShadowLevel Level) : SettingEffect;

public sealed record SetMsaa(int Samples) : SettingEffect;

public sealed record SetMasterVolume(double Linear) : SettingEffect;

public sealed record SetBusVolume(string Bus, double Linear) : SettingEffect;

public sealed record SetMuteWhenUnfocused(bool Enabled) : SettingEffect;

// Scale is relative to the default sensitivity (1 = default).
public sealed record SetMouseSensitivity(double Scale) : SettingEffect;

public sealed record SetInvertY(bool Enabled) : SettingEffect;

// Scale is relative to the default dead zone (1 = default).
public sealed record SetStickDeadzone(double Scale) : SettingEffect;

public sealed record SetVibration(bool Enabled) : SettingEffect;

public sealed record SetDifficulty(string Level) : SettingEffect;

public sealed record SetAutosave(bool Enabled) : SettingEffect;

public sealed record SetAutosaveInterval(int Minutes) : SettingEffect;

public sealed record SetTutorialHints(bool Enabled) : SettingEffect;

public sealed record SetSubtitles(bool Enabled) : SettingEffect;

public sealed record SetSubtitleSize(string Size) : SettingEffect;

public sealed record SetHudScale(double Scale) : SettingEffect;

public sealed record SetColourBlindPalette(bool Enabled) : SettingEffect;

public sealed record SetReduceMotion(bool Enabled) : SettingEffect;

public static class SettingsEffects
{
    public const string MasterBus = "Master";
    public const string MusicBus = "Music";
    public const string EffectsBus = "Effects";
    public const string VoiceBus = "Voice";
    public const double SilenceDb = -80;
    public const double MouseSensitivityBaseline = 50;
    public const double StickDeadzoneBaseline = 10;
    public const int MsaaOnSamples = 4;
    public const double ReducedMotionFlashFactor = 0.35;
    public const double ReducedMotionKickFactor = 0.25;

    private static readonly IReadOnlyList<SettingEffect> None = Array.Empty<SettingEffect>();

    public static IReadOnlyList<SettingEffect> Map(string id, SettingValue value)
    {
        SettingEffect? effect = id switch
        {
            "display.mode" => Choice(value, WindowMode),
            "display.render_scale" => Slider(value, v => new SetRenderScale(Math.Clamp(v / 100, 0.25, 1))),
            "display.vsync" => Toggle(value, on => new SetVSync(on)),
            "display.fps_limit" => Choice(value, FpsLimit),
            "display.ui_scale" => Slider(value, v => new SetUiScale(Math.Clamp(v, 0.5, 2))),

            "graphics.quality" => Choice(value, MeshDetail),
            "graphics.view_distance" => Slider(value, v => new SetViewDistance(Percent(v))),
            "graphics.shadows" => Choice(value, Shadows),
            "graphics.anti_aliasing" => Toggle(value, on => new SetMsaa(on ? MsaaOnSamples : 0)),

            "audio.master" => Slider(value, v => new SetMasterVolume(Percent(v))),
            "audio.music" => Slider(value, v => new SetBusVolume(MusicBus, Percent(v))),
            "audio.effects" => Slider(value, v => new SetBusVolume(EffectsBus, Percent(v))),
            "audio.voice" => Slider(value, v => new SetBusVolume(VoiceBus, Percent(v))),
            "audio.mute_unfocused" => Toggle(value, on => new SetMuteWhenUnfocused(on)),

            "controls.mouse_sensitivity" => Slider(value, v => new SetMouseSensitivity(v / MouseSensitivityBaseline)),
            "controls.invert_y" => Toggle(value, on => new SetInvertY(on)),
            "controls.stick_deadzone" => Slider(value, v => new SetStickDeadzone(v / StickDeadzoneBaseline)),
            "controls.vibration" => Toggle(value, on => new SetVibration(on)),

            "gameplay.difficulty" => Choice(value, level => new SetDifficulty(level)),
            "gameplay.autosave" => Toggle(value, on => new SetAutosave(on)),
            "gameplay.autosave_interval" => Choice(value, Interval),
            "gameplay.hints" => Toggle(value, on => new SetTutorialHints(on)),

            "access.subtitles" => Toggle(value, on => new SetSubtitles(on)),
            "access.subtitle_size" => Choice(value, size => new SetSubtitleSize(size)),
            "access.text_scale" => Slider(value, v => new SetHudScale(v)),
            "access.high_contrast" => Toggle(value, on => new SetColourBlindPalette(on)),
            "access.reduce_motion" => Toggle(value, on => new SetReduceMotion(on)),
            _ => null,
        };
        return effect is null ? None : new[] { effect };
    }

    public static IReadOnlyList<string> StartupIds(IEnumerable<SettingDef> defs, Func<string, SettingValue> applied)
    {
        var ids = new List<string>();
        foreach (var def in defs)
            if (applied(def.Id) != def.Default || def.Id.StartsWith("audio.", StringComparison.Ordinal)) ids.Add(def.Id);
        return ids;
    }

    public const double DefaultViewFraction = 0.7;
    public const int DefaultStreamRadius = 3;

    public static int StreamRadius(double fraction) =>
        double.IsFinite(fraction) ? Math.Clamp((int)Math.Round(fraction / DefaultViewFraction * DefaultStreamRadius), 1, DefaultStreamRadius) : DefaultStreamRadius;

    public static double LinearToDb(double linear) =>
        linear > 0 ? Math.Max(SilenceDb, 20 * Math.Log10(linear)) : SilenceDb;

    public static double PitchSign(bool invertY) => invertY ? -1 : 1;

    public static double FlashAlphaScale(bool reduceMotion) => reduceMotion ? ReducedMotionFlashFactor : 1;

    public static double KickScale(bool reduceMotion) => reduceMotion ? ReducedMotionKickFactor : 1;

    public static double SubtitleSizeFactor(string? size) => size switch
    {
        "small" => 0.8,
        "large" => 1.35,
        _ => 1,
    };

    private static double Percent(double value) => Math.Clamp(value / 100, 0, 1);

    private static SettingEffect? Toggle(SettingValue value, Func<bool, SettingEffect> make) =>
        value.Kind == SettingKind.Toggle ? make(value.Flag) : null;

    private static SettingEffect? Slider(SettingValue value, Func<double, SettingEffect> make) =>
        value.Kind == SettingKind.Slider && double.IsFinite(value.Number) ? make(value.Number) : null;

    private static SettingEffect? Choice(SettingValue value, Func<string, SettingEffect?> make) =>
        value.Kind == SettingKind.Choice && !string.IsNullOrEmpty(value.Text) ? make(value.Text) : null;

    private static SettingEffect? WindowMode(string choice) => choice switch
    {
        "windowed" => new SetWindowMode(WindowModeKind.Windowed),
        "borderless" => new SetWindowMode(WindowModeKind.Borderless),
        "fullscreen" => new SetWindowMode(WindowModeKind.Fullscreen),
        _ => null,
    };

    private static SettingEffect? FpsLimit(string choice) =>
        int.TryParse(choice, NumberStyles.None, CultureInfo.InvariantCulture, out var fps) ? new SetFpsLimit(fps) : null;

    private static SettingEffect? Interval(string choice) =>
        int.TryParse(choice, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? new SetAutosaveInterval(minutes)
            : null;

    // Godot treats a higher mesh LOD threshold as less detail; 1 is the engine default.
    private static SettingEffect? MeshDetail(string choice) => choice switch
    {
        "low" => new SetMeshDetail(4),
        "medium" => new SetMeshDetail(2),
        "high" => new SetMeshDetail(1),
        "ultra" => new SetMeshDetail(0.5),
        _ => null,
    };

    private static SettingEffect? Shadows(string choice) => choice switch
    {
        "off" => new SetShadowQuality(ShadowLevel.Off),
        "low" => new SetShadowQuality(ShadowLevel.Low),
        "medium" => new SetShadowQuality(ShadowLevel.Medium),
        "high" => new SetShadowQuality(ShadowLevel.High),
        _ => null,
    };
}
