using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SharedSettingsTests
{
    [Fact]
    public void Every_shared_setting_exists_in_the_pause_menu_definitions()
    {
        var ids = DefaultSettingDefs.All.Select(d => d.Id).ToHashSet();
        foreach (var id in SharedSettings.Ids)
        {
            Assert.Contains(id, ids);
            Assert.True(SharedSettings.IsShared(id));
        }
        Assert.False(SharedSettings.IsShared("audio.music"));
    }

    [Theory]
    [InlineData("display.mode", 0, "windowed")]
    [InlineData("display.mode", 1, "borderless")]
    [InlineData("display.mode", 2, "fullscreen")]
    [InlineData("graphics.shadows", 0, "low")]
    [InlineData("graphics.shadows", 1, "medium")]
    [InlineData("graphics.shadows", 2, "high")]
    [InlineData("graphics.shadows", 3, "high")]
    public void Title_choices_map_to_the_pause_menu_choices_and_back(string id, double title, string choice)
    {
        var value = SharedSettings.ToModel(id, title);
        Assert.Equal(choice, value.Text);
        Assert.Equal(title, SharedSettings.ToTitle(id, value, title));
    }

    [Fact]
    public void Every_choice_the_pause_menu_offers_for_a_shared_setting_survives_the_title_scale()
    {
        foreach (var def in DefaultSettingDefs.All.Where(d => SharedSettings.IsShared(d.Id) && d.Kind == SettingKind.Choice))
        foreach (var choice in def.Choices)
        {
            var title = SharedSettings.ToTitle(def.Id, SettingValue.Choice(choice.Id), 0);
            Assert.Equal(choice.Id, SharedSettings.ToModel(def.Id, title).Text);
        }
    }

    [Fact]
    public void Shadow_quality_has_no_off_choice_because_the_title_scale_cannot_hold_it()
    {
        var shadows = DefaultSettingDefs.All.Single(d => d.Id == "graphics.shadows");

        Assert.Equal(new[] { "low", "medium", "high" }, shadows.Choices.Select(c => c.Id).ToArray());
        Assert.False(shadows.TryNormalize(SettingValue.Choice("off"), out _));
    }

    [Fact]
    public void Title_render_scale_and_master_volume_are_fractions_of_one()
    {
        Assert.Equal(75, SharedSettings.ToModel("display.render_scale", 0.75).Number);
        Assert.Equal(0.75, SharedSettings.ToTitle("display.render_scale", SettingValue.Slider(75), 1));
        Assert.Equal(0, SharedSettings.ToModel("audio.master", 0).Number);
        Assert.Equal(0.4, SharedSettings.ToTitle("audio.master", SettingValue.Slider(40), 1));
    }

    [Fact]
    public void Anti_aliasing_keeps_the_title_technique_while_it_stays_on()
    {
        Assert.True(SharedSettings.ToModel("graphics.anti_aliasing", SharedSettings.TitleSmaa).Flag);
        Assert.Equal(SharedSettings.TitleSmaa, SharedSettings.ToTitle("graphics.anti_aliasing", SettingValue.Toggle(true), SharedSettings.TitleSmaa));
        Assert.Equal(SharedSettings.TitleSmaa, SharedSettings.ToTitle("graphics.anti_aliasing", SettingValue.Toggle(true), 0));
        Assert.Equal(0, SharedSettings.ToTitle("graphics.anti_aliasing", SettingValue.Toggle(false), SharedSettings.TitleSmaa));
    }

    [Fact]
    public void Vsync_and_invert_y_are_toggles()
    {
        Assert.True(SharedSettings.ToModel("display.vsync", 1).Flag);
        Assert.False(SharedSettings.ToModel("display.vsync", 0).Flag);
        Assert.Equal(0, SharedSettings.ToTitle("display.vsync", SettingValue.Toggle(false), 1));
        Assert.Equal(1, SharedSettings.ToTitle("controls.invert_y", SettingValue.Toggle(true), 0));
        Assert.Equal(0, SharedSettings.ToTitle("controls.invert_y", SettingValue.Toggle(false), 1));
    }

    [Fact]
    public void Adopt_sets_applied_and_pending_without_saving_or_marking_dirty()
    {
        var store = new InMemoryFileStore();
        var model = new SettingsModel(DefaultSettingDefs.All, store);

        Assert.True(model.Adopt("display.vsync", SettingValue.Toggle(false)));

        Assert.False(model.AppliedValue("display.vsync").Flag);
        Assert.False(model.Value("display.vsync").Flag);
        Assert.False(model.IsDirty());
        Assert.False(store.Files.ContainsKey(SettingsModel.FileName));
        Assert.False(model.Adopt("no.such.setting", SettingValue.Toggle(true)));
    }

    [Fact]
    public void A_title_value_is_adopted_over_the_pause_menu_default()
    {
        var model = new SettingsModel(DefaultSettingDefs.All, new InMemoryFileStore());
        foreach (var id in SharedSettings.Ids)
            model.Adopt(id, SharedSettings.ToModel(id, id == "audio.master" ? 0 : 1));

        Assert.Equal(0, model.AppliedValue("audio.master").Number);
        Assert.Equal("borderless", model.AppliedValue("display.mode").Text);
    }

    [Fact]
    public void Master_mix_is_audible_by_default()
    {
        var mix = new MasterMix();
        Assert.False(mix.Muted);
        Assert.Equal(0, mix.Db);
    }

    [Fact]
    public void Raising_master_volume_from_zero_clears_the_mute()
    {
        var mix = new MasterMix { Volume = 0 };
        Assert.True(mix.Muted);
        Assert.Equal(SettingsEffects.SilenceDb, mix.Db);

        mix.Volume = 0.5;

        Assert.False(mix.Muted);
        Assert.Equal(SettingsEffects.LinearToDb(0.5), mix.Db);
    }

    [Fact]
    public void Losing_focus_mutes_only_when_mute_unfocused_is_on()
    {
        var mix = new MasterMix { Focused = false };
        Assert.False(mix.Muted);

        mix.MuteUnfocused = true;
        Assert.True(mix.Muted);

        mix.Focused = true;
        Assert.False(mix.Muted);
    }
}
