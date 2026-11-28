using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lockpick;
using Xunit;

namespace OpenCw.Ui.Tests;

sealed class FakeLockpickSession : ILockpickSession
{
    public LockpickView ViewValue { get; set; } = new(LockLevel.Expert, 0, 0, 100, 5, false, LockpickPhase.Active);
    public bool Valid { get; set; } = true;
    public double Moved { get; private set; }
    public List<(double Turn, double Seconds)> Steps { get; } = new();
    public List<string> Cues { get; } = new();
    public Action<FakeLockpickSession>? OnStep { get; set; }

    public LockpickView View => ViewValue;
    public bool StillValid => Valid;

    public void MovePick(double degrees) => Moved += degrees;

    public void Step(double turnInput, double seconds)
    {
        Steps.Add((turnInput, seconds));
        OnStep?.Invoke(this);
    }

    public IReadOnlyList<string> DrainCues()
    {
        var cues = Cues.ToArray();
        Cues.Clear();
        return cues;
    }

    public void SetPins(int pins) => ViewValue = ViewValue with { PinsLeft = pins };

    public void SetPhase(LockpickPhase phase) => ViewValue = ViewValue with { Phase = phase };
}

sealed class FakeLockpickRules : ILockpickRules
{
    public LockpickEntry Entry { get; set; } = new(string.Empty, new FakeLockpickSession());

    public string? LastLock { get; private set; }

    public LockpickEntry Begin(string lockId)
    {
        LastLock = lockId;
        return Entry;
    }
}

public class LockpickMenuTests
{
    static (LockpickMenuModel Model, FakeLockpickSession Session, List<(LockpickOutcome Outcome, int Broken)> Closed) Open(double initialStick = 0)
    {
        var session = new FakeLockpickSession();
        var model = new LockpickMenuModel(session, null, initialStick);
        var closed = new List<(LockpickOutcome, int)>();
        model.Closed += (outcome, broken) => closed.Add((outcome, broken));
        return (model, session, closed);
    }

    [Fact]
    public void Escape_closes_on_release_and_not_on_press()
    {
        var (model, _, closed) = Open();

        model.PressCancel(LockpickCancelKey.Escape);
        Assert.Empty(closed);
        Assert.Equal(LockpickMenuPhase.Active, model.Phase);

        model.ReleaseCancel(LockpickCancelKey.Escape);

        Assert.Equal(new[] { (LockpickOutcome.Cancelled, 0) }, closed);
        Assert.Equal(LockpickMenuPhase.Closed, model.Phase);
    }

    [Fact]
    public void Release_of_a_cancel_key_that_was_never_pressed_here_is_ignored()
    {
        var (model, _, closed) = Open();

        model.ReleaseCancel(LockpickCancelKey.Escape);
        model.PressCancel(LockpickCancelKey.PadB);
        model.ReleaseCancel(LockpickCancelKey.Escape);

        Assert.Empty(closed);
        Assert.Equal(LockpickMenuPhase.Active, model.Phase);

        model.ReleaseCancel(LockpickCancelKey.PadB);
        Assert.Single(closed);
    }

    [Fact]
    public void Turn_keys_do_not_stack_and_the_session_gets_one_turn_input_while_any_is_held()
    {
        var (model, session, _) = Open();

        model.SetTurn(LockpickTurnKey.W, true);
        model.SetTurn(LockpickTurnKey.W, true);
        model.SetTurn(LockpickTurnKey.D, true);
        model.Tick(0.1);
        model.SetTurn(LockpickTurnKey.W, false);
        model.Tick(0.1);
        model.SetTurn(LockpickTurnKey.D, false);
        model.Tick(0.1);

        Assert.Equal(new[] { (1.0, 0.1), (1.0, 0.1), (0.0, 0.1) }, session.Steps);
    }

    [Fact]
    public void Mouse_motion_moves_the_pick_by_the_configured_degrees_per_pixel()
    {
        var (model, session, _) = Open();

        model.MovePickMouse(40);
        model.MovePickMouse(-8);
        model.MovePickMouse(double.NaN);

        Assert.Equal(8.0, session.Moved, 6);
    }

    [Fact]
    public void Stick_inside_the_dead_zone_does_nothing_and_outside_uses_a_cubic_response()
    {
        var (model, session, _) = Open();

        model.SetStick(0.1);
        model.Tick(1);
        Assert.Equal(0, session.Moved);

        model.SetStick(0.5);
        model.Tick(1);
        Assert.Equal(0.125 * 400, session.Moved, 6);

        model.SetStick(-1);
        model.Tick(0.5);
        Assert.Equal(0.125 * 400 - 200, session.Moved, 6);
    }

