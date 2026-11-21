using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsUnconnectedTests
{
    static SettingDef Def(string id) => DefaultSettingDefs.All.Single(d => d.Id == id);

    [Theory]
    [InlineData("gameplay.difficulty", "damage you take")]
    [InlineData("gameplay.hints", "guidance messages")]
    public void Difficulty_and_hints_describe_what_they_now_do(string id, string text)
    {
        Assert.Contains(text, Def(id).Description);
    }

    [Fact]
    public void The_autosave_interval_explains_when_autosave_actually_runs()
    {
        Assert.Contains("while you play", Def("gameplay.autosave_interval").Description);
    }

    [Fact]
    public void Settings_that_are_applied_carry_no_unconnected_note()
    {
        foreach (var id in new[] { "gameplay.autosave", "display.vsync", "audio.master", "controls.invert_y", "access.subtitles", "graphics.view_distance", "controls.vibration", "gameplay.difficulty", "gameplay.hints" })
            Assert.DoesNotContain("Not connected", Def(id).Description);
    }

    [Fact]
    public void The_autosave_toggle_maps_to_an_effect_the_applier_consumes()
    {
        var effect = Assert.IsType<SetAutosave>(Assert.Single(SettingsEffects.Map("gameplay.autosave", SettingValue.Toggle(false))));
        Assert.False(effect.Enabled);
    }
}
