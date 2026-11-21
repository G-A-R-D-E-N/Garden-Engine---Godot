using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsEffectsTests
{
    static SettingDef Def(string id) => DefaultSettingDefs.All.Single(d => d.Id == id);

    static T Only<T>(string id, SettingValue value) where T : SettingEffect =>
        Assert.IsType<T>(Assert.Single(SettingsEffects.Map(id, value)));

    [Fact]
    public void Every_default_setting_maps_to_at_least_one_effect()
    {
        foreach (var def in DefaultSettingDefs.All)
            Assert.True(SettingsEffects.Map(def.Id, def.Default).Count >= 1, $"{def.Id} has no effect mapping");
    }

    [Fact]
    public void Every_value_a_setting_can_take_maps_to_an_effect()
    {
        foreach (var def in DefaultSettingDefs.All)
        {
            var values = def.Kind switch
            {
                SettingKind.Toggle => new[] { SettingValue.Toggle(false), SettingValue.Toggle(true) },
                SettingKind.Slider => new[] { SettingValue.Slider(def.Min), SettingValue.Slider(def.Max) },
                SettingKind.Choice => def.Choices.Select(c => SettingValue.Choice(c.Id)).ToArray(),
                _ => new SettingValue[0],
            };
            foreach (var value in values)
                Assert.True(SettingsEffects.Map(def.Id, value).Count >= 1, $"{def.Id} has no effect for {value}");
        }
    }

    [Fact]
    public void Unknown_ids_and_mismatched_values_give_no_effect()
    {
        Assert.Empty(SettingsEffects.Map("no.such.setting", SettingValue.Toggle(true)));
        Assert.Empty(SettingsEffects.Map("display.vsync", SettingValue.Slider(1)));
        Assert.Empty(SettingsEffects.Map("audio.master", SettingValue.Slider(double.NaN)));
        Assert.Empty(SettingsEffects.Map("display.mode", SettingValue.Choice("bogus")));
        Assert.Empty(SettingsEffects.Map("graphics.quality", SettingValue.Choice("bogus")));
        Assert.Empty(SettingsEffects.Map("display.fps_limit", SettingValue.Choice("fast")));
    }

    [Fact]
    public void Window_mode_choices_map_one_to_one()
    {
        Assert.Equal(WindowModeKind.Windowed, Only<SetWindowMode>("display.mode", SettingValue.Choice("windowed")).Mode);
        Assert.Equal(WindowModeKind.Borderless, Only<SetWindowMode>("display.mode", SettingValue.Choice("borderless")).Mode);
        Assert.Equal(WindowModeKind.Fullscreen, Only<SetWindowMode>("display.mode", SettingValue.Choice("fullscreen")).Mode);
    }

    [Fact]
    public void Toggles_carry_their_flag()
    {
        Assert.Equal(new SetVSync(true), Only<SetVSync>("display.vsync", SettingValue.Toggle(true)));
        Assert.Equal(new SetVSync(false), Only<SetVSync>("display.vsync", SettingValue.Toggle(false)));
        Assert.Equal(new SetSubtitles(false), Only<SetSubtitles>("access.subtitles", SettingValue.Toggle(false)));
        Assert.Equal(new SetColourBlindPalette(true), Only<SetColourBlindPalette>("access.high_contrast", SettingValue.Toggle(true)));
        Assert.Equal(new SetInvertY(true), Only<SetInvertY>("controls.invert_y", SettingValue.Toggle(true)));
        Assert.Equal(new SetMuteWhenUnfocused(true), Only<SetMuteWhenUnfocused>("audio.mute_unfocused", SettingValue.Toggle(true)));
    }

    [Fact]
    public void Anti_aliasing_toggle_maps_to_msaa_samples()
    {
        Assert.Equal(SettingsEffects.MsaaOnSamples, Only<SetMsaa>("graphics.anti_aliasing", SettingValue.Toggle(true)).Samples);
        Assert.Equal(0, Only<SetMsaa>("graphics.anti_aliasing", SettingValue.Toggle(false)).Samples);
    }

    [Fact]
    public void Frame_rate_limit_parses_the_choice_and_zero_means_unlimited()
    {
        Assert.Equal(120, Only<SetFpsLimit>("display.fps_limit", SettingValue.Choice("120")).Fps);
        Assert.Equal(0, Only<SetFpsLimit>("display.fps_limit", SettingValue.Choice("0")).Fps);
    }

    [Fact]
    public void Volume_sliders_convert_percent_to_zero_to_one()
    {
        Assert.Equal(0.8, Only<SetMasterVolume>("audio.master", SettingValue.Slider(80)).Linear, 6);
        Assert.Equal(0.0, Only<SetMasterVolume>("audio.master", SettingValue.Slider(0)).Linear, 6);
        Assert.Equal(1.0, Only<SetMasterVolume>("audio.master", SettingValue.Slider(100)).Linear, 6);
        Assert.Equal(1.0, Only<SetMasterVolume>("audio.master", SettingValue.Slider(250)).Linear, 6);

        var music = Only<SetBusVolume>("audio.music", SettingValue.Slider(70));
        Assert.Equal(SettingsEffects.MusicBus, music.Bus);
        Assert.Equal(0.7, music.Linear, 6);
        Assert.Equal(SettingsEffects.EffectsBus, Only<SetBusVolume>("audio.effects", SettingValue.Slider(10)).Bus);
        Assert.Equal(SettingsEffects.VoiceBus, Only<SetBusVolume>("audio.voice", SettingValue.Slider(10)).Bus);
    }

    [Fact]
    public void Linear_to_db_is_twenty_log_ten_with_an_audible_floor()
    {
        Assert.Equal(0.0, SettingsEffects.LinearToDb(1), 6);
        Assert.Equal(-6.0206, SettingsEffects.LinearToDb(0.5), 3);
        Assert.Equal(SettingsEffects.SilenceDb, SettingsEffects.LinearToDb(0));
        Assert.Equal(SettingsEffects.SilenceDb, SettingsEffects.LinearToDb(0.00001));
        Assert.Equal(SettingsEffects.SilenceDb, SettingsEffects.LinearToDb(double.NaN));
    }

    [Fact]
    public void Render_and_interface_scale_convert_to_factors()
    {
        Assert.Equal(0.5, Only<SetRenderScale>("display.render_scale", SettingValue.Slider(50)).Scale, 6);
        Assert.Equal(1.0, Only<SetRenderScale>("display.render_scale", SettingValue.Slider(100)).Scale, 6);
        Assert.Equal(1.25, Only<SetUiScale>("display.ui_scale", SettingValue.Slider(1.25)).Scale, 6);
        Assert.Equal(1.4, Only<SetHudScale>("access.text_scale", SettingValue.Slider(1.4)).Scale, 6);
    }

    [Fact]
    public void Mouse_sensitivity_and_dead_zone_are_relative_to_their_defaults()
    {
        Assert.Equal(Def("controls.mouse_sensitivity").Default.Number, SettingsEffects.MouseSensitivityBaseline);
        Assert.Equal(Def("controls.stick_deadzone").Default.Number, SettingsEffects.StickDeadzoneBaseline);

        Assert.Equal(1.0, Only<SetMouseSensitivity>("controls.mouse_sensitivity", Def("controls.mouse_sensitivity").Default).Scale, 6);
        Assert.Equal(2.0, Only<SetMouseSensitivity>("controls.mouse_sensitivity", SettingValue.Slider(100)).Scale, 6);
        Assert.Equal(0.02, Only<SetMouseSensitivity>("controls.mouse_sensitivity", SettingValue.Slider(1)).Scale, 6);
        Assert.Equal(1.0, Only<SetStickDeadzone>("controls.stick_deadzone", Def("controls.stick_deadzone").Default).Scale, 6);
        Assert.Equal(4.0, Only<SetStickDeadzone>("controls.stick_deadzone", SettingValue.Slider(40)).Scale, 6);
        Assert.Equal(0.0, Only<SetStickDeadzone>("controls.stick_deadzone", SettingValue.Slider(0)).Scale, 6);
    }

    [Fact]
    public void Quality_preset_lowers_detail_as_the_threshold_rises()
    {
        var low = Only<SetMeshDetail>("graphics.quality", SettingValue.Choice("low")).LodThreshold;
        var medium = Only<SetMeshDetail>("graphics.quality", SettingValue.Choice("medium")).LodThreshold;
        var high = Only<SetMeshDetail>("graphics.quality", SettingValue.Choice("high")).LodThreshold;
        var ultra = Only<SetMeshDetail>("graphics.quality", SettingValue.Choice("ultra")).LodThreshold;
        Assert.True(low > medium && medium > high && high > ultra);
        Assert.Equal(1.0, high);
    }

    [Fact]
    public void Shadow_choices_map_to_levels_and_view_distance_to_a_fraction()
    {
        Assert.Equal(ShadowLevel.Off, Only<SetShadowQuality>("graphics.shadows", SettingValue.Choice("off")).Level);
        Assert.Equal(ShadowLevel.High, Only<SetShadowQuality>("graphics.shadows", SettingValue.Choice("high")).Level);
        Assert.Equal(0.7, Only<SetViewDistance>("graphics.view_distance", SettingValue.Slider(70)).Fraction, 6);
    }

    static readonly string[] AudioIds = { "audio.master", "audio.music", "audio.effects", "audio.voice", "audio.mute_unfocused" };

    [Fact]
    public void Startup_plan_at_defaults_applies_only_the_audio_settings()
    {
        var model = new SettingsModel(DefaultSettingDefs.All, new InMemoryFileStore());
        Assert.Equal(AudioIds, SettingsEffects.StartupIds(model.Definitions, model.AppliedValue).ToArray());
    }

    [Fact]
    public void Startup_plan_lists_the_saved_non_default_settings_and_the_audio_settings()
    {
        var store = new InMemoryFileStore();
        var first = new SettingsModel(DefaultSettingDefs.All, store);
        Assert.True(first.Set("display.vsync", SettingValue.Toggle(false)));
        first.Apply();

        var second = new SettingsModel(DefaultSettingDefs.All, store);
        Assert.Equal(new[] { "display.vsync" }.Concat(AudioIds), SettingsEffects.StartupIds(second.Definitions, second.AppliedValue).ToArray());
    }

    [Fact]
    public void Gameplay_choices_pass_through()
    {
        Assert.Equal("survival", Only<SetDifficulty>("gameplay.difficulty", SettingValue.Choice("survival")).Level);
        Assert.Equal(20, Only<SetAutosaveInterval>("gameplay.autosave_interval", SettingValue.Choice("20")).Minutes);
        Assert.Equal("large", Only<SetSubtitleSize>("access.subtitle_size", SettingValue.Choice("large")).Size);
    }

    [Fact]
    public void Pitch_sign_flips_only_when_vertical_look_is_inverted()
    {
        Assert.Equal(1.0, SettingsEffects.PitchSign(false), 6);
        Assert.Equal(-1.0, SettingsEffects.PitchSign(true), 6);
        Assert.Equal(new SetInvertY(false), Only<SetInvertY>("controls.invert_y", Def("controls.invert_y").Default));
    }

    [Fact]
    public void Reduce_motion_dims_the_damage_flash_and_off_leaves_it_alone()
    {
        Assert.Equal(1.0, SettingsEffects.FlashAlphaScale(false), 6);
        Assert.Equal(SettingsEffects.ReducedMotionFlashFactor, SettingsEffects.FlashAlphaScale(true), 6);
        Assert.InRange(SettingsEffects.ReducedMotionFlashFactor, 0.01, 0.99);
        Assert.Equal(new SetReduceMotion(true), Only<SetReduceMotion>("access.reduce_motion", SettingValue.Toggle(true)));
    }

    [Fact]
    public void Subtitle_size_grows_with_the_choice_and_medium_is_the_unscaled_default()
    {
        var small = SettingsEffects.SubtitleSizeFactor("small");
        var medium = SettingsEffects.SubtitleSizeFactor("medium");
        var large = SettingsEffects.SubtitleSizeFactor("large");
        Assert.Equal(1.0, medium, 6);
        Assert.True(small < medium && medium < large);
        Assert.Equal(1.0, SettingsEffects.SubtitleSizeFactor("bogus"), 6);
        Assert.Equal(1.0, SettingsEffects.SubtitleSizeFactor(null), 6);
        Assert.Equal(Def("access.subtitle_size").Choices.Count,
            Def("access.subtitle_size").Choices.Select(c => SettingsEffects.SubtitleSizeFactor(c.Id)).Distinct().Count());
    }
}
