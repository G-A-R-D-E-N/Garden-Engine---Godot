using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HackingMenuEdgeTests
{
    static HackingMenuModel Playing(FakeHackingSession session, HackingOptions? options = null)
    {
        var model = new HackingMenuModel(session, options);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        return model;
    }

    [Fact]
    public void A_fresh_model_reports_the_session_board_attempts_and_the_two_hints()
    {
        Assert.Throws<ArgumentNullException>(() => new HackingMenuModel(null!));
        var model = new HackingMenuModel(new FakeHackingSession());

        Assert.Equal(HackingGeometry.Standard, model.Geometry);
        Assert.Equal(4, model.MaxAttempts);
        Assert.Equal(4, model.AttemptsRemaining);
        Assert.Equal("4 ATTEMPTS LEFT", model.AttemptsText);
        Assert.Equal(new[] { 'C', 'L', 'D', '.' }, new[] { model.CellAt(0), model.CellAt(2), model.CellAt(15), model.CellAt(5) });
        Assert.Equal(-1, model.Cursor);
        Assert.Null(model.Highlight);
        Assert.Equal(string.Empty, model.Prompt);
        Assert.Equal(new[] { (MenuInputAction.Accept, "Select"), (MenuInputAction.Back, "Exit") }, model.Hints.Select(h => (h.Action, h.Label)));
    }

    [Fact]
    public void Typing_ignores_bad_ticks_and_finishing_the_board_by_time_starts_play_on_the_first_cell()
    {
        var model = new HackingMenuModel(new FakeHackingSession());
        var changes = 0;
        model.Changed += () => changes++;

        model.Tick(0);
        model.Tick(-1);
        model.Tick(double.NaN);
        Assert.Equal(0, model.RevealedCells);
        Assert.Equal(0, changes);

        model.Tick(6.4);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        Assert.Equal(384, model.RevealedCells);
        Assert.Equal(0, model.Cursor);
        Assert.Equal("COLD", model.Prompt);
        model.Tick(10);
        Assert.Equal(384, model.RevealedCells);
    }

    [Fact]
    public void The_cursor_steps_inside_a_column_and_only_cells_on_a_word_or_bracket_make_the_hover_sound()
    {
        var model = Playing(new FakeHackingSession());
        var sounds = new List<string>();
        var changes = 0;
        model.SoundRequested += sounds.Add;
        model.Changed += () => changes++;

        model.Hover(5);
        model.Handle(MenuInputAction.Right);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(18, model.Cursor);
        model.Handle(MenuInputAction.Left);
        model.Handle(MenuInputAction.Up);
        Assert.Equal(5, model.Cursor);
        Assert.Empty(sounds);
        Assert.Equal(5, changes);

        Assert.True(model.Hover(5));
        Assert.Equal(5, changes);

        model.Hover(0);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(new HackingHit(12, 4, HackingHitKind.Word), model.Highlight);
        Assert.Equal("CORD", model.Prompt);
        model.Handle(MenuInputAction.Up);
        Assert.Equal("COLD", model.Prompt);
        Assert.Equal(new[] { "hover_word", "hover_word", "hover_word" }, sounds);

        model.Hover(31);
        Assert.Equal(new HackingHit(30, 4, HackingHitKind.Bracket), model.Highlight);
        Assert.Equal("(..)", model.Prompt);
    }

    [Fact]
    public void Actions_the_hacking_board_does_not_use_are_not_handled_while_playing()
    {
        var model = Playing(new FakeHackingSession());

        Assert.False(model.Handle(MenuInputAction.Search));
        Assert.False(model.Handle(MenuInputAction.PageUp));
        Assert.Equal(0, model.Cursor);
    }

    [Fact]
    public void After_a_correct_guess_input_is_swallowed_until_the_hold_ends()
    {
        var session = new FakeHackingSession();
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.Correct));
        var model = Playing(session);
        var finished = new List<HackingOutcome>();
        model.Finished += finished.Add;

        model.Handle(MenuInputAction.Accept);
        Assert.Equal(HackingPhase.Accepted, model.Phase);
        Assert.Equal(new[] { "COLD", "Access granted." }, model.Log);

        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.True(model.Handle(MenuInputAction.Down));
        Assert.True(model.Handle(MenuInputAction.Accept));
        Assert.Equal(HackingPhase.Accepted, model.Phase);
        Assert.Equal(0, model.Cursor);
        Assert.Empty(finished);
        Assert.Null(model.Highlight);
    }

    [Fact]
    public void Log_lines_wrap_at_the_configured_width_and_the_attempt_label_comes_from_the_strings()
    {
        var options = new HackingOptions { LogColumns = 10, Strings = new HackingStrings { Denied = "Entry denied now", Attempts = "TRIES" } };
        var model = Playing(new FakeHackingSession(), options);

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "COLD", "Entry", "denied now", "Likeness=3" }, model.Log);
        Assert.Equal("3 TRIES", model.AttemptsText);
        Assert.Equal(HackingPhase.Playing, model.Phase);
    }

    [Fact]
    public void Cancel_finishes_once_and_a_closed_board_ignores_input_and_time()
    {
        var model = Playing(new FakeHackingSession());
        var finished = new List<HackingOutcome>();
        var changes = 0;
        model.Finished += finished.Add;
        model.Changed += () => changes++;

        model.Cancel();
        model.Cancel();

        Assert.Equal(new[] { HackingOutcome.Cancelled }, finished);
        Assert.Equal(HackingPhase.Closed, model.Phase);
        Assert.Equal(1, changes);
        Assert.False(model.Handle(MenuInputAction.Accept));
        Assert.False(model.Hover(3));
        model.Tick(10);
        Assert.Equal(1, changes);
    }
}

