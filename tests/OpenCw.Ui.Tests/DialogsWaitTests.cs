using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DialogsWaitTests
{
    static GameClock At(int day, int hour, int minute = 0) => new(day, hour * 60 + minute);

    [Fact]
    public void Result_time_rolls_over_to_the_next_day_and_counts_days_ahead()
    {
        var model = new WaitDurationModel(At(3, 23));
        model.SetHours(2);

        Assert.Equal(At(4, 1), model.ResultTime);
        Assert.Equal(1, model.DaysAhead);
        Assert.Equal("01:00", model.ResultTime.Format24());
    }

    [Fact]
    public void Waiting_a_full_day_lands_on_the_same_clock_time_one_day_later()
    {
        var model = new WaitDurationModel(At(0, 0, 30));
        model.SetToMaximum();

        Assert.Equal(24, model.Hours);
        Assert.Equal(At(1, 0, 30), model.ResultTime);
        Assert.Equal(1, model.DaysAhead);
    }

    [Fact]
    public void Waiting_within_the_same_day_does_not_advance_the_day()
    {
        var model = new WaitDurationModel(At(2, 8));
        model.SetHours(5);

        Assert.Equal(0, model.DaysAhead);
        Assert.Equal(At(2, 13), model.ResultTime);
    }

    [Fact]
    public void Hours_clamp_to_the_range_and_snap_to_the_step_grid()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait,
            new WaitOptions { MinHours = 2, MaxHours = 24, Step = 5, DefaultHours = 2 });

        Assert.Equal(22, model.MaxHours);

        model.SetHours(24);
        Assert.Equal(22, model.Hours);

        model.SetHours(9);
        Assert.Equal(7, model.Hours);

        model.SetHours(10);
        Assert.Equal(12, model.Hours);

        model.SetHours(-50);
        Assert.Equal(2, model.Hours);
    }

    [Fact]
    public void Adjust_moves_by_whole_steps_and_reports_whether_anything_changed()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait,
            new WaitOptions { MinHours = 2, MaxHours = 24, Step = 5, DefaultHours = 2 });

        Assert.False(model.Adjust(-1));
        Assert.Equal(2, model.Hours);

        Assert.True(model.Adjust(1));
        Assert.Equal(7, model.Hours);

        Assert.True(model.Adjust(100));
        Assert.Equal(22, model.Hours);
        Assert.False(model.Adjust(1));
    }

    [Fact]
    public void Default_hours_start_inside_the_range()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { DefaultHours = 99 });

        Assert.Equal(24, model.Hours);
    }

    [Fact]
    public void Preview_names_the_time_of_day_of_the_result()
    {
        var model = new WaitDurationModel(At(0, 4));
        model.SetHours(5);
        Assert.Equal("Morning", model.ResultTimeOfDay);

        model.SetHours(18);
        Assert.Equal("Night", model.ResultTimeOfDay);

        model.SetHours(1);
        Assert.Equal("Dawn", model.ResultTimeOfDay);
    }

    [Fact]
    public void Until_presets_round_up_to_whole_hours_and_pass_the_target()
    {
        var model = new WaitDurationModel(At(0, 22));
        int morning = model.Presets.ToList().FindIndex(p => p.Label == "Until morning");

        Assert.Equal(8, model.ResolvePreset(morning));
        Assert.True(model.ApplyPreset(morning));
        Assert.Equal(At(1, 6), model.ResultTime);

        model.UpdateNow(At(0, 5, 30));
        Assert.Equal(1, model.ResolvePreset(morning));
    }

    [Fact]
    public void An_until_preset_for_the_current_hour_means_the_next_day_and_is_clamped_to_the_maximum()
    {
        var model = new WaitDurationModel(At(0, 6), WaitMode.Wait, new WaitOptions { MaxHours = 12 });
        int morning = model.Presets.ToList().FindIndex(p => p.Label == "Until morning");

        Assert.Equal(12, model.ResolvePreset(morning));
    }

    [Fact]
    public void Fixed_presets_apply_their_hours_and_bad_indexes_are_rejected()
    {
        var model = new WaitDurationModel(At(0, 0));
        int six = model.Presets.ToList().FindIndex(p => p.Label == "6 hours");

        Assert.True(model.ApplyPreset(six));
        Assert.Equal(6, model.Hours);
        Assert.False(model.ApplyPreset(-1));
        Assert.False(model.ApplyPreset(model.Presets.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.ResolvePreset(model.Presets.Count));
    }

    [Fact]
    public void Sleep_is_refused_while_a_blocking_state_is_active_but_waiting_is_not()
    {
        var options = new WaitOptions
        {
            Blocks = new[] { new ModeBlock(WaitMode.Sleep, "combat", "You cannot sleep during combat.") },
        };
        var model = new WaitDurationModel(At(0, 12), WaitMode.Sleep, options);
        model.SetActiveStates(new[] { "combat" });

        Assert.Equal("You cannot sleep during combat.", model.BlockMessage);
        Assert.False(model.CanConfirm);
        Assert.Null(model.Confirm());
        Assert.Equal(DialogOutcome.Pending, model.Outcome);

        model.SetMode(WaitMode.Wait);
        Assert.Null(model.BlockMessage);
        Assert.Equal("You cannot sleep during combat.", model.BlockMessageFor(WaitMode.Sleep));
        Assert.True(model.CanConfirm);
    }

    [Fact]
    public void Clearing_the_blocking_state_unblocks_sleep()
    {
        var options = new WaitOptions
        {
            Blocks = new[] { new ModeBlock(WaitMode.Sleep, "combat", "No sleep in combat.") },
        };
        var model = new WaitDurationModel(At(0, 12), WaitMode.Sleep, options);
        model.SetActiveStates(new[] { "combat" });
        Assert.False(model.CanConfirm);

        model.SetActiveStates(Array.Empty<string>());

        Assert.True(model.CanConfirm);
        Assert.Equal(new WaitRequest(WaitMode.Sleep, 1), model.Confirm());
    }

    [Fact]
    public void Confirm_returns_the_mode_and_hours_once_and_freezes_the_model()
    {
        var model = new WaitDurationModel(At(0, 12), WaitMode.Sleep);
        model.SetHours(8);

        Assert.Equal(new WaitRequest(WaitMode.Sleep, 8), model.Confirm());
        Assert.Equal(DialogOutcome.Confirmed, model.Outcome);

        Assert.False(model.SetHours(3));
        Assert.Equal(8, model.Hours);
        Assert.Null(model.Confirm());
    }

    [Fact]
    public void Cancel_resolves_the_dialog_and_prevents_a_later_confirm()
    {
        var model = new WaitDurationModel(At(0, 12));

        model.Cancel();

        Assert.Equal(DialogOutcome.Cancelled, model.Outcome);
        Assert.Null(model.Confirm());
        Assert.False(model.CanConfirm);
    }

    [Fact]
    public void Bad_options_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { MinHours = 5, MaxHours = 4 }));
        Assert.Throws<ArgumentException>(() =>
            new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { Step = 0 }));
        Assert.Throws<ArgumentException>(() =>
            new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { MinHours = 0 }));
    }

    [Fact]
    public void Clock_adds_minutes_across_midnight_and_rejects_invalid_values()
    {
        Assert.Equal(At(1, 0), At(0, 23, 59).AddMinutes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameClock(0, 1440));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameClock(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => At(0, 0).AddHours(-1));
    }

    [Fact]
    public void Custom_time_bands_are_validated_and_looked_up_by_start_hour()
    {
        var bands = new TimeBands(new[] { new TimeBand("Dark", 0), new TimeBand("Light", 7) });

        Assert.Equal("Dark", bands.NameAt(At(0, 6, 59)));
        Assert.Equal("Light", bands.NameAt(At(0, 7)));
        Assert.Throws<ArgumentException>(() => new TimeBands(new[] { new TimeBand("Late", 3) }));
        Assert.Throws<ArgumentException>(() =>
            new TimeBands(new[] { new TimeBand("A", 0), new TimeBand("B", 5), new TimeBand("C", 5) }));
    }
}
