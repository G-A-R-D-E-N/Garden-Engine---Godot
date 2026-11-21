using System;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingDefTests
{
    static SettingDef Volume() => SettingDef.Slider("s", SettingCategory.Audio, "S", 50, 0, 100, 5);

    static double Normalized(SettingDef def, double number)
    {
        Assert.True(def.TryNormalize(SettingValue.Slider(number), out var normalized));
        Assert.Equal(SettingKind.Slider, normalized.Kind);
        return normalized.Number;
    }

    static SettingsModel Make(SettingDef def) => new(new[] { def }, new InMemoryFileStore());

    [Fact]
    public void Slider_values_snap_to_the_step_and_midpoints_round_away_from_zero()
    {
        var def = Volume();

        Assert.Equal(45, Normalized(def, 47));
        Assert.Equal(50, Normalized(def, 47.5));
        Assert.Equal(50, Normalized(def, 50));
    }

    [Fact]
    public void Slider_values_clamp_to_the_range()
    {
        var def = Volume();

        Assert.Equal(100, Normalized(def, 150));
        Assert.Equal(0, Normalized(def, -3));
    }

    [Fact]
    public void A_step_that_rounds_past_the_maximum_is_clamped_back_to_it()
    {
        var def = SettingDef.Slider("s", SettingCategory.Audio, "S", 0, 0, 10, 4);

        Assert.Equal(10, Normalized(def, 10));
        Assert.Equal(8, Normalized(def, 9));
    }

    [Fact]
    public void Steps_count_from_the_minimum_not_from_zero()
    {
        var def = SettingDef.Slider("s", SettingCategory.Display, "S", 1, 0.75, 1.5, 0.05);

        Assert.Equal(1.1, Normalized(def, 1.12), 9);
        Assert.Equal(0.75, Normalized(def, 0.76), 9);
    }

    [Fact]
    public void A_slider_without_a_step_is_only_rounded_to_six_places()
    {
        var def = SettingDef.Slider("s", SettingCategory.Audio, "S", 0, 0, 1, 0);

        Assert.Equal(0.123457, Normalized(def, 0.1234567891), 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_slider_value_is_refused(double number)
    {
        Assert.False(Volume().TryNormalize(SettingValue.Slider(number), out _));
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused_and_returned_unchanged()
    {
        var offered = SettingValue.Toggle(true);

        Assert.False(Volume().TryNormalize(offered, out var normalized));
        Assert.Equal(offered, normalized);
    }

    [Fact]
    public void A_choice_must_be_one_of_the_listed_ids()
    {
        var def = SettingDef.Choice("c", SettingCategory.Audio, "C", "a", new SettingChoice("a", "A"), new SettingChoice("b", "B"));

        Assert.True(def.TryNormalize(SettingValue.Choice("b"), out var normalized));
        Assert.Equal(SettingValue.Choice("b"), normalized);
        Assert.False(def.TryNormalize(SettingValue.Choice("zzz"), out _));
        Assert.False(def.TryNormalize(SettingValue.Choice("B"), out _));
        Assert.False(def.TryNormalize(new SettingValue(SettingKind.Choice, false, 0, null!), out _));
    }

    [Fact]
    public void A_toggle_drops_any_stray_number_or_text()
    {
        var def = SettingDef.Toggle("t", SettingCategory.Audio, "T", false);

        Assert.True(def.TryNormalize(new SettingValue(SettingKind.Toggle, true, 99, "x"), out var normalized));
        Assert.Equal(SettingValue.Toggle(true), normalized);
    }

    [Fact]
    public void A_keybinding_value_keeps_its_code_and_turns_null_into_empty()
    {
        var def = new SettingDef("k", SettingCategory.Controls, "K", SettingKind.Keybinding, SettingValue.Binding("E"));

        Assert.True(def.TryNormalize(SettingValue.Binding("Q"), out var normalized));
        Assert.Equal(SettingValue.Binding("Q"), normalized);
        Assert.True(def.TryNormalize(new SettingValue(SettingKind.Keybinding, false, 0, null!), out normalized));
        Assert.Equal(SettingValue.Binding(""), normalized);
    }

    [Fact]
    public void Search_text_joins_label_keywords_description_and_category()
    {
        var plain = SettingDef.Toggle("display.vsync", SettingCategory.Display, "Vertical sync", true);
        var full = plain with { Keywords = "vsync tearing", Description = "Stops tearing" };

        Assert.Equal("Vertical sync   Display", plain.SearchText);
        Assert.Equal("Vertical sync vsync tearing Stops tearing Display", full.SearchText);
    }

    [Fact]
    public void Factories_set_kind_default_and_range()
    {
        var toggle = SettingDef.Toggle("t", SettingCategory.Gameplay, "T", true);
        var choice = SettingDef.Choice("c", SettingCategory.Gameplay, "C", "b", new SettingChoice("a", "A"), new SettingChoice("b", "B"));
        var slider = SettingDef.Slider("s", SettingCategory.Gameplay, "S", 3, 1, 9, 2);

        Assert.Equal(SettingKind.Toggle, toggle.Kind);
        Assert.Equal(SettingValue.Toggle(true), toggle.Default);
        Assert.Equal(SettingKind.Choice, choice.Kind);
        Assert.Equal(SettingValue.Choice("b"), choice.Default);
        Assert.Equal(new[] { "a", "b" }, new[] { choice.Choices[0].Id, choice.Choices[1].Id });
        Assert.Equal(SettingKind.Slider, slider.Kind);
        Assert.Equal(SettingValue.Slider(3), slider.Default);
        Assert.Equal((1d, 9d, 2d), (slider.Min, slider.Max, slider.Step));
        Assert.False(toggle.Risky);
        Assert.Empty(toggle.Choices);
    }

    [Fact]
    public void Value_factories_fill_only_the_field_of_their_kind()
    {
        Assert.Equal(new SettingValue(SettingKind.Toggle, true, 0, ""), SettingValue.Toggle(true));
        Assert.Equal(new SettingValue(SettingKind.Choice, false, 0, "id"), SettingValue.Choice("id"));
        Assert.Equal(new SettingValue(SettingKind.Slider, false, 2.5, ""), SettingValue.Slider(2.5));
        Assert.Equal(new SettingValue(SettingKind.Keybinding, false, 0, "E"), SettingValue.Binding("E"));
    }

    [Fact]
    public void A_model_refuses_a_definition_that_cannot_work()
    {
        var emptyId = SettingDef.Toggle(" ", SettingCategory.Audio, "T", true);
        var flatSlider = SettingDef.Slider("s", SettingCategory.Audio, "S", 5, 5, 5, 1);
        var noChoices = SettingDef.Choice("c", SettingCategory.Audio, "C", "a");
        var offStepDefault = SettingDef.Slider("o", SettingCategory.Audio, "O", 47, 0, 100, 5);

        Assert.Equal("Setting id is empty.", Assert.Throws<ArgumentException>(() => Make(emptyId)).Message);
        Assert.Equal("Slider s needs Max > Min.", Assert.Throws<ArgumentException>(() => Make(flatSlider)).Message);
        Assert.Equal("Choice c has no choices.", Assert.Throws<ArgumentException>(() => Make(noChoices)).Message);
        Assert.Equal("Default of o is not a valid value for it.", Assert.Throws<ArgumentException>(() => Make(offStepDefault)).Message);
    }

    [Fact]
    public void Bindable_action_defaults_are_read_per_device_and_slot()
    {
        var action = new BindableAction("a", "A", "Gameplay", "W", "Up", "Pad A", "Pad B");
        var bare = new BindableAction("b", "B", "Gameplay");

        Assert.Equal("W", action.DefaultFor(InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal("Up", action.DefaultFor(InputDevice.Keyboard, BindingSlot.Secondary));
        Assert.Equal("Pad A", action.DefaultFor(InputDevice.Controller, BindingSlot.Primary));
        Assert.Equal("Pad B", action.DefaultFor(InputDevice.Controller, BindingSlot.Secondary));
        foreach (var device in Enum.GetValues<InputDevice>())
            foreach (var slot in Enum.GetValues<BindingSlot>())
                Assert.Null(bare.DefaultFor(device, slot));
    }
}