public class TerminalFlowEdgeTests
{
    const string Id = "term1";

    static FakeTerminalSource Source()
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "ROBCO", "WELCOME", "Body",
            FakeTerminalSource.Entry("a", "A", TerminalEntryKind.Redraw));
        return source;
    }

    static FakeTerminalSource Locked(FakeHackingSession? session = null)
    {
        var source = Source();
        source.Opening = new TerminalOpening(TerminalOpenStatus.Locked, string.Empty, string.Empty, session ?? new FakeHackingSession());
        return source;
    }

    [Fact]
    public void Opening_needs_a_source_and_a_locked_terminal_without_a_hack_or_an_open_one_without_a_root_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalFlowModel.TryOpen(null!, Id, out _));

        var lockedNoHack = Source();
        lockedNoHack.Opening = new TerminalOpening(TerminalOpenStatus.Locked);
        Assert.Null(TerminalFlowModel.TryOpen(lockedNoHack, Id, out var reason));
        Assert.Equal(TerminalFlowModel.DefaultFailure, reason);

        var noRoot = Source();
        noRoot.Opening = new TerminalOpening(TerminalOpenStatus.Open, "", "No root");
        Assert.Null(TerminalFlowModel.TryOpen(noRoot, Id, out reason));
        Assert.Equal("No root", reason);

        var ok = TerminalFlowModel.TryOpen(Source(), Id, out reason);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(Id, ok!.TerminalId);
    }

    [Fact]
    public void The_hints_follow_the_phase_and_are_empty_once_closed()
    {
        var flow = TerminalFlowModel.TryOpen(Locked(), Id, out _)!;

        Assert.Equal(TerminalFlowPhase.Login, flow.Phase);
        Assert.Equal(new[] { (MenuInputAction.Back, "Exit") }, flow.Hints.Select(h => (h.Action, h.Label)));
        flow.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
        Assert.Equal(new[] { (MenuInputAction.Accept, "Select"), (MenuInputAction.Back, "Exit") }, flow.Hints.Select(h => (h.Action, h.Label)));

        var menu = TerminalFlowModel.TryOpen(Source(), Id, out _)!;
        Assert.Equal(new[] { (MenuInputAction.Accept, "Accept"), (MenuInputAction.Back, "Exit") }, menu.Hints.Select(h => (h.Action, h.Label)));
        menu.Close();
        Assert.Empty(menu.Hints);
        Assert.Equal(TerminalPhase.Closed, menu.Menu!.Phase);
    }

    [Fact]
    public void The_login_line_types_at_the_hacking_rate_and_hands_over_when_it_is_complete()
    {
        var flow = TerminalFlowModel.TryOpen(Locked(), Id, out _)!;
        var changes = 0;
        flow.Changed += () => changes++;

        Assert.Equal(string.Empty, flow.LoginText);
        flow.Tick(0.1);
        Assert.Equal("PASSWO", flow.LoginText);
        Assert.Equal(TerminalFlowPhase.Login, flow.Phase);
        Assert.Equal(1, changes);
        Assert.True(flow.Handle(MenuInputAction.Search));
        Assert.Equal(TerminalFlowPhase.Login, flow.Phase);

        flow.Tick(10);
        Assert.Equal("PASSWORD REQUIRED", flow.LoginText);
        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
    }

    [Fact]
    public void A_terminal_with_an_empty_login_line_starts_in_hacking()
    {
        var options = new HackingOptions { Strings = new HackingStrings { Login = "" } };

        var flow = TerminalFlowModel.TryOpen(Locked(), Id, out _, hackingOptions: options)!;

        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
        Assert.Equal(string.Empty, flow.LoginText);
    }

    [Fact]
    public void Cracking_a_terminal_whose_menu_cannot_be_built_closes_the_flow_after_telling_the_source()
    {
        var source = Locked();
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        var closes = 0;
        flow.Closed += () => closes++;
        source.Nodes.Clear();
        flow.Handle(MenuInputAction.Accept);
        flow.Handle(MenuInputAction.Accept);
        flow.Hack!.Hover(12);

        flow.Handle(MenuInputAction.Accept);
        flow.Tick(5.1);

        Assert.Equal(new[] { HackingOutcome.Accepted }, source.Hacks);
        Assert.Equal(HackingOutcome.Accepted, flow.LastHackOutcome);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Null(flow.Menu);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void A_closed_flow_refuses_input_ignores_time_and_closes_only_once()
    {
        var flow = TerminalFlowModel.TryOpen(Source(), Id, out _)!;
        var closes = 0;
        flow.Closed += () => closes++;

        flow.Close();
        flow.Close();

        Assert.Equal(1, closes);
        Assert.False(flow.Handle(MenuInputAction.Accept));
        flow.Tick(10);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
    }
}
