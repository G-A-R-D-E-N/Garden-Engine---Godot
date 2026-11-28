using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsEffectsBoundaryTests
{
    static T Only<T>(string id, SettingValue value) where T : SettingEffect =>
        Assert.IsType<T>(Assert.Single(SettingsEffects.Map(id, value)));

    [Fact]
    public void Published_constants_keep_their_values()
    {
        Assert.Equal("Master", SettingsEffects.MasterBus);
        Assert.Equal("Music", SettingsEffects.MusicBus);
        Assert.Equal("Effects", SettingsEffects.EffectsBus);
        Assert.Equal("Voice", SettingsEffects.VoiceBus);
        Assert.Equal(-80.0, SettingsEffects.SilenceDb);
        Assert.Equal(50.0, SettingsEffects.MouseSensitivityBaseline);
        Assert.Equal(10.0, SettingsEffects.StickDeadzoneBaseline);
        Assert.Equal(4, SettingsEffects.MsaaOnSamples);
        Assert.Equal(0.35, SettingsEffects.ReducedMotionFlashFactor);
    }

    [Theory]
    [InlineData(-5, 0.25)]
    [InlineData(10, 0.25)]
    [InlineData(25, 0.25)]
    [InlineData(75, 0.75)]
    [InlineData(100, 1.0)]
    [InlineData(250, 1.0)]
    public void Render_scale_is_percent_clamped_to_a_quarter_through_one(double percent, double expected)
    {
        Assert.Equal(expected, Only<SetRenderScale>("display.render_scale", SettingValue.Slider(percent)).Scale, 6);
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(3.0, 2.0)]
    public void Interface_scale_is_clamped_to_half_through_two(double value, double expected)
    {
        Assert.Equal(expected, Only<SetUiScale>("display.ui_scale", SettingValue.Slider(value)).Scale, 6);
    }

    [Theory]
    [InlineData(-20, 0.0)]
    [InlineData(35, 0.35)]
    [InlineData(150, 1.0)]
    public void View_distance_and_every_volume_clamp_percent_to_zero_through_one(double percent, double expected)
    {
        Assert.Equal(expected, Only<SetViewDistance>("graphics.view_distance", SettingValue.Slider(percent)).Fraction, 6);
        Assert.Equal(expected, Only<SetMasterVolume>("audio.master", SettingValue.Slider(percent)).Linear, 6);
        Assert.Equal(expected, Only<SetBusVolume>("audio.music", SettingValue.Slider(percent)).Linear, 6);
        Assert.Equal(expected, Only<SetBusVolume>("audio.effects", SettingValue.Slider(percent)).Linear, 6);
        Assert.Equal(expected, Only<SetBusVolume>("audio.voice", SettingValue.Slider(percent)).Linear, 6);
    }

    [Fact]
    public void Infinite_slider_values_give_no_effect_for_every_slider()
    {
        foreach (var id in new[]
                 {
                     "display.render_scale", "display.ui_scale", "graphics.view_distance", "audio.master", "audio.music",
                     "audio.effects", "audio.voice", "controls.mouse_sensitivity", "controls.stick_deadzone", "access.text_scale",
                 })
        {
            Assert.Empty(SettingsEffects.Map(id, SettingValue.Slider(double.PositiveInfinity)));
            Assert.Empty(SettingsEffects.Map(id, SettingValue.Slider(double.NegativeInfinity)));
            Assert.Empty(SettingsEffects.Map(id, SettingValue.Slider(double.NaN)));
        }
    }

    [Fact]
    public void A_value_of_the_wrong_kind_gives_no_effect()
    {
        Assert.Empty(SettingsEffects.Map("display.mode", SettingValue.Toggle(true)));
        Assert.Empty(SettingsEffects.Map("display.render_scale", SettingValue.Choice("50")));
        Assert.Empty(SettingsEffects.Map("gameplay.autosave", SettingValue.Choice("true")));
        Assert.Empty(SettingsEffects.Map("gameplay.autosave_interval", SettingValue.Slider(10)));
        Assert.Empty(SettingsEffects.Map("access.text_scale", SettingValue.Toggle(true)));
        Assert.Empty(SettingsEffects.Map("controls.vibration", SettingValue.Binding("X")));
    }

    [Fact]
    public void An_empty_choice_gives_no_effect_for_every_choice_setting()
    {
        foreach (var id in new[]
                 {
                     "display.mode", "display.fps_limit", "graphics.quality", "graphics.shadows", "gameplay.difficulty",
                     "gameplay.autosave_interval", "access.subtitle_size",
                 })
            Assert.Empty(SettingsEffects.Map(id, SettingValue.Choice("")));
    }

    [Theory]
    [InlineData("low", 4.0)]
    [InlineData("medium", 2.0)]
    [InlineData("high", 1.0)]
    [InlineData("ultra", 0.5)]
    public void Mesh_detail_presets_map_to_their_lod_thresholds(string choice, double expected)
    {
        Assert.Equal(expected, Only<SetMeshDetail>("graphics.quality", SettingValue.Choice(choice)).LodThreshold);
    }

    [Theory]
    [InlineData("off", ShadowLevel.Off)]
    [InlineData("low", ShadowLevel.Low)]
    [InlineData("medium", ShadowLevel.Medium)]
    [InlineData("high", ShadowLevel.High)]
    public void Shadow_choices_map_one_to_one(string choice, ShadowLevel expected)
    {
        Assert.Equal(expected, Only<SetShadowQuality>("graphics.shadows", SettingValue.Choice(choice)).Level);
    }

    [Fact]
    public void Choice_ids_are_case_sensitive_and_unlisted_ones_give_no_effect()
    {
        Assert.Empty(SettingsEffects.Map("graphics.shadows", SettingValue.Choice("Off")));
        Assert.Empty(SettingsEffects.Map("graphics.shadows", SettingValue.Choice("ultra")));
        Assert.Empty(SettingsEffects.Map("graphics.quality", SettingValue.Choice("Ultra")));
        Assert.Empty(SettingsEffects.Map("graphics.quality", SettingValue.Choice("off")));
        Assert.Empty(SettingsEffects.Map("display.mode", SettingValue.Choice("Windowed")));
    }

    [Theory]
    [InlineData("060", 60)]
    [InlineData("144", 144)]
    public void Frame_rate_limit_accepts_plain_digits(string choice, int expected)
    {
        Assert.Equal(expected, Only<SetFpsLimit>("display.fps_limit", SettingValue.Choice(choice)).Fps);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+60")]
    [InlineData(" 60")]
    [InlineData("60 ")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Frame_rate_limit_rejects_anything_but_plain_digits(string choice)
    {
        Assert.Empty(SettingsEffects.Map("display.fps_limit", SettingValue.Choice(choice)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("abc")]
    [InlineData("5.5")]
    public void Autosave_interval_needs_a_positive_whole_number_of_minutes(string choice)
    {
        Assert.Empty(SettingsEffects.Map("gameplay.autosave_interval", SettingValue.Choice(choice)));
    }

    [Fact]
    public void Autosave_interval_keeps_a_positive_value()
    {
        Assert.Equal(1, Only<SetAutosaveInterval>("gameplay.autosave_interval", SettingValue.Choice("1")).Minutes);
        Assert.Equal(15, Only<SetAutosaveInterval>("gameplay.autosave_interval", SettingValue.Choice("15")).Minutes);
    }

    [Fact]
    public void Remaining_toggles_carry_their_flag()
    {
        Assert.Equal(new SetAutosave(true), Only<SetAutosave>("gameplay.autosave", SettingValue.Toggle(true)));
        Assert.Equal(new SetAutosave(false), Only<SetAutosave>("gameplay.autosave", SettingValue.Toggle(false)));
        Assert.Equal(new SetTutorialHints(true), Only<SetTutorialHints>("gameplay.hints", SettingValue.Toggle(true)));
        Assert.Equal(new SetTutorialHints(false), Only<SetTutorialHints>("gameplay.hints", SettingValue.Toggle(false)));
        Assert.Equal(new SetVibration(true), Only<SetVibration>("controls.vibration", SettingValue.Toggle(true)));
        Assert.Equal(new SetVibration(false), Only<SetVibration>("controls.vibration", SettingValue.Toggle(false)));
        Assert.Equal(new SetReduceMotion(false), Only<SetReduceMotion>("access.reduce_motion", SettingValue.Toggle(false)));
        Assert.Equal(new SetMuteWhenUnfocused(false), Only<SetMuteWhenUnfocused>("audio.mute_unfocused", SettingValue.Toggle(false)));
        Assert.Equal(new SetColourBlindPalette(false), Only<SetColourBlindPalette>("access.high_contrast", SettingValue.Toggle(false)));
        Assert.Equal(new SetInvertY(false), Only<SetInvertY>("controls.invert_y", SettingValue.Toggle(false)));
    }

    [Fact]
    public void Subtitle_size_factors_are_exact_and_case_sensitive()
    {
        Assert.Equal(0.8, SettingsEffects.SubtitleSizeFactor("small"));
        Assert.Equal(1.35, SettingsEffects.SubtitleSizeFactor("large"));
        Assert.Equal(1.0, SettingsEffects.SubtitleSizeFactor("medium"));
        Assert.Equal(1.0, SettingsEffects.SubtitleSizeFactor(""));
        Assert.Equal(1.0, SettingsEffects.SubtitleSizeFactor("Large"));
    }

    [Fact]
    public void Linear_to_db_handles_gain_attenuation_and_the_floor_boundary()
    {
        Assert.Equal(20.0, SettingsEffects.LinearToDb(10), 6);
        Assert.Equal(-20.0, SettingsEffects.LinearToDb(0.1), 6);
        Assert.Equal(-40.0, SettingsEffects.LinearToDb(0.01), 6);
        Assert.Equal(-80.0, SettingsEffects.LinearToDb(0.0001), 6);
        Assert.Equal(SettingsEffects.SilenceDb, SettingsEffects.LinearToDb(-1));
        Assert.Equal(SettingsEffects.SilenceDb, SettingsEffects.LinearToDb(double.NegativeInfinity));
    }

    [Fact]
    public void Startup_ids_list_only_the_definitions_whose_applied_value_differs_in_definition_order()
    {
        var defs = new[]
        {
            SettingDef.Toggle("t", SettingCategory.Display, "T", false),
            SettingDef.Slider("s", SettingCategory.Audio, "S", 5, 0, 10, 1),
            SettingDef.Choice("c", SettingCategory.Gameplay, "C", "a", new SettingChoice("a", "A"), new SettingChoice("b", "B")),
        };

        Assert.Empty(SettingsEffects.StartupIds(defs, id => defs.Single(d => d.Id == id).Default));

        Assert.Equal(new[] { "t", "c" }, SettingsEffects.StartupIds(defs, id => id switch
        {
            "t" => SettingValue.Toggle(true),
            "c" => SettingValue.Choice("b"),
            _ => defs.Single(d => d.Id == id).Default,
        }).ToArray());

        Assert.Equal(new[] { "s" }, SettingsEffects.StartupIds(defs, id => id == "s"
            ? SettingValue.Slider(6)
            : defs.Single(d => d.Id == id).Default).ToArray());
    }

    [Fact]
    public void Startup_ids_treat_a_value_of_another_kind_as_changed_and_an_empty_list_as_nothing()
    {
        var defs = new[] { SettingDef.Toggle("t", SettingCategory.Display, "T", false) };

        Assert.Equal(new[] { "t" }, SettingsEffects.StartupIds(defs, _ => SettingValue.Slider(0)).ToArray());
        Assert.Empty(SettingsEffects.StartupIds(new SettingDef[0], _ => SettingValue.Toggle(true)));
    }
}