    [Fact]
    public void A_stick_held_when_the_menu_opens_is_ignored_until_it_returns_to_neutral()
    {
        var (model, session, _) = Open(initialStick: 1);

        model.SetStick(1);
        model.Tick(1);
        Assert.Equal(0, session.Moved);

        model.SetStick(0);
        model.SetStick(1);
        model.Tick(1);

        Assert.Equal(400, session.Moved, 6);
    }

    [Fact]
    public void Solved_lock_stops_input_and_closes_unlocked_only_after_the_delay()
    {
        var (model, session, closed) = Open();
        model.SetTurn(LockpickTurnKey.W, true);
        session.OnStep = s => s.SetPhase(LockpickPhase.Solved);

        model.Tick(0.1);

        Assert.Equal(LockpickMenuPhase.Unlocking, model.Phase);
        Assert.False(model.CanExit);

        model.PressCancel(LockpickCancelKey.Escape);
        model.ReleaseCancel(LockpickCancelKey.Escape);
        model.Tick(1.5);
        Assert.Empty(closed);
        Assert.Equal(0.0, session.Steps[^1].Turn);

        model.Tick(0.01);

        Assert.Equal(new[] { (LockpickOutcome.Unlocked, 0) }, closed);
    }

    [Fact]
    public void Breaking_a_pin_reports_the_remaining_count_and_the_closed_outcome_carries_the_total()
    {
        var (model, session, closed) = Open();
        var remaining = new List<int>();
        model.PinBroken += remaining.Add;

        session.SetPins(4);
        model.Tick(0.1);
        session.SetPins(2);
        model.Tick(0.1);
        model.Tick(0.1);
        model.PressCancel(LockpickCancelKey.PadB);
        model.ReleaseCancel(LockpickCancelKey.PadB);

        Assert.Equal(new[] { 4, 2 }, remaining);
        Assert.Equal(3, model.PinsBroken);
        Assert.Equal(new[] { (LockpickOutcome.Cancelled, 3) }, closed);
    }

    [Fact]
    public void Running_out_of_pins_closes_with_a_message()
    {
        var (model, session, closed) = Open();
        session.ViewValue = session.ViewValue with { PinsLeft = 0, Phase = LockpickPhase.OutOfPins };

        model.Tick(0.1);

        Assert.Equal(new[] { (LockpickOutcome.OutOfPins, 5) }, closed);
        Assert.Equal(new LockpickOptions().OutOfPinsText, model.Message);
    }

    [Fact]
    public void Session_that_stops_being_valid_aborts_and_closes_only_once()
    {
        var (model, session, closed) = Open();

        session.Valid = false;
        model.Tick(0.1);
        model.Tick(0.1);
        model.Abort();

        Assert.Equal(new[] { (LockpickOutcome.Aborted, 0) }, closed);
        Assert.Empty(session.Steps);
    }

    [Fact]
    public void Audio_cues_are_forwarded_and_drained()
    {
        var (model, session, _) = Open();
        var cues = new List<string>();
        model.AudioCue += cues.Add;
        session.Cues.Add("pick_creak");
        session.Cues.Add("pin_break");

        model.Tick(0.1);
        model.Tick(0.1);

        Assert.Equal(new[] { "pick_creak", "pin_break" }, cues);
    }

    [Fact]
    public void Input_after_close_is_ignored()
    {
        var (model, session, closed) = Open();
        model.Abort();

        model.SetTurn(LockpickTurnKey.W, true);
        model.MovePickMouse(100);
        model.Tick(0.1);

        Assert.Equal(0, session.Moved);
        Assert.Single(closed);
    }

    [Fact]
    public void Text_follows_the_session_and_the_last_input_device()
    {
        var (model, session, _) = Open();
        var options = new LockpickOptions();

        Assert.Equal("EXPERT LOCK", model.Title);
        Assert.Equal("5 PINS LEFT", model.PinsText);
        Assert.Equal(options.MouseInstruction, model.Instruction);

        session.SetPins(1);
        model.NoteDevice(true);

        Assert.Equal("1 PIN LEFT", model.PinsText);
        Assert.Equal(options.ControllerInstruction, model.Instruction);
    }

    [Fact]
    public void Rules_refusal_gives_a_reason_and_no_menu()
    {
        var rules = new FakeLockpickRules { Entry = new LockpickEntry("Jammed.", null) };

        var model = LockpickMenuModel.TryOpen(rules, "door7", out string reason);

        Assert.Null(model);
        Assert.Equal("Jammed.", reason);
        Assert.Equal("door7", rules.LastLock);
    }

    [Fact]
    public void Rules_acceptance_opens_a_menu_over_the_session()
    {
        var session = new FakeLockpickSession();
        var rules = new FakeLockpickRules { Entry = new LockpickEntry(string.Empty, session) };

        var model = LockpickMenuModel.TryOpen(rules, "door7", out string reason);

        Assert.NotNull(model);
        Assert.Equal(string.Empty, reason);
        model!.MovePickMouse(4);
        Assert.Equal(1.0, session.Moved, 6);
    }
}
