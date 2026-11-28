using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsSnapshotTests
{
    static SettingsModel Make(InMemoryFileStore? store = null) => new(DefaultSettingDefs.All, store ?? new InMemoryFileStore());

    static SettingDef Def(string id) => DefaultSettingDefs.All.Single(d => d.Id == id);

    [Fact]
    public void Defaults_match_the_default_setting_definitions()
    {
        var fromDefs = SettingsSnapshot.From(id => Def(id).Default);
        Assert.Equal(new SettingsSnapshot(), fromDefs);
        Assert.Equal(new SettingsSnapshot(), SettingsSnapshot.From(Make().AppliedValue));
    }

    [Fact]
    public void Applied_values_are_reported_and_unapplied_ones_are_not()
    {
        var model = Make();
        Assert.True(model.Set("controls.vibration", SettingValue.Toggle(false)));
        Assert.True(model.Set("gameplay.difficulty", SettingValue.Choice("survival")));
        Assert.True(model.Set("gameplay.autosave", SettingValue.Toggle(false)));
        Assert.True(model.Set("gameplay.autosave_interval", SettingValue.Choice("20")));
        Assert.True(model.Set("gameplay.hints", SettingValue.Toggle(false)));
        Assert.True(model.Set("graphics.view_distance", SettingValue.Slider(40)));
        Assert.Equal(new SettingsSnapshot(), SettingsSnapshot.From(model.AppliedValue));

        model.Apply();

        var snapshot = SettingsSnapshot.From(model.AppliedValue);
        Assert.False(snapshot.Vibration);
        Assert.Equal("survival", snapshot.Difficulty);
        Assert.False(snapshot.Autosave);
        Assert.Equal(20, snapshot.AutosaveMinutes);
        Assert.False(snapshot.TutorialHints);
        Assert.Equal(0.4, snapshot.ViewDistance, 6);
    }

    [Fact]
    public void Saved_values_are_reported_after_a_restart()
    {
        var store = new InMemoryFileStore();
        var first = Make(store);
        Assert.True(first.Set("gameplay.difficulty", SettingValue.Choice("hard")));
        first.Apply();

        Assert.Equal("hard", SettingsSnapshot.From(Make(store).AppliedValue).Difficulty);
    }

    [Fact]
    public void A_value_that_maps_to_no_effect_keeps_the_default()
    {
        var snapshot = SettingsSnapshot.From(id => id == "gameplay.difficulty" ? SettingValue.Choice("") : Def(id).Default);
        Assert.Equal("normal", snapshot.Difficulty);
    }

    [Fact]
    public void A_null_reader_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsSnapshot.From(null!));
    }
}
