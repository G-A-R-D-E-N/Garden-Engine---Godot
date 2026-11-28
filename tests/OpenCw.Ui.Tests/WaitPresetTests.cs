using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WaitPresetTests
{
    static GameClock At(int day, int hour, int minute = 0) => new(day, hour * 60 + minute);

    [Fact]
    public void A_fixed_preset_keeps_its_label_and_hours_and_has_no_target_hour()
    {
        var preset = WaitPreset.ForHours("Two", 2);

        Assert.Equal("Two", preset.Label);
        Assert.Equal(2, preset.Hours);
        Assert.Null(preset.UntilHour);
    }

    [Fact]
    public void An_until_preset_keeps_its_target_hour_and_has_zero_fixed_hours()
    {
        var preset = WaitPreset.Until("Noon", 12);

        Assert.Equal("Noon", preset.Label);
        Assert.Equal(0, preset.Hours);
        Assert.Equal(12, preset.UntilHour);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_fixed_preset_needs_at_least_one_hour(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaitPreset.ForHours("Bad", hours));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void An_until_preset_needs_an_hour_of_day_from_zero_to_twenty_three(int hour)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaitPreset.Until("Bad", hour));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public void The_first_and_last_hours_of_the_day_are_valid_until_targets(int hour)
    {
        Assert.Equal(hour, WaitPreset.Until("Edge", hour).UntilHour);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_preset_needs_a_visible_label(string label)
    {
        Assert.Throws<ArgumentException>(() => WaitPreset.ForHours(label, 1));
        Assert.Throws<ArgumentException>(() => WaitPreset.Until(label, 1));
        Assert.Throws<ArgumentNullException>(() => WaitPreset.ForHours(null!, 1));
    }

    [Fact]
    public void The_default_presets_are_four_fixed_waits_then_morning_and_evening()
    {
        var presets = WaitOptions.DefaultPresets;

        Assert.Equal(
            new[] { "1 hour", "3 hours", "6 hours", "12 hours", "Until morning", "Until evening" },
            presets.Select(p => p.Label));
        Assert.Equal(new[] { 1, 3, 6, 12, 0, 0 }, presets.Select(p => p.Hours));
        Assert.Equal(new int?[] { null, null, null, null, 6, 18 }, presets.Select(p => p.UntilHour));
    }

    [Fact]
    public void Default_options_are_one_to_twenty_four_hours_in_steps_of_one_starting_at_one()
    {
        var options = new WaitOptions();

        Assert.Equal(1, options.MinHours);
        Assert.Equal(24, options.MaxHours);
        Assert.Equal(1, options.Step);
        Assert.Equal(1, options.DefaultHours);
        Assert.Empty(options.Blocks);
        Assert.Same(WaitOptions.DefaultPresets, options.Presets);
    }

    [Fact]
    public void Until_presets_count_whole_hours_from_the_current_minute_with_a_full_day_for_the_same_hour()
    {
        var model = new WaitDurationModel(At(0, 17, 30));
        int morning = model.Presets.ToList().FindIndex(p => p.Label == "Until morning");
        int evening = model.Presets.ToList().FindIndex(p => p.Label == "Until evening");

        Assert.Equal(13, model.ResolvePreset(morning));
        Assert.Equal(1, model.ResolvePreset(evening));

        model.UpdateNow(At(0, 18));
        Assert.Equal(24, model.ResolvePreset(evening));

        model.UpdateNow(At(0, 18, 1));
        Assert.Equal(24, model.ResolvePreset(evening));

        model.UpdateNow(At(0, 19));
        Assert.Equal(23, model.ResolvePreset(evening));
    }

    [Fact]
    public void Applying_the_current_value_again_reports_no_change()
    {
        var model = new WaitDurationModel(At(0, 0));
        int three = model.Presets.ToList().FindIndex(p => p.Label == "3 hours");

        Assert.True(model.ApplyPreset(three));
        Assert.False(model.ApplyPreset(three));
        Assert.Equal(3, model.Hours);
    }

    [Fact]
    public void Set_to_minimum_snaps_to_the_lowest_value_and_reports_whether_it_moved()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait,
            new WaitOptions { MinHours = 2, MaxHours = 24, Step = 5, DefaultHours = 22 });

        Assert.Equal(22, model.Hours);
        Assert.Equal(2, model.MinHours);
        Assert.Equal(5, model.Step);

        Assert.True(model.SetToMinimum());
        Assert.Equal(2, model.Hours);
        Assert.False(model.SetToMinimum());
        Assert.Equal(At(0, 2), model.ResultTime);
    }

    [Fact]
    public void Adjust_by_a_negative_step_count_moves_down_the_grid()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait,
            new WaitOptions { MinHours = 2, MaxHours = 24, Step = 5, DefaultHours = 22 });

        Assert.True(model.Adjust(-1));
        Assert.Equal(17, model.Hours);
        Assert.True(model.Adjust(-2));
        Assert.Equal(7, model.Hours);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    [InlineData(22, 21)]
    [InlineData(24, 21)]
    public void A_value_halfway_between_two_grid_points_rounds_up_and_the_top_is_the_last_grid_point(int requested, int expected)
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait,
            new WaitOptions { MinHours = 1, MaxHours = 24, Step = 4, DefaultHours = 13 });

        model.SetHours(requested);

        Assert.Equal(expected, model.Hours);
        Assert.Equal(21, model.MaxHours);
    }

    [Fact]
    public void A_wait_request_compares_by_mode_and_hours()
    {
        Assert.Equal(new WaitRequest(WaitMode.Wait, 4), new WaitRequest(WaitMode.Wait, 4));
        Assert.NotEqual(new WaitRequest(WaitMode.Wait, 4), new WaitRequest(WaitMode.Sleep, 4));
        Assert.NotEqual(new WaitRequest(WaitMode.Wait, 4), new WaitRequest(WaitMode.Wait, 5));
    }
}
