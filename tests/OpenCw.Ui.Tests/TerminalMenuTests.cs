using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalMenuTests
{
    const string Id = "term1";

    static FakeTerminalSource Source()
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "ROBCO", "WELCOME USER", "Select an entry.",
            FakeTerminalSource.Entry("notes", "Notes", target: "notes"),
            FakeTerminalSource.Entry("log", "Read log", TerminalEntryKind.Text, page: "Log text", response: "Opening"),
            FakeTerminalSource.Entry("reset", "Reset", TerminalEntryKind.ReturnToTop),
            FakeTerminalSource.Entry("locked", "Locked", target: "notes", unavailable: "Needs access"),
            FakeTerminalSource.Entry("image", "Img", TerminalEntryKind.Image),
            FakeTerminalSource.Entry("weird", "Weird", TerminalEntryKind.Unsupported),
            FakeTerminalSource.Entry("slow", "Slow", TerminalEntryKind.Redraw, response: new string('x', 400)));
        source.Nodes["notes"] = FakeTerminalSource.Node("notes", "NOTES", "WELCOME USER", "Notes body",
            FakeTerminalSource.Entry("top", "Back to top", TerminalEntryKind.ReturnToTop),
            FakeTerminalSource.Entry("again", "Again", TerminalEntryKind.Redraw));
        return source;
    }

    static TerminalMenuModel Ready(FakeTerminalSource source, TerminalOptions? options = null)
    {
        var model = new TerminalMenuModel(source, Id, "root", options);
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        return model;
    }

    static void Run(TerminalMenuModel model, int index)
    {
        Assert.True(model.Activate(index));
        Assert.True(model.IsBusy);
        model.Tick(1);
        Assert.False(model.IsBusy);
    }

    static void Skip(TerminalMenuModel model)
    {
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
    }

    static int Revealed(TerminalMenuModel model) =>
        model.Header.Length + model.Welcome.Length + model.Body.Length
        + Enumerable.Range(0, model.Entries.Count).Sum(i => model.EntryText(i).Length);

    [Theory]
    [InlineData(30)]
    [InlineData(144)]
    public void Typing_reveals_whole_characters_at_the_configured_rate_for_any_frame_rate(int fps)
    {
        var model = new TerminalMenuModel(Source(), Id, "root");

        for (int i = 0; i < fps; i++)
            model.Tick(1.0 / fps);

        Assert.Equal(TerminalPhase.Typing, model.Phase);
        Assert.Equal(60, Revealed(model));
    }

    [Fact]
    public void Accept_while_typing_shows_everything_and_does_not_run_an_entry()
    {
        var source = Source();
        var model = new TerminalMenuModel(source, Id, "root");

        model.Handle(MenuInputAction.Accept);

        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.Equal("ROBCO", model.Header);
        Assert.Equal("Notes", model.EntryText(0));
        Assert.Empty(source.Runs);
    }

    [Fact]
    public void Submenu_entry_runs_through_the_source_then_pushes_and_back_pops()
    {
        var source = Source();
        var model = Ready(source);

        Run(model, 0);

        Assert.Equal(new[] { "root/notes" }, source.Runs);
        Assert.Equal("notes", model.NodeId);
        Assert.Equal(1, model.Depth);
        Skip(model);
        Assert.Equal("NOTES", model.Header);

        model.Handle(MenuInputAction.Back);

        Assert.Equal("root", model.NodeId);
        Assert.Equal(0, model.Depth);
    }

    [Fact]
    public void Selection_is_remembered_per_node_across_visits()
    {
        var model = Ready(Source());
        Run(model, 0);
        Skip(model);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(1, model.SelectedIndex);

        model.Handle(MenuInputAction.Back);
        Skip(model);
        Assert.Equal(0, model.SelectedIndex);
        Run(model, 0);
        Skip(model);

        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void Return_to_top_clears_the_whole_stack()
    {
        var model = Ready(Source());
        Run(model, 0);
        Skip(model);
        Assert.Equal(1, model.Depth);

        Run(model, 0);

        Assert.Equal("root", model.NodeId);
        Assert.Equal(0, model.Depth);
    }

    [Fact]
    public void Cursor_wraps_in_both_directions()
    {
        var model = Ready(Source());

        model.Handle(MenuInputAction.Up);
        Assert.Equal(6, model.SelectedIndex);

        model.Handle(MenuInputAction.Down);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void Text_entry_shows_its_page_and_accept_or_back_returns_to_the_node()
    {
        var model = Ready(Source());
        model.Select(1);

        Run(model, 1);
        Skip(model);

        Assert.True(model.IsShowingText);
        Assert.Equal("Log text", model.Body);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);

        model.Handle(MenuInputAction.Accept);

        Assert.False(model.IsShowingText);
        Assert.Equal("root", model.NodeId);

        Skip(model);
        Run(model, 1);
        Skip(model);
        model.Handle(MenuInputAction.Back);
        Assert.False(model.IsShowingText);
        Assert.Equal(7, model.Entries.Count);
    }

    [Fact]
    public void Busy_hold_waits_for_the_minimum_even_when_the_response_is_typed()
    {
        var model = Ready(Source());
        model.Select(2);

        Assert.True(model.Activate(2));
        model.Tick(0.49);
        Assert.True(model.IsBusy);

        model.Tick(0.02);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public void Busy_hold_ends_at_the_maximum_when_the_response_is_still_typing()
    {
        var source = Source();
        var model = Ready(source);
        model.Select(6);

        Assert.True(model.Activate(6));
        model.Tick(1);
        Assert.Equal(new string('x', 60), model.Prompt);
        model.Tick(3.9);
        Assert.True(model.IsBusy);
        model.Tick(0.2);

        Assert.False(model.IsBusy);
        Assert.Equal(TerminalPhase.Typing, model.Phase);
    }

    [Fact]
    public void Input_is_ignored_while_busy_so_an_entry_runs_once()
    {
        var source = Source();
        var model = Ready(source);
        model.Select(6);
        model.Activate(6);

        model.Handle(MenuInputAction.Accept);
        model.Handle(MenuInputAction.Down);
        model.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "root/slow" }, source.Runs);
        Assert.Equal(6, model.SelectedIndex);
    }

    [Fact]
    public void Unavailable_entry_shows_its_reason_and_never_reaches_the_source()
    {
        var source = Source();
        var model = Ready(source);

        model.Select(3);

        Assert.Equal("Needs access", model.Prompt);
        Assert.False(model.IsEntryAvailable(3));
        Assert.False(model.Activate(3));
        model.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalPhase.Ready, model.Phase);
        Assert.Empty(source.Runs);

        model.Select(5);
        Assert.Equal("Unsupported action", model.Prompt);
        Assert.False(model.Activate(5));
        Assert.True(model.IsEntryAvailable(0));
    }

    [Fact]
    public void Image_entry_shows_the_fallback_text()
    {
        var model = Ready(Source());
        model.Select(4);

        Run(model, 4);
        Skip(model);

        Assert.True(model.IsShowingText);
        Assert.Equal(new TerminalOptions().ImageText, model.Body.Replace("\n", " "));
    }

    [Fact]
    public void Long_body_is_paged_and_entries_appear_only_on_the_last_page()
    {
        var source = new FakeTerminalSource();
        string body = string.Join('\n', Enumerable.Range(1, 30).Select(n => "line " + n));
        source.Nodes["root"] = FakeTerminalSource.Node("root", "H", "W", body,
            FakeTerminalSource.Entry("a", "A", TerminalEntryKind.Redraw));
        var model = Ready(source);

        Assert.Equal(3, model.PageCount);
        Assert.Equal(0, model.Page);
        Assert.Empty(model.Entries);

        model.Handle(MenuInputAction.Accept);
        Skip(model);
        Assert.Equal(1, model.Page);
        Assert.Empty(model.Entries);

        model.Handle(MenuInputAction.PageDown);
        Skip(model);
        Assert.Equal(2, model.Page);
        Assert.Single(model.Entries);

        model.Handle(MenuInputAction.PageUp);
        Assert.Equal(1, model.Page);
    }

    [Fact]
    public void Back_at_depth_zero_closes_exactly_once()
    {
        var model = Ready(Source());
        int closed = 0;
        model.Closed += () => closed++;

        model.Handle(MenuInputAction.Back);
        model.Handle(MenuInputAction.Back);

        Assert.Equal(TerminalPhase.Closed, model.Phase);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_closed_terminal_shows_no_entries()
    {
        var model = Ready(Source());
        Assert.True(model.EntriesVisible);

        model.Handle(MenuInputAction.Back);

        Assert.Equal(TerminalPhase.Closed, model.Phase);
        Assert.False(model.EntriesVisible);
        Assert.Empty(model.Entries);
        Assert.Equal(-1, model.SelectedIndex);
    }

    [Fact]
    public void Missing_node_closes_the_terminal()
    {
        var source = Source();
        source.Nodes.Remove("notes");
        var model = Ready(source);
        model.Select(0);
        model.Activate(0);

        model.Tick(1);

        Assert.Equal(TerminalPhase.Closed, model.Phase);
    }

    [Fact]
    public void Terminal_closes_on_the_next_tick_once_the_source_is_no_longer_valid()
    {
        var source = Source();
        var model = Ready(source);

        source.Valid = false;
        model.Tick(0.1);

        Assert.Equal(TerminalPhase.Closed, model.Phase);
    }

    [Fact]
    public void Hints_reflect_depth_and_paging()
    {
        var model = Ready(Source());
        Assert.Equal("Exit", model.Hints.Single(h => h.Action == MenuInputAction.Back).Label);

        Run(model, 0);
        Skip(model);

        Assert.Equal("Back", model.Hints.Single(h => h.Action == MenuInputAction.Back).Label);
    }
}
