using System.Collections.Generic;
using OpenCommonwealth.Services.Interaction;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DoorSwingTests
{
    private static readonly DoorSwingPlan Plan = new(2f, 1f, DoorSwingStyle.Hinge, true);

    private static DoorSwingSequence Seq(string name, float start, float stop, float frequency = 1f) => new(name, start, stop, frequency);

    [Fact]
    public void The_plan_takes_open_and_close_durations_from_the_named_sequences_ignoring_case()
    {
        var plan = DoorSwingPlan.From([Seq("idle", 0, 9), Seq("OPEN", 0, 1.5f), Seq("close", 1.5f, 2.25f)], 0);
        Assert.True(plan.FromSequence);
        Assert.Equal(1.5f, plan.OpenSeconds);
        Assert.Equal(0.75f, plan.CloseSeconds);
        Assert.Equal(DoorSwingStyle.Hinge, plan.Style);
    }

    [Fact]
    public void A_sequence_duration_is_its_span_over_its_frequency()
    {
        Assert.Equal(1f, DoorSwingPlan.From([Seq("Open", 0, 2, 2)], 0).OpenSeconds);
    }

    [Fact]
    public void A_missing_close_sequence_reuses_the_open_duration()
    {
        var plan = DoorSwingPlan.From([Seq("Open", 0, 1.25f)], 0);
        Assert.True(plan.FromSequence);
        Assert.Equal(1.25f, plan.CloseSeconds);
    }

    [Theory]
    [InlineData(1f, 1f, 1f)]
    [InlineData(2f, 1f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 1f, float.NaN)]
    [InlineData(0f, float.NaN, 1f)]
    public void A_sequence_with_no_usable_duration_falls_back(float start, float stop, float frequency)
    {
        var plan = DoorSwingPlan.From([Seq("Open", start, stop, frequency)], 0);
        Assert.False(plan.FromSequence);
        Assert.Equal(DoorSwingPlan.FallbackSeconds, plan.OpenSeconds);
        Assert.Equal(DoorSwingPlan.FallbackSeconds, plan.CloseSeconds);
    }

    [Fact]
    public void Without_sequences_the_plan_falls_back()
    {
        var plan = DoorSwingPlan.From([], 0);
        Assert.False(plan.FromSequence);
        Assert.Equal(DoorSwingPlan.FallbackSeconds, plan.OpenSeconds);
    }

    [Theory]
    [InlineData((byte)0, DoorSwingStyle.Hinge)]
    [InlineData((byte)0x02, DoorSwingStyle.Hinge)]
    [InlineData((byte)0x10, DoorSwingStyle.Slide)]
    [InlineData((byte)0x12, DoorSwingStyle.Slide)]
    public void The_sliding_flag_picks_the_slide_style_with_or_without_sequences(byte flags, DoorSwingStyle style)
    {
        Assert.Equal(style, DoorSwingPlan.From([], flags).Style);
        Assert.Equal(style, DoorSwingPlan.From([Seq("Open", 0, 1)], flags).Style);
    }

    [Fact]
    public void The_door_base_carries_the_record_flags_into_the_plan()
    {
        Assert.Equal(DoorSwingStyle.Slide, DoorSwingPlan.From([], new DoorBase(null, null, [], 0x10).Flags).Style);
        Assert.Equal((byte)0, new DoorBase(null, null, []).Flags);
    }

    [Fact]
    public void A_closed_door_is_idle_until_commanded()
    {
        var swing = new DoorSwing(Plan);
        Assert.Equal(DoorSwingPhase.Closed, swing.Phase);
        Assert.False(swing.Advance(1f));
        Assert.Equal(0f, swing.Progress);
    }

    [Fact]
    public void Opening_advances_by_time_over_the_open_duration_and_stops_open()
    {
        var swing = new DoorSwing(Plan);
        swing.Command(true);
        Assert.Equal(DoorSwingPhase.Opening, swing.Phase);
        Assert.True(swing.Advance(1f));
        Assert.Equal(0.5f, swing.Progress);
        Assert.False(swing.Advance(5f));
        Assert.Equal(1f, swing.Progress);
        Assert.Equal(DoorSwingPhase.Open, swing.Phase);
        Assert.False(swing.Moving);
    }

    [Fact]
    public void Closing_uses_the_close_duration_and_stops_closed()
    {
        var swing = new DoorSwing(Plan, true);
        Assert.Equal(DoorSwingPhase.Open, swing.Phase);
        swing.Command(false);
        Assert.Equal(DoorSwingPhase.Closing, swing.Phase);
        Assert.True(swing.Advance(0.25f));
        Assert.Equal(0.75f, swing.Progress);
        Assert.False(swing.Advance(5f));
        Assert.Equal(0f, swing.Progress);
        Assert.Equal(DoorSwingPhase.Closed, swing.Phase);
    }

    [Fact]
    public void A_command_mid_swing_reverses_from_the_current_progress()
    {
        var swing = new DoorSwing(Plan);
        swing.Command(true);
        swing.Advance(1f);
        swing.Command(false);
        Assert.Equal(DoorSwingPhase.Closing, swing.Phase);
        Assert.Equal(0.5f, swing.Progress);
        swing.Advance(0.25f);
        Assert.Equal(0.25f, swing.Progress);
    }

    [Fact]
    public void Repeating_the_same_command_changes_nothing()
    {
        var swing = new DoorSwing(Plan);
        swing.Command(true);
        swing.Advance(1f);
        swing.Command(true);
        Assert.Equal(0.5f, swing.Progress);
        Assert.Equal(DoorSwingPhase.Opening, swing.Phase);
    }

    [Fact]
    public void Snap_jumps_to_the_end_without_moving()
    {
        var swing = new DoorSwing(Plan);
        swing.Snap(true);
        Assert.Equal(1f, swing.Progress);
        Assert.Equal(DoorSwingPhase.Open, swing.Phase);
        Assert.False(swing.Moving);
        swing.Command(true);
        Assert.False(swing.Advance(1f));
        swing.Snap(false);
        Assert.Equal(0f, swing.Progress);
        Assert.Equal(DoorSwingPhase.Closed, swing.Phase);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void A_non_positive_or_invalid_step_does_not_move_the_door(float seconds)
    {
        var swing = new DoorSwing(Plan);
        swing.Command(true);
        Assert.True(swing.Advance(seconds));
        Assert.Equal(0f, swing.Progress);
    }

    [Fact]
    public void A_door_that_started_open_and_is_told_to_open_stays_open()
    {
        var swing = new DoorSwing(Plan, true);
        swing.Command(true);
        Assert.False(swing.Advance(1f));
        Assert.Equal(DoorSwingPhase.Open, swing.Phase);
    }

    [Fact]
    public void The_placeholder_hide_never_applies_while_the_swing_replaces_it()
    {
        Assert.False(DoorVisual.ShouldToggle(false, true, true, false, true));
        Assert.False(DoorVisual.ShouldToggle(false, false, false, true, true));
        Assert.True(DoorVisual.ShouldToggle(false, true, true, false, false));
        Assert.True(DoorVisual.ShouldToggle(false, false, false, true, false));
    }

    private static NifAnimSequenceInfo Info(string name, float start, float stop, float frequency = 1f) => new()
    {
        Block = 1, Name = name, Weight = 1f, TextKeysRef = -1, Cycle = 2, Frequency = frequency, StartTime = start, StopTime = stop,
        ManagerRef = -1, AccumRoot = "", Controlled = [],
    };

    [Fact]
    public void Radicle_sequence_infos_map_to_swing_sequences_that_set_the_plan_durations()
    {
        var sequences = DoorSwingSequences.From([Info("Open", 0f, 2f, 2f), Info("Close", 2f, 3.5f), Info("Idle", 0f, 9f)]);
        Assert.Equal(new DoorSwingSequence("Open", 0f, 2f, 2f), sequences[0]);
        Assert.Equal(3, sequences.Count);
        var plan = DoorSwingPlan.From(sequences, 0);
        Assert.True(plan.FromSequence);
        Assert.Equal(1f, plan.OpenSeconds);
        Assert.Equal(1.5f, plan.CloseSeconds);
    }

    [Fact]
    public void A_nif_with_no_sequences_gives_the_fallback_plan()
    {
        Assert.Empty(DoorSwingSequences.From([]));
        Assert.False(DoorSwingPlan.From(DoorSwingSequences.From([]), 0).FromSequence);
    }

    [Fact]
    public void A_restored_open_door_is_snapped_to_the_end_and_a_restored_closed_one_to_the_start()
    {
        var swing = new DoorSwing(Plan);
        swing.Snap(true);
        Assert.Equal(DoorSwingPhase.Open, swing.Phase);
        Assert.Equal(1f, swing.Progress);
        swing.Snap(false);
        Assert.Equal(DoorSwingPhase.Closed, swing.Phase);
        Assert.False(swing.Advance(1f));
    }
}
