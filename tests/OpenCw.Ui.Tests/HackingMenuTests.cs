using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HackingMenuTests
{
    static HackingMenuModel Playing(FakeHackingSession session, List<HackingOutcome>? finished = null)
    {
        var model = new HackingMenuModel(session);
        finished ??= new List<HackingOutcome>();
        model.Finished += finished.Add;
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        return model;
    }

    [Fact]
    public void Board_types_out_at_the_configured_rate_and_accept_reveals_all_of_it()
    {
        var model = new HackingMenuModel(new FakeHackingSession());

        model.Tick(1);
        Assert.Equal(60, model.RevealedCells);
        Assert.Equal(HackingPhase.Typing, model.Phase);
        Assert.Null(model.Highlight);

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(384, model.RevealedCells);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        Assert.Equal(0, model.Cursor);
    }

    [Fact]
    public void Cursor_crosses_the_column_seam_on_the_same_row_and_stops_at_the_edges()
    {
        var model = Playing(new FakeHackingSession());

        model.Hover(11);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(192, model.Cursor);

        model.Handle(MenuInputAction.Left);
        Assert.Equal(11, model.Cursor);

        model.Hover(12 * 3 + 11);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(192 + 36, model.Cursor);

        model.Hover(192 + 11);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(192 + 11, model.Cursor);

        model.Hover(0);
        model.Handle(MenuInputAction.Left);
        model.Handle(MenuInputAction.Up);
        Assert.Equal(0, model.Cursor);

        model.Handle(MenuInputAction.Down);
        Assert.Equal(12, model.Cursor);

        model.Hover(15 * 12 + 5);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(15 * 12 + 5, model.Cursor);
    }

    [Fact]
    public void Hover_rejects_cells_outside_the_board()
    {
        var model = Playing(new FakeHackingSession());

        Assert.False(model.Hover(-1));
        Assert.False(model.Hover(384));
        Assert.True(model.Hover(383));
        Assert.Equal(383, model.Cursor);
    }

    [Fact]
    public void Highlight_covers_the_whole_word_under_the_cursor()
    {
        var model = Playing(new FakeHackingSession());

        Assert.Equal("COLD", model.Prompt);

        model.Hover(14);

        Assert.Equal(new HackingHit(12, 4, HackingHitKind.Word), model.Highlight);
        Assert.Equal("CORD", model.Prompt);

        model.Hover(8);
        Assert.Null(model.Highlight);
        Assert.Equal(string.Empty, model.Prompt);
    }

    [Fact]
    public void A_wrong_guess_logs_the_word_the_denial_and_the_likeness_the_session_reported()
    {
        var session = new FakeHackingSession(4);
        var model = Playing(session);

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { new HackingHit(0, 4, HackingHitKind.Word) }, session.Selected);
        Assert.Equal(new[] { "COLD", "Entry denied.", "Likeness=3" }, model.Log);
        Assert.Equal("3 ATTEMPTS LEFT", model.AttemptsText);
        Assert.Equal(HackingPhase.Playing, model.Phase);
    }

    [Fact]
    public void Accepting_with_the_cursor_on_filler_never_asks_the_session()
    {
        var session = new FakeHackingSession();
        var model = Playing(session);
        model.Hover(8);

        model.Handle(MenuInputAction.Accept);

        Assert.Empty(session.Selected);
        Assert.Empty(model.Log);
    }

    [Fact]
    public void The_correct_word_holds_for_five_seconds_then_reports_accepted_once()
    {
        var finished = new List<HackingOutcome>();
        var model = Playing(new FakeHackingSession(), finished);
        model.Hover(12);

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(HackingPhase.Accepted, model.Phase);
        Assert.Equal("Access granted.", model.Log[^1]);
        model.Handle(MenuInputAction.Back);
        model.Tick(4.9);
        Assert.Empty(finished);

        model.Tick(0.2);
        model.Tick(1);

        Assert.Equal(new[] { HackingOutcome.Accepted }, finished);
        Assert.Equal(HackingPhase.Closed, model.Phase);
    }

    [Fact]
    public void Running_out_of_attempts_holds_for_three_seconds_then_reports_locked_out()
    {
        var finished = new List<HackingOutcome>();
        var model = Playing(new FakeHackingSession(1), finished);

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(HackingPhase.LockedOut, model.Phase);
        Assert.Equal("Terminal locked.", model.Log[^1]);
        model.Tick(2.9);
        Assert.Empty(finished);

        model.Tick(0.2);

        Assert.Equal(new[] { HackingOutcome.LockedOut }, finished);
    }

    [Fact]
    public void Help_results_from_the_session_are_logged_and_announced_without_costing_the_turn()
    {
        var session = new FakeHackingSession();
        var model = Playing(session);
        var helps = new List<HackingGuessKind>();
        model.HelpUsed += helps.Add;
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.DudRemoved));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.AttemptsRestored));
        model.Hover(30);

        model.Handle(MenuInputAction.Accept);
        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { HackingGuessKind.DudRemoved, HackingGuessKind.AttemptsRestored }, helps);
        Assert.Equal(new[] { "Dud removed.", "Attempts restored." }, model.Log);
        Assert.Equal(HackingPhase.Playing, model.Phase);
    }

    [Fact]
    public void An_ignored_guess_leaves_the_log_alone()
    {
        var session = new FakeHackingSession();
        var model = Playing(session);
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.Ignored));

        model.Handle(MenuInputAction.Accept);

        Assert.Single(session.Selected);
        Assert.Empty(model.Log);
    }

    [Fact]
    public void The_log_keeps_only_the_newest_fifteen_lines()
    {
        var model = Playing(new FakeHackingSession(100));

        for (int i = 0; i < 6; i++)
            model.Handle(MenuInputAction.Accept);

        Assert.Equal(15, model.Log.Count);
        Assert.Equal("Likeness=3", model.Log[^1]);
        Assert.Equal("Entry denied.", model.Log[^2]);
    }

    [Fact]
    public void Back_cancels_while_typing_or_playing_and_only_once()
    {
        var typing = new List<HackingOutcome>();
        var typed = new HackingMenuModel(new FakeHackingSession());
        typed.Finished += typing.Add;
        typed.Handle(MenuInputAction.Back);
        typed.Handle(MenuInputAction.Back);
        Assert.Equal(new[] { HackingOutcome.Cancelled }, typing);

        var finished = new List<HackingOutcome>();
        var playing = Playing(new FakeHackingSession(), finished);
        playing.Handle(MenuInputAction.Back);
        playing.Tick(10);
        Assert.Equal(new[] { HackingOutcome.Cancelled }, finished);
    }

    [Fact]
    public void A_session_that_stops_being_valid_cancels_on_the_next_tick()
    {
        var session = new FakeHackingSession();
        var finished = new List<HackingOutcome>();
        var model = Playing(session, finished);

        session.Valid = false;
        model.Tick(0.1);

        Assert.Equal(new[] { HackingOutcome.Cancelled }, finished);
        Assert.Equal(HackingPhase.Closed, model.Phase);
    }

    [Fact]
    public void Sounds_are_requested_for_hover_on_a_word_and_for_the_result()
    {
        var model = Playing(new FakeHackingSession());
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        model.Hover(13);
        model.Hover(8);
        model.Hover(12);
        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "hover_word", "hover_word", "password_good", "terminal.accept" }, sounds);
        Assert.Equal(2, sounds.Count(s => s == "hover_word"));
    }

    [Fact]
    public void Every_validated_word_and_bracket_plays_the_enter_sound_after_its_result_sound_and_an_ignored_guess_plays_none()
    {
        var session = new FakeHackingSession();
        var model = Playing(session);
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.Wrong, 1));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.DudRemoved));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.AttemptsRestored));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.Ignored));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.LockedOut));

        model.Hover(30);
        sounds.Clear();
        for (int i = 0; i < 5; i++)
            model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[]
        {
            "terminal.accept",
            "help_dud", "terminal.accept",
            "help_attempts", "terminal.accept",
            "password_bad", "terminal.accept",
        }, sounds);
    }

    [Fact]
    public void Skipping_the_typing_plays_the_unmapped_skip_cue_and_not_the_enter_sound()
    {
        var model = new HackingMenuModel(new FakeHackingSession());
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "type_skip" }, sounds);
    }
}
