using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalGap9Tests
{
    const string Id = "term1";

    static FakeTerminalSource Single(string body, params TerminalEntry[] entries)
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "H", "W", body, entries);
        return source;
    }

    static TerminalEntry Plain(int i, string? response = null) =>
        FakeTerminalSource.Entry($"e{i}", $"Entry {i}", TerminalEntryKind.Redraw, response: response);

    static TerminalMenuModel Ready(FakeTerminalSource source, TerminalOptions? options = null)
    {
        var model = new TerminalMenuModel(source, Id, "root", options);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        return model;
    }

    [Fact]
    public void The_default_busy_window_is_half_a_second_to_five_seconds()
    {
        var options = new TerminalOptions();

        Assert.Equal(0.5, options.BusyMinimum);
        Assert.Equal(5.0, options.BusyMaximum);
    }

    [Fact]
    public void An_entry_with_nothing_to_type_still_stays_busy_for_the_minimum_time()
    {
        var model = Ready(Single("", Plain(0)), new TerminalOptions { BusyMinimum = 2, BusyMaximum = 4 });
        Assert.True(model.Activate(0));
        Assert.True(model.IsBusy);

        model.Tick(1.5);
        model.Tick(0.25);
        Assert.True(model.IsBusy);
        Assert.Equal(TerminalPhase.Busy, model.Phase);

        model.Tick(0.25);
        Assert.False(model.IsBusy);
        Assert.Equal(TerminalPhase.Typing, model.Phase);
    }

    [Fact]
    public void A_typed_response_waits_for_the_default_minimum_before_the_entry_applies()
    {
        var model = Ready(Single("", Plain(0, "Hi")));
        model.Activate(0);

        model.Tick(0.25);
        Assert.True(model.IsBusy);
        Assert.Equal("Hi", model.Prompt);

        model.Tick(0.25);
        Assert.False(model.IsBusy);
        Assert.Equal(string.Empty, model.Prompt);
    }

    [Fact]
    public void A_response_that_never_finishes_typing_is_cut_off_at_the_maximum()
    {
        var model = Ready(Single("", Plain(0, new string('x', 600))), new TerminalOptions { BusyMaximum = 1.5 });
        model.Activate(0);

        model.Tick(1.25);
        Assert.True(model.IsBusy);
        Assert.Equal(new string('x', 75), model.Prompt);

        model.Tick(0.25);
        Assert.False(model.IsBusy);
        Assert.Equal(TerminalPhase.Typing, model.Phase);
    }

    [Fact]
    public void With_the_default_maximum_a_long_response_applies_at_five_seconds()
    {
        var model = Ready(Single("", Plain(0, new string('x', 400))));
        model.Activate(0);

        model.Tick(4.75);
        Assert.True(model.IsBusy);
        Assert.Equal(new string('x', 285), model.Prompt);

        model.Tick(0.25);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public void Entries_appear_only_on_the_last_page_of_a_node_body()
    {
        var source = Single("a\nb\nc\nd\ne", Plain(0), Plain(1));
        var model = new TerminalMenuModel(source, Id, "root", new TerminalOptions { PageLines = 2 });
        Assert.Equal(3, model.PageCount);
        Assert.True(model.IsTyping);
        Assert.False(model.EntriesVisible);

        model.Handle(MenuInputAction.Accept);
        Assert.False(model.IsTyping);
        Assert.Equal(0, model.Page);
        Assert.False(model.EntriesVisible);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Equal(string.Empty, model.EntryText(0));
        Assert.False(model.IsEntryAvailable(0));
        Assert.False(model.Select(0));
        Assert.False(model.Activate(0));
        Assert.Equal("a\nb", model.Body);

        model.Handle(MenuInputAction.PageDown);
        Assert.True(model.IsTyping);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(1, model.Page);
        Assert.False(model.EntriesVisible);
        Assert.Equal("c\nd", model.Body);

        model.Handle(MenuInputAction.Right);
        Assert.True(model.IsTyping);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(2, model.Page);
        Assert.True(model.EntriesVisible);
        Assert.Equal(new[] { "e0", "e1" }, model.Entries.Select(e => e.Id).ToArray());
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal("Entry 1", model.EntryText(1));
        Assert.True(model.IsEntryAvailable(1));
        Assert.Equal("e", model.Body);

        model.Handle(MenuInputAction.PageUp);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(1, model.Page);
        Assert.False(model.EntriesVisible);
        Assert.Equal(-1, model.SelectedIndex);
    }

    [Fact]
    public void A_terminal_whose_root_node_is_missing_closes_at_once_with_no_entries()
    {
        var model = new TerminalMenuModel(new FakeTerminalSource(), Id, "root");

        Assert.Equal(TerminalPhase.Closed, model.Phase);
        Assert.False(model.IsTyping);
        Assert.False(model.EntriesVisible);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Equal(string.Empty, model.EntryText(0));
        Assert.False(model.IsEntryAvailable(0));
    }

    [Fact]
    public void Accept_on_an_earlier_page_turns_the_page_instead_of_running_an_entry()
    {
        var source = Single("a\nb\nc", Plain(0));
        var model = Ready(source, new TerminalOptions { PageLines = 2 });

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(1, model.Page);
        Assert.True(model.IsTyping);
        Assert.Empty(source.Runs);
        Assert.Equal(
            new[] { MenuInputAction.Accept, MenuInputAction.PageUp, MenuInputAction.PageDown, MenuInputAction.Back },
            model.Hints.Select(h => h.Action).ToArray());
    }

    [Fact]
    public void A_text_page_hides_the_entries_until_back_returns_to_the_node()
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "H", "W", "Body",
            FakeTerminalSource.Entry("log", "Read log", TerminalEntryKind.Text, page: "Log text"));
        var model = Ready(source);
        Assert.True(model.EntriesVisible);

        model.Activate(0);
        model.Tick(1);
        Assert.True(model.IsShowingText);
        Assert.True(model.IsTyping);
        Assert.False(model.EntriesVisible);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.Equal(string.Empty, model.Body);

        model.Handle(MenuInputAction.Accept);
        Assert.False(model.IsTyping);
        Assert.Equal("Log text", model.Body);
        Assert.Equal("Back", model.Hints.Last().Label);

        model.Handle(MenuInputAction.Back);
        model.Handle(MenuInputAction.Accept);
        Assert.False(model.IsShowingText);
        Assert.True(model.EntriesVisible);
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal("Exit", model.Hints.Last().Label);
    }

    [Fact]
    public void Typing_ends_when_everything_has_been_revealed_and_a_closed_terminal_is_not_typing()
    {
        var model = new TerminalMenuModel(Single("", Plain(0)), Id, "root");
        Assert.True(model.IsTyping);
        Assert.Equal(TerminalPhase.Typing, model.Phase);

        model.Tick(1);
        Assert.False(model.IsTyping);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.Equal("H", model.Header);
        Assert.Equal("W", model.Welcome);
        Assert.Equal("Entry 0", model.EntryText(0));

        var closing = new TerminalMenuModel(Single("", Plain(0)), Id, "root");
        closing.Close();
        Assert.False(closing.IsTyping);
        Assert.Equal(TerminalPhase.Closed, closing.Phase);
    }

    sealed class GridSession : IHackingSession
    {
        public HackingGeometry Geometry { get; } = new(3, 4, 5);
        public int MaxAttempts => 4;
        public int AttemptsRemaining => 4;
        public bool StillValid => true;
        public char CellAt(int index) => '.';
        public HackingHit? HitAt(int index) => null;
        public HackingGuess Select(HackingHit hit) => new(HackingGuessKind.Ignored);
    }

    static HackingMenuModel Playing(IHackingSession session, HackingOptions? options = null, List<HackingOutcome>? finished = null)
    {
        var model = new HackingMenuModel(session, options);
        model.Finished += (finished ??= new List<HackingOutcome>()).Add;
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        return model;
    }

    [Fact]
    public void The_standard_board_is_two_columns_of_sixteen_rows_of_twelve_cells()
    {
        var geometry = HackingGeometry.Standard;

        Assert.Equal(new HackingGeometry(2, 16, 12), geometry);
        Assert.Equal(192, geometry.CellsPerColumn);
        Assert.Equal(384, geometry.Cells);
    }

    [Fact]
    public void A_board_with_another_shape_types_out_and_moves_the_cursor_by_its_own_column_size()
    {
        var geometry = new GridSession().Geometry;
        Assert.Equal(20, geometry.CellsPerColumn);
        Assert.Equal(60, geometry.Cells);

        var model = new HackingMenuModel(new GridSession());
        model.Tick(0.5);
        Assert.Equal(30, model.RevealedCells);
        model.Tick(0.5);
        Assert.Equal(60, model.RevealedCells);
        Assert.Equal(HackingPhase.Playing, model.Phase);
        Assert.Equal(0, model.Cursor);

        model.Hover(4);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(20, model.Cursor);

        model.Hover(24);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(40, model.Cursor);

        model.Handle(MenuInputAction.Left);
        Assert.Equal(24, model.Cursor);

        model.Hover(44);
        model.Handle(MenuInputAction.Right);
        Assert.Equal(44, model.Cursor);

        model.Hover(35);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(35, model.Cursor);

        model.Hover(30);
        model.Handle(MenuInputAction.Left);
        Assert.Equal(14, model.Cursor);

        Assert.True(model.Hover(59));
        Assert.False(model.Hover(60));
    }

    [Fact]
    public void The_default_options_and_strings_are_pinned()
    {
        var options = new HackingOptions();

        Assert.Equal(60.0, options.CharsPerSecond);
        Assert.Equal(5.0, options.AcceptedHold);
        Assert.Equal(3.0, options.LockedOutHold);
        Assert.Equal(15, options.LogLines);
        Assert.Equal(34, options.LogColumns);
        Assert.Equal("PASSWORD REQUIRED", options.Strings.Login);
        Assert.Equal("ATTEMPTS LEFT", options.Strings.Attempts);
        Assert.Equal("Entry denied.", options.Strings.Denied);
        Assert.Equal("Likeness={0}", options.Strings.Likeness);
        Assert.Equal("Access granted.", options.Strings.Granted);
        Assert.Equal("Terminal locked.", options.Strings.LockedOut);
        Assert.Equal("Dud removed.", options.Strings.DudRemoved);
        Assert.Equal("Attempts restored.", options.Strings.Restored);
    }

    [Fact]
    public void A_shorter_accepted_hold_and_a_custom_granted_text_apply()
    {
        var finished = new List<HackingOutcome>();
        var options = new HackingOptions { AcceptedHold = 1, Strings = new HackingStrings { Granted = "OK" } };
        var model = Playing(new FakeHackingSession(), options, finished);
        model.Hover(12);

        model.Handle(MenuInputAction.Accept);
        model.Tick(0.75);

        Assert.Equal(new[] { "CORD", "OK" }, model.Log);
        Assert.Empty(finished);
        model.Tick(0.25);
        Assert.Equal(new[] { HackingOutcome.Accepted }, finished);
    }

    [Fact]
    public void A_shorter_locked_out_hold_and_custom_locked_out_text_apply()
    {
        var finished = new List<HackingOutcome>();
        var options = new HackingOptions { LockedOutHold = 0.5, Strings = new HackingStrings { LockedOut = "LOCKED", Denied = "NO" } };
        var model = Playing(new FakeHackingSession(1), options, finished);

        model.Handle(MenuInputAction.Accept);
        model.Tick(0.25);

        Assert.Equal(new[] { "COLD", "NO", "LOCKED" }, model.Log);
        Assert.Empty(finished);
        model.Tick(0.25);
        Assert.Equal(new[] { HackingOutcome.LockedOut }, finished);
    }

    [Fact]
    public void The_log_keeps_the_configured_number_of_lines_and_the_likeness_text_is_formatted()
    {
        var options = new HackingOptions { LogLines = 4, Strings = new HackingStrings { Likeness = "L{0}", Denied = "NO" } };
        var model = Playing(new FakeHackingSession(100), options);

        model.Handle(MenuInputAction.Accept);
        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "L3", "COLD", "NO", "L3" }, model.Log);
    }

    [Fact]
    public void Log_messages_wrap_at_the_configured_column_count()
    {
        var model = Playing(new FakeHackingSession(100), new HackingOptions { LogColumns = 10 });

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "COLD", "Entry", "denied.", "Likeness=3" }, model.Log);
    }

    [Fact]
    public void Help_results_use_the_configured_texts_and_the_attempts_label_is_configurable()
    {
        var session = new FakeHackingSession();
        var strings = new HackingStrings { DudRemoved = "Dud gone", Restored = "Tries back", Attempts = "TRIES" };
        var model = Playing(session, new HackingOptions { Strings = strings });
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.DudRemoved));
        session.Scripted.Enqueue(new HackingGuess(HackingGuessKind.AttemptsRestored));
        model.Hover(30);

        model.Handle(MenuInputAction.Accept);
        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "Dud gone", "Tries back" }, model.Log);
        Assert.Equal("4 TRIES", model.AttemptsText);
    }
}
