using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DialogsEdgeTests
{
    static GameClock At(int day, int hour, int minute = 0) => new(day, hour * 60 + minute);

    [Theory]
    [InlineData(6, 5, 30, 1)]
    [InlineData(6, 5, 0, 1)]
    [InlineData(6, 4, 59, 2)]
    [InlineData(6, 22, 0, 8)]
    [InlineData(6, 6, 0, 24)]
    [InlineData(6, 6, 1, 24)]
    [InlineData(0, 23, 59, 1)]
    [InlineData(23, 0, 0, 23)]
    public void Hours_until_a_target_hour_round_up_and_the_same_hour_means_a_whole_day(int target, int hour, int minute, int expected)
    {
        Assert.Equal(expected, At(0, hour, minute).HoursUntil(target));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Hours_until_rejects_a_target_outside_the_day(int target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => At(0, 0).HoursUntil(target));
    }

    [Fact]
    public void The_clock_reads_hour_minute_and_pads_the_twenty_four_hour_text()
    {
        Assert.Equal((13, 7), (At(2, 13, 7).Hour, At(2, 13, 7).Minute));
        Assert.Equal("09:05", At(0, 9, 5).Format24());
        Assert.Equal("00:00", At(0, 0).Format24());
        Assert.Equal("23:59", At(0, 23, 59).Format24());
    }

    [Fact]
    public void Subtracting_minutes_can_cross_back_over_midnight_but_not_below_day_zero()
    {
        Assert.Equal(At(0, 23, 50), At(1, 0, 10).AddMinutes(-20));
        Assert.Equal(At(0, 0), At(0, 0, 10).AddMinutes(-10));
        Assert.Throws<ArgumentOutOfRangeException>(() => At(0, 0, 10).AddMinutes(-11));
    }

    [Fact]
    public void Adding_minutes_past_the_largest_day_overflows()
    {
        var last = new GameClock(int.MaxValue, 1439);

        Assert.Equal(new GameClock(int.MaxValue, 1439), last.AddMinutes(0));
        Assert.Throws<OverflowException>(() => last.AddMinutes(1));
    }

    [Theory]
    [InlineData(0, "Night")]
    [InlineData(4, "Night")]
    [InlineData(5, "Dawn")]
    [InlineData(7, "Dawn")]
    [InlineData(8, "Morning")]
    [InlineData(11, "Morning")]
    [InlineData(12, "Afternoon")]
    [InlineData(16, "Afternoon")]
    [InlineData(17, "Evening")]
    [InlineData(20, "Evening")]
    [InlineData(21, "Night")]
    [InlineData(23, "Night")]
    public void The_default_bands_change_exactly_at_their_start_hours(int hour, string expected)
    {
        Assert.Equal(expected, TimeBands.Default.NameAt(At(0, hour, 59)));
    }

    [Fact]
    public void Bands_must_have_names_ascending_hours_inside_the_day_and_a_midnight_start()
    {
        Assert.Throws<ArgumentNullException>(() => new TimeBands(null!));
        Assert.Throws<ArgumentException>(() => new TimeBands(Array.Empty<TimeBand>()));
        Assert.Throws<ArgumentException>(() => new TimeBands(new[] { new TimeBand("A", 0), new TimeBand(" ", 5) }));
        Assert.Throws<ArgumentException>(() => new TimeBands(new[] { new TimeBand("A", 0), new TimeBand("B", 24) }));
        Assert.Throws<ArgumentException>(() => new TimeBands(new[] { new TimeBand("A", 0), new TimeBand("B", 9), new TimeBand("C", 4) }));
        Assert.Equal("A", new TimeBands(new[] { new TimeBand("A", 0) }).NameAt(At(0, 23)));
    }

    [Fact]
    public void A_hold_completes_on_the_tick_that_lands_exactly_on_the_hold_time()
    {
        var model = new HoldToConfirmModel(2.0);
        model.Press();

        Assert.False(model.Tick(0.5));
        Assert.Equal(0.25, model.Progress);
        Assert.False(model.Tick(1.0));
        Assert.True(model.Tick(0.5));
        Assert.Equal(HoldState.Completed, model.State);
    }

    [Fact]
    public void An_armed_double_press_resets_exactly_when_the_window_is_used_up()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress, 0.5);
        model.Press();

        model.Tick(0.25);
        Assert.Equal(HoldState.Armed, model.State);
        Assert.Equal(0.5, model.Progress);

        model.Tick(0.25);

        Assert.Equal(HoldState.Idle, model.State);
        Assert.Equal(0.0, model.Progress);
    }

    [Fact]
    public void Releasing_an_armed_double_press_keeps_it_armed_and_reset_cannot_undo_a_completed_one()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress, 1.0);
        model.Press();
        model.Release();
        Assert.Equal(HoldState.Armed, model.State);

        Assert.True(model.Press());
        model.Reset();

        Assert.Equal(HoldState.Completed, model.State);
        Assert.Equal(1.0, model.Progress);
    }

    [Fact]
    public void A_new_hold_model_defaults_to_a_second_and_a_half_in_hold_mode()
    {
        var model = new HoldToConfirmModel();

        Assert.Equal((1.5, 1.5, HoldMode.Hold, HoldState.Idle), (model.HoldSeconds, model.DoublePressWindowSeconds, model.Mode, model.State));
    }

    [Fact]
    public void The_typed_phrase_is_trimmed_and_the_case_rule_is_kept()
    {
        var model = new TypedConfirmModel("  Erase  ", CaseRule.Insensitive);

        Assert.Equal(("Erase", CaseRule.Insensitive), (model.Phrase, model.Case));
        Assert.Equal("Type \"Erase\" to continue.", model.Prompt.Validity.Message);
    }

    [Fact]
    public void A_typed_confirmation_confirms_once_and_a_second_confirm_does_nothing()
    {
        var model = new TypedConfirmModel("erase");
        model.Prompt.Insert("erase");

        Assert.True(model.Confirm());
        Assert.False(model.Confirm());
        Assert.False(model.CanConfirm);
        Assert.Equal(DialogOutcome.Confirmed, model.Outcome);
    }

    [Fact]
    public void A_phrase_with_repeated_inner_spaces_matches_when_typed_with_single_spaces()
    {
        var model = new TypedConfirmModel("delete   world");
        model.Prompt.Insert("delete world");

        Assert.True(model.Matches);
    }

    [Fact]
    public void A_decomposed_accent_typed_matches_a_composed_phrase()
    {
        var model = new TypedConfirmModel("café");
        model.Prompt.Insert("café");

        Assert.True(model.Confirm());
    }

    [Fact]
    public void Adjusting_by_an_enormous_number_of_steps_lands_on_the_last_grid_point_either_way()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { Step = 5 });

        Assert.True(model.Adjust(int.MaxValue));
        Assert.Equal(21, model.Hours);
        Assert.Equal(21, model.MaxHours);
        Assert.True(model.Adjust(int.MinValue));
        Assert.Equal(1, model.Hours);
    }

    [Fact]
    public void A_default_below_the_minimum_starts_at_the_minimum()
    {
        var model = new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { MinHours = 3, MaxHours = 12, DefaultHours = 1 });

        Assert.Equal(3, model.Hours);
    }

    [Fact]
    public void After_a_confirm_or_cancel_the_mode_and_the_clock_are_frozen()
    {
        var confirmed = new WaitDurationModel(At(1, 8));
        confirmed.Confirm();
        confirmed.SetMode(WaitMode.Sleep);
        confirmed.UpdateNow(At(5, 0));
        var cancelled = new WaitDurationModel(At(1, 8));
        cancelled.Cancel();
        cancelled.SetMode(WaitMode.Sleep);
        cancelled.UpdateNow(At(5, 0));

        Assert.Equal((WaitMode.Wait, At(1, 8)), (confirmed.Mode, confirmed.Now));
        Assert.Equal((WaitMode.Wait, At(1, 8)), (cancelled.Mode, cancelled.Now));
    }

    [Fact]
    public void Missing_preset_block_or_band_lists_are_rejected_and_a_bad_preset_index_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { Presets = null! }));
        Assert.Throws<ArgumentNullException>(() => new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { Blocks = null! }));
        Assert.Throws<ArgumentNullException>(() => new WaitDurationModel(At(0, 0), WaitMode.Wait, new WaitOptions { Bands = null! }));
        var model = new WaitDurationModel(At(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.ResolvePreset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.ResolvePreset(model.Presets.Count));
        Assert.Throws<ArgumentNullException>(() => model.SetActiveStates(null!));
    }

    [Fact]
    public void The_first_matching_block_decides_the_message_and_inactive_states_do_not_block()
    {
        var blocks = new List<ModeBlock>
        {
            new(WaitMode.Sleep, "combat", "Not in combat."),
            new(WaitMode.Sleep, "combat", "Second."),
            new(WaitMode.Sleep, "hostile", "Not here."),
        };
        var model = new WaitDurationModel(At(0, 0), WaitMode.Sleep, new WaitOptions { Blocks = blocks });
        Assert.True(model.CanConfirm);

        model.SetActiveStates(["hostile", "combat"]);
        Assert.Equal("Not in combat.", model.BlockMessage);
        Assert.Null(model.BlockMessageFor(WaitMode.Wait));
        Assert.Null(model.Confirm());
        Assert.Equal(DialogOutcome.Pending, model.Outcome);

        model.SetActiveStates(["calm"]);
        Assert.Null(model.BlockMessage);
        Assert.Equal(new WaitRequest(WaitMode.Sleep, 1), model.Confirm());
    }
}
