using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalMenuEdgeTests
{
    const string Id = "term1";

    static FakeTerminalSource Single(string id, string header, string welcome, string body, params TerminalEntry[] entries)
    {
        var source = new FakeTerminalSource();
        source.Nodes[id] = FakeTerminalSource.Node(id, header, welcome, body, entries);
        return source;
    }

    static TerminalMenuModel Ready(FakeTerminalSource source, TerminalOptions? options = null, string root = "root")
    {
        var model = new TerminalMenuModel(source, Id, root, options);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        return model;
    }

    static TerminalEntry Plain(int i) => FakeTerminalSource.Entry($"e{i}", $"Entry {i}", TerminalEntryKind.Redraw);

    [Fact]
    public void Construction_rejects_a_missing_source_and_an_empty_terminal_or_root_id()
    {
        var source = Single("root", "H", "", "");

        Assert.Throws<ArgumentNullException>(() => new TerminalMenuModel(null!, Id, "root"));
        Assert.Throws<ArgumentNullException>(() => new TerminalMenuModel(source, null!, "root"));
        Assert.Throws<ArgumentException>(() => new TerminalMenuModel(source, "", "root"));
        Assert.Throws<ArgumentNullException>(() => new TerminalMenuModel(source, Id, null!));
        Assert.Throws<ArgumentException>(() => new TerminalMenuModel(source, Id, ""));
        Assert.Equal(Id, new TerminalMenuModel(source, Id, "root").TerminalId);
    }

    [Fact]
    public void The_selection_scrolls_one_row_at_a_time_and_wraps_back_to_the_top_and_bottom()
    {
        var source = Single("root", "H", "", "", Enumerable.Range(0, 5).Select(Plain).ToArray());
        var model = Ready(source, new TerminalOptions { VisibleRows = 2 });
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        Assert.Equal(2, model.VisibleRows);
        Assert.Equal((0, 0), (model.SelectedIndex, model.ScrollTop));
        var seen = new List<(int Cursor, int Scroll)>();
        for (int i = 0; i < 5; i++)
        {
            model.Handle(MenuInputAction.Down);
            seen.Add((model.SelectedIndex, model.ScrollTop));
        }
        Assert.Equal(new[] { (1, 0), (2, 1), (3, 2), (4, 3), (0, 0) }, seen);

        model.Handle(MenuInputAction.Up);
        Assert.Equal((4, 3), (model.SelectedIndex, model.ScrollTop));
        Assert.Equal(6, sounds.Count(s => s == "select_move"));

        Assert.True(model.Select(2));
        Assert.Equal((2, 2), (model.SelectedIndex, model.ScrollTop));
        Assert.True(model.Select(2));
        Assert.Equal(7, sounds.Count(s => s == "select_move"));
    }

    [Fact]
    public void Returning_to_a_node_restores_the_remembered_row_and_scrolls_it_into_view()
    {
        var source = Single("root", "H", "", "", Enumerable.Range(0, 5).Select(Plain).ToArray());
        var model = Ready(source, new TerminalOptions { VisibleRows = 2 });
        model.Select(4);

        Assert.True(model.Activate(4));
        model.Tick(1);

        Assert.Equal(TerminalPhase.Typing, model.Phase);
        Assert.Equal((4, 3), (model.SelectedIndex, model.ScrollTop));
    }

    [Fact]
    public void Select_is_refused_while_typing_and_for_an_index_outside_the_entries()
    {
        var source = Single("root", "H", "", "", Enumerable.Range(0, 2).Select(Plain).ToArray());
        var model = new TerminalMenuModel(source, Id, "root");

        Assert.False(model.Select(1));
        Assert.False(model.Activate(1));
        model.Handle(MenuInputAction.Accept);
        Assert.False(model.Select(-1));
        Assert.False(model.Select(2));
        Assert.False(model.Activate(2));
        Assert.True(model.Select(1));
    }

    [Fact]
    public void A_blocked_entry_shows_its_option_text_is_not_available_and_never_runs()
    {
        var source = Single("root", "H", "", "",
            FakeTerminalSource.Entry("a", "A", TerminalEntryKind.Redraw, unavailable: ""),
            FakeTerminalSource.Entry("b", "B", TerminalEntryKind.Unsupported),
            FakeTerminalSource.Entry("c", "C", TerminalEntryKind.Submenu),
            FakeTerminalSource.Entry("d", "D", TerminalEntryKind.Submenu, target: ""),
            FakeTerminalSource.Entry("e", "E", TerminalEntryKind.Redraw, unavailable: "Needs key"),
            FakeTerminalSource.Entry("f", "F", TerminalEntryKind.Redraw));
        var model = Ready(source, new TerminalOptions { UnavailableText = "N/A", UnsupportedText = "NOPE" });
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;
        var expected = new[] { "N/A", "NOPE", "N/A", "N/A", "Needs key", "" };

        for (int i = 0; i < expected.Length; i++)
        {
            model.Select(i);
            Assert.Equal(expected[i], model.Prompt);
            Assert.Equal(expected[i].Length == 0, model.IsEntryAvailable(i));
            if (expected[i].Length > 0) Assert.False(model.Activate(i));
        }

        Assert.Empty(source.Runs);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.DoesNotContain("terminal.accept", sounds);
        Assert.False(model.IsEntryAvailable(-1));
        Assert.False(model.IsEntryAvailable(6));
        Assert.Equal("", model.EntryText(6));
        Assert.Equal("", model.EntryText(-1));
    }

    [Fact]
    public void Running_an_entry_plays_the_enter_sound_and_both_hard_drive_sounds_in_that_order()
    {
        var source = Single("root", "H", "", "", Plain(0));
        var model = Ready(source);
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        Assert.True(model.Activate(0));

        Assert.Equal(new[] { "terminal.accept", "store_a", "store_b" }, sounds);
        Assert.Single(source.Runs);
    }

    [Fact]
    public void A_node_with_nothing_to_type_is_ready_at_once_and_has_no_selectable_entry()
    {
        var source = Single("root", "", "", "");
        var model = new TerminalMenuModel(source, Id, "root", new TerminalOptions { DefaultHeader = "" });
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.Equal(("", "", ""), (model.Header, model.Welcome, model.Body));
        Assert.Equal(1, model.PageCount);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.True(model.Handle(MenuInputAction.Down));
        Assert.True(model.Handle(MenuInputAction.Accept));
        Assert.Empty(sounds);
        Assert.Empty(source.Runs);
        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.Equal(TerminalPhase.Closed, model.Phase);
    }

    [Fact]
    public void An_empty_node_header_falls_back_to_the_default_header_option()
    {
        var model = Ready(Single("root", "", "", "body"), new TerminalOptions { DefaultHeader = "FALLBACK" });

        Assert.Equal("FALLBACK", model.Header);
    }

    [Fact]
    public void While_typing_back_finishes_the_text_silently_and_other_actions_are_swallowed()
    {
        var source = Single("root", "HEAD", "", "", Plain(0));
        var model = new TerminalMenuModel(source, Id, "root");
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;

        Assert.True(model.Handle(MenuInputAction.Search));
        Assert.Equal(TerminalPhase.Typing, model.Phase);
        Assert.Empty(sounds);
        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.Equal(new[] { "type_loop_stop" }, sounds);
        Assert.Equal("HEAD", model.Header);
    }

    [Fact]
    public void Actions_the_terminal_does_not_use_are_not_handled_once_ready()
    {
        var model = Ready(Single("root", "H", "", "", Plain(0)));

        Assert.False(model.Handle(MenuInputAction.Search));
        Assert.False(model.Handle(MenuInputAction.Compare));
    }

    [Fact]
    public void Ticks_that_are_not_positive_finite_or_that_type_no_whole_character_raise_nothing()
    {
        var model = new TerminalMenuModel(Single("root", "ROBCO", "", ""), Id, "root");
        var changes = 0;
        model.Changed += () => changes++;

        model.Tick(0);
        model.Tick(-1);
        model.Tick(double.NaN);
        model.Tick(double.PositiveInfinity);
        model.Tick(0.001);
        Assert.Equal(0, changes);
        Assert.Equal("", model.Header);

        model.Tick(1.0 / 60);
        Assert.Equal(1, changes);
        Assert.Equal("R", model.Header);
    }

    [Fact]
    public void A_source_that_stops_being_valid_closes_the_terminal_on_the_next_input_and_later_input_is_refused()
    {
        var source = Single("root", "H", "", "", Plain(0));
        var model = Ready(source);
        var closes = 0;
        model.Closed += () => closes++;
        source.Valid = false;

        Assert.True(model.Handle(MenuInputAction.Up));
        Assert.Equal(TerminalPhase.Closed, model.Phase);
        Assert.False(model.Handle(MenuInputAction.Up));
        model.Tick(1);
        model.Close();
        Assert.Equal(1, closes);
        Assert.Equal("", model.Prompt);
    }

    [Fact]
    public void Back_while_an_entry_runs_closes_only_at_the_top_level()
    {
        var source = Single("root", "H", "", "",
            FakeTerminalSource.Entry("sub", "Sub", target: "sub"),
            Plain(1));
        source.Nodes["sub"] = FakeTerminalSource.Node("sub", "S", "", "", Plain(0));
        var model = Ready(source);

        Assert.True(model.Activate(0));
        model.Tick(1);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(1, model.Depth);
        Assert.True(model.Activate(0));
        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.True(model.IsBusy);
        Assert.Equal(1, model.Depth);

        var top = Ready(Single("root", "H", "", "", Plain(0)));
        var closes = 0;
        top.Closed += () => closes++;
        Assert.True(top.Activate(0));
        Assert.True(top.Handle(MenuInputAction.Back));
        Assert.Equal(TerminalPhase.Closed, top.Phase);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void Paging_hints_appear_only_with_several_pages_and_the_entries_wait_for_the_last_page()
    {
        var source = Single("root", "H", "", "aaaa bbbb cccc dddd eeee", Plain(0));
        var model = Ready(source, new TerminalOptions { Columns = 10, PageLines = 2 });
        var changes = 0;
        model.Changed += () => changes++;

        Assert.Equal(2, model.PageCount);
        Assert.Equal(new[] { "Accept", "Page up", "Page down", "Exit" }, model.Hints.Select(h => h.Label));
        Assert.Equal("aaaa bbbb\ncccc dddd", model.Body);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);

        Assert.True(model.Handle(MenuInputAction.PageUp));
        Assert.Equal(0, model.Page);
        Assert.Equal(0, changes);

        Assert.True(model.Handle(MenuInputAction.PageDown));
        Assert.Equal(1, model.Page);
        Assert.Equal(1, changes);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal("eeee", model.Body);
        Assert.Single(model.Entries);
        Assert.Equal(0, model.SelectedIndex);
        Assert.True(model.Handle(MenuInputAction.Right));
        Assert.Equal(1, model.Page);
    }

    [Fact]
    public void A_text_entry_changes_the_back_hint_even_at_the_top_level_and_accept_on_its_last_page_returns()
    {
        var source = Single("root", "H", "", "",
            FakeTerminalSource.Entry("log", "Log", TerminalEntryKind.Text, page: "Log text"));
        var model = Ready(source);

        Assert.True(model.Activate(0));
        model.Tick(1);
        model.Handle(MenuInputAction.Accept);

        Assert.True(model.IsShowingText);
        Assert.Equal("Back", model.Hints.Single(h => h.Action == MenuInputAction.Back).Label);
        Assert.Empty(model.Entries);
        model.Handle(MenuInputAction.Accept);
        Assert.False(model.IsShowingText);
        Assert.Equal("Exit", model.Hints.Single(h => h.Action == MenuInputAction.Back).Label);
    }
}
