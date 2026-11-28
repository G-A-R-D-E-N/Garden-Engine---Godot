using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lockpick;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LockpickGap9Tests
{
    static (LockpickMenuModel Model, FakeLockpickSession Session, List<(LockpickOutcome Outcome, int Broken)> Closed) Open(
        LockpickOptions? options = null, double initialStick = 0)
    {
        var session = new FakeLockpickSession();
        var model = new LockpickMenuModel(session, options, initialStick);
        var closed = new List<(LockpickOutcome, int)>();
        model.Closed += (outcome, broken) => closed.Add((outcome, broken));
        return (model, session, closed);
    }

    [Fact]
    public void Default_options_carry_the_tuned_constants()
    {
        var options = new LockpickOptions();

        Assert.Equal(0.25, options.MouseDegreesPerPixel);
        Assert.Equal(0.15, options.StickDeadZone);
        Assert.Equal(400.0, options.StickDegreesPerSecond);
        Assert.Equal(1.5, options.UnlockDelay);
    }

    [Fact]
    public void Mouse_degrees_per_pixel_scales_the_pick_movement_and_zero_motion_is_not_forwarded()
    {
        var (model, session, _) = Open(new LockpickOptions { MouseDegreesPerPixel = 0.5 });

        model.MovePickMouse(10);
        model.MovePickMouse(-3);
        model.MovePickMouse(0);
        model.MovePickMouse(double.PositiveInfinity);

        Assert.Equal(3.5, session.Moved);
    }

    [Fact]
    public void Stick_exactly_on_the_dead_zone_edge_moves_and_just_below_it_does_not()
    {
        var (model, session, _) = Open();

        model.SetStick(0.1499);
        model.Tick(1);
        Assert.Equal(0.0, session.Moved);

        model.SetStick(0.15);
        model.Tick(1);
        Assert.Equal(0.15 * 0.15 * 0.15 * 400, session.Moved, 9);
    }

    [Fact]
    public void A_custom_dead_zone_is_honoured_in_both_directions()
    {
        var (model, session, _) = Open(new LockpickOptions { StickDeadZone = 0.5 });

        model.SetStick(0.4);
        model.Tick(1);
        model.SetStick(-0.4);
        model.Tick(1);
        Assert.Equal(0.0, session.Moved);

        model.SetStick(-0.5);
        model.Tick(1);
        Assert.Equal(-0.125 * 400, session.Moved, 9);
    }

    [Fact]
    public void Stick_degrees_per_second_and_the_tick_length_scale_the_movement()
    {
        var (model, session, _) = Open(new LockpickOptions { StickDegreesPerSecond = 100 });

        model.SetStick(1);
        model.Tick(0.5);
        model.Tick(0.25);

        Assert.Equal(75.0, session.Moved, 9);
    }

    [Fact]
    public void A_stick_that_starts_exactly_on_the_dead_zone_edge_is_not_armed_until_it_returns_to_neutral()
    {
        var (model, session, _) = Open(initialStick: 0.15);
        model.SetStick(0.15);
        model.Tick(1);
        Assert.Equal(0.0, session.Moved);

        model.SetStick(0.14);
        model.SetStick(1);
        model.Tick(1);
        Assert.Equal(400.0, session.Moved, 9);

        var (armedModel, armedSession, _) = Open(initialStick: 0.1499);
        armedModel.SetStick(1);
        armedModel.Tick(1);
        Assert.Equal(400.0, armedSession.Moved, 9);
    }

    [Fact]
    public void A_non_finite_stick_value_counts_as_neutral_and_moves_nothing()
    {
        var (model, session, _) = Open(initialStick: 1);

        model.SetStick(double.NaN);
        model.Tick(1);
        Assert.Equal(0.0, session.Moved);

        model.SetStick(1);
        model.Tick(1);
        Assert.Equal(400.0, session.Moved, 9);
    }

    [Fact]
    public void The_stick_does_not_move_the_pick_while_unlocking()
    {
        var (model, session, _) = Open();
        session.OnStep = s => s.SetPhase(LockpickPhase.Solved);
        model.SetStick(1);
        model.Tick(0.1);
        Assert.Equal(40.0, session.Moved, 9);
        Assert.Equal(LockpickMenuPhase.Unlocking, model.Phase);

        model.Tick(0.1);
        model.MovePickMouse(100);

        Assert.Equal(40.0, session.Moved, 9);
    }

    [Fact]
    public void Unlock_delay_counts_across_ticks_and_closes_only_when_strictly_exceeded()
    {
        var (model, session, closed) = Open(new LockpickOptions { UnlockDelay = 3 });
        session.OnStep = s => s.SetPhase(LockpickPhase.Solved);

        model.Tick(0.5);
        model.Tick(1);
        model.Tick(1);
        model.Tick(1);
        Assert.Empty(closed);
        Assert.Equal(LockpickMenuPhase.Unlocking, model.Phase);

        model.Tick(0.001);

        Assert.Equal(new[] { (LockpickOutcome.Unlocked, 0) }, closed);
        Assert.Equal(LockpickMenuPhase.Closed, model.Phase);
        Assert.Equal(LockpickOutcome.Unlocked, model.Outcome);
    }

    [Fact]
    public void A_zero_unlock_delay_closes_on_the_tick_after_the_solve()
    {
        var (model, session, closed) = Open(new LockpickOptions { UnlockDelay = 0 });
        session.OnStep = s => s.SetPhase(LockpickPhase.Solved);

        model.Tick(0.1);
        Assert.Empty(closed);

        model.Tick(0.01);
        Assert.Equal(new[] { (LockpickOutcome.Unlocked, 0) }, closed);
    }

    [Fact]
    public void Unlocking_steps_the_session_with_zero_turn_and_clears_held_keys()
    {
        var (model, session, _) = Open();
        session.OnStep = s => s.SetPhase(LockpickPhase.Solved);
        model.SetTurn(LockpickTurnKey.PadA, true);
        model.Tick(0.1);
        model.Tick(0.2);

        Assert.Equal(new[] { (1.0, 0.1), (0.0, 0.2) }, session.Steps);
    }

    [Theory]
    [InlineData(100, "99+ PINS LEFT")]
    [InlineData(99, "99 PINS LEFT")]
    [InlineData(2, "2 PINS LEFT")]
    [InlineData(1, "1 PIN LEFT")]
    [InlineData(0, "0 PINS LEFT")]
    [InlineData(-3, "0 PINS LEFT")]
    public void Pin_text_pluralises_caps_and_never_goes_negative(int pins, string expected)
    {
        var (model, session, _) = Open();
        session.SetPins(pins);

        Assert.Equal(expected, model.PinsText);
    }

    [Theory]
    [InlineData(LockLevel.Novice, "NOVICE LOCK")]
    [InlineData(LockLevel.Advanced, "ADVANCED LOCK")]
    [InlineData(LockLevel.Expert, "EXPERT LOCK")]
    [InlineData(LockLevel.Master, "MASTER LOCK")]
    public void Title_names_the_lock_level_in_capitals(LockLevel level, string expected)
    {
        var (model, session, _) = Open();
        session.ViewValue = session.ViewValue with { Level = level };

        Assert.Equal(expected, model.Title);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_tick_with_no_usable_duration_does_nothing(double seconds)
    {
        var (model, session, closed) = Open();
        model.SetStick(1);

        model.Tick(seconds);

        Assert.Empty(session.Steps);
        Assert.Equal(0.0, session.Moved);
        Assert.Empty(closed);
    }

    [Fact]
    public void Changed_fires_once_per_live_tick_and_only_when_the_device_really_changes()
    {
        var (model, _, _) = Open();
        int changed = 0;
        model.Changed += () => changed++;

        model.NoteDevice(false);
        Assert.Equal(0, changed);

        model.NoteDevice(true);
        Assert.Equal(1, changed);
        Assert.True(model.UsingController);

        model.Tick(0.1);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Refusal_without_a_reason_falls_back_to_the_option_text()
    {
        var rules = new FakeLockpickRules { Entry = new LockpickEntry(string.Empty, null) };

        Assert.Null(LockpickMenuModel.TryOpen(rules, "door1", out string reason));
        Assert.Equal("This lock cannot be picked.", reason);

        Assert.Null(LockpickMenuModel.TryOpen(rules, "door1", out reason, new LockpickOptions { RefusedText = "Nope." }));
        Assert.Equal("Nope.", reason);
    }
}
