using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Looks;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class LooksControllerEdgeTests
{
    static LooksCategory Cat(string id, string name, string? selected, bool optional = false, params string[] options) =>
        new(id, name, options.Select(option => new LooksOption(option, "Opt " + option)).ToArray(), selected, optional);

    static (LooksController Ctrl, InMemoryLooks Session) Make(params LooksCategory[] categories)
    {
        var session = categories.Length == 0 ? new InMemoryLooks() : new InMemoryLooks(LooksContext.NewCharacter, categories);
        var ctrl = LooksController.TryCreate(session);
        Assert.NotNull(ctrl);
        return (ctrl!, session);
    }

    static string[] RowTexts(LooksController ctrl) =>
        Enumerable.Range(0, ctrl.Rows.RowCount).Select(ctrl.Rows.RowText).ToArray();

    [Fact]
    public void A_second_category_with_the_same_id_is_dropped_and_the_first_one_wins()
    {
        var (ctrl, _) = Make(Cat("a", "First", "x", false, "x"), Cat("a", "Second", "y", false, "y"));

        var only = Assert.Single(ctrl.Categories);

        Assert.Equal("First", only.Name);
        Assert.Equal(new[] { "x" }, only.Options.Select(option => option.Id));
        Assert.Equal(new[] { "First: Opt x", "Done" }, RowTexts(ctrl));
    }

    [Fact]
    public void Missing_blank_or_optionless_categories_and_options_are_cleaned_out()
    {
        var categories = new LooksCategory[]
        {
            null!,
            Cat("", "Blank id", null, false, "x"),
            Cat("b", "  ", null, false, "x"),
            new("c", "Cee", null!, null),
            new("d", "Dee", new LooksOption[] { null!, new("x", "Ex"), new("  ", "Blank"), new("y", "") }, "x"),
        };

        var (ctrl, _) = Make(categories);

        var only = Assert.Single(ctrl.Categories);
        Assert.Equal("d", only.Id);
        Assert.Equal(new[] { "x" }, only.Options.Select(option => option.Id));
        Assert.Equal(new[] { "Dee: Ex", "Done" }, RowTexts(ctrl));
    }

    [Fact]
    public void An_unknown_category_has_no_current_option()
    {
        var (ctrl, _) = Make();

        Assert.Null(ctrl.Current("nope"));
        Assert.Equal("hair1", ctrl.Current("hair_style"));
    }

    [Fact]
    public void The_hints_are_select_previous_next_close_on_the_list_and_keep_cancel_on_the_options()
    {
        var (ctrl, _) = Make();

        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Select"),
                new MenuHint(MenuInputAction.Left, "Previous"),
                new MenuHint(MenuInputAction.Right, "Next"),
                new MenuHint(MenuInputAction.Back, "Close"),
            },
            ctrl.Hints);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Keep"), new MenuHint(MenuInputAction.Back, "Cancel") },
            ctrl.Hints);
    }

    [Fact]
    public void The_done_row_explains_itself_and_still_shows_the_changed_line_when_dirty()
    {
        var (ctrl, _) = Make(Cat("a", "Alpha", "x", false, "x", "y"));
        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(new[] { new MenuTextLine(LooksController.DoneHelp, LineTone.Muted) }, ctrl.DetailLines);
        Assert.Equal("Keep these looks and close.", LooksController.DoneHelp);

        ctrl.Handle(MenuInputAction.Up);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(
            new[] { new MenuTextLine(LooksController.DoneHelp, LineTone.Muted), new MenuTextLine("Changed", LineTone.Good) },
            ctrl.DetailLines);
    }

    [Fact]
    public void A_selection_that_is_not_among_the_options_reads_as_none_at_position_zero_and_right_goes_to_the_first()
    {
        var (ctrl, session) = Make(Cat("a", "Alpha", "zzz", false, "x", "y"));

        Assert.Equal("Alpha: None", ctrl.Rows.RowText(0));
        Assert.Equal(
            new[] { new MenuTextLine("Alpha", LineTone.Heading), new MenuTextLine("Current: None (0 / 2)") },
            ctrl.DetailLines);
        Assert.False(ctrl.IsDirty);

        Assert.True(ctrl.Handle(MenuInputAction.Right));

        Assert.Equal(new[] { ("a", (string?)"x") }, session.Previews);
        Assert.Equal(
            new[]
            {
                new MenuTextLine("Alpha", LineTone.Heading),
                new MenuTextLine("Current: Opt x (1 / 2)"),
                new MenuTextLine(LooksController.ChangedText, LineTone.Good),
            },
            ctrl.DetailLines);
    }

    [Fact]
    public void A_selection_that_is_not_among_the_options_goes_to_the_last_one_on_left()
    {
        var (ctrl, session) = Make(Cat("a", "Alpha", "zzz", false, "x", "y"));

        ctrl.Handle(MenuInputAction.Left);

        Assert.Equal(new[] { ("a", (string?)"y") }, session.Previews);
        Assert.Equal("Current: Opt y (2 / 2)", ctrl.DetailLines[1].Text);
    }

    [Fact]
    public void No_selection_in_a_required_category_is_position_zero_and_in_an_optional_one_position_one()
    {
        var (required, _) = Make(Cat("a", "Alpha", null, false, "x", "y"));
        var (optional, _) = Make(Cat("a", "Alpha", null, true, "x", "y"));

        Assert.Equal("Current: None (0 / 2)", required.DetailLines[1].Text);
        Assert.Equal("Current: None (1 / 3)", optional.DetailLines[1].Text);
    }

    [Fact]
    public void A_category_with_one_option_previews_it_again_on_step_without_becoming_dirty()
    {
        var (ctrl, session) = Make(Cat("a", "Alpha", "x", false, "x"));

        Assert.True(ctrl.Handle(MenuInputAction.Right));

        Assert.Equal(new[] { ("a", (string?)"x") }, session.Previews);
        Assert.False(ctrl.IsDirty);
        Assert.Equal("x", ctrl.Current("a"));
    }

    [Fact]
    public void An_option_with_no_description_shows_no_description_line_and_the_options_pane_says_previewing()
    {
        var (ctrl, _) = Make(Cat("a", "Alpha", "x", false, "x", "y"));
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(
            new[]
            {
                new MenuTextLine("Alpha", LineTone.Heading),
                new MenuTextLine("Previewing: Opt y (2 / 2)"),
                new MenuTextLine(LooksController.ChangedText, LineTone.Good),
            },
            ctrl.DetailLines);
    }

    [Fact]
    public void Keeping_the_option_you_opened_closes_the_options_without_a_preview_and_with_the_confirm_sound()
    {
        var (ctrl, session) = Make();
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(ctrl.InOptions);
        Assert.Empty(session.Previews);
        Assert.Equal(new[] { "menu.confirm" }, sounds);
        Assert.False(ctrl.IsDirty);
    }

    [Fact]
    public void Leaving_the_options_without_moving_makes_no_preview_call()
    {
        var (ctrl, session) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Empty(session.Previews);
        Assert.False(ctrl.InOptions);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Choosing_none_in_an_optional_category_that_had_an_option_previews_null_and_marks_it_changed()
    {
        var (ctrl, session) = Make(Cat("c", "Cee", "x", true, "x", "y"));
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(1, ctrl.Rows.SelectedIndex);

        ctrl.Handle(MenuInputAction.Up);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { ("c", (string?)null) }, session.Previews);
        Assert.Null(ctrl.Current("c"));
        Assert.True(ctrl.IsDirty);
        Assert.Equal("Cee: None", ctrl.Rows.RowText(0));
    }

    [Fact]
    public void Discarding_reverts_every_changed_category_in_list_order_and_plays_the_blocked_sound_once()
    {
        var (ctrl, session) = Make();
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Right);

        ctrl.Handle(MenuInputAction.Back);
        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(
            new[]
            {
                ("hair_style", (string?)"hair2"),
                ("hair_color", (string?)"tint2"),
                ("hair_style", (string?)"hair1"),
                ("hair_color", (string?)"tint1"),
            },
            session.Previews);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
        Assert.Equal(1, session.Cancels);
    }

    [Fact]
    public void Nothing_is_handled_after_the_menu_has_finished()
    {
        var (ctrl, session) = Make(Cat("a", "Alpha", "x", false, "x", "y"));
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.True(ctrl.IsFinished);

        Assert.False(ctrl.Handle(MenuInputAction.Back));
        Assert.False(ctrl.Handle(MenuInputAction.Right));
        Assert.False(ctrl.Handle(MenuInputAction.Accept));

        Assert.Equal(0, session.Cancels);
        Assert.Equal(new[] { new LooksChoice("a", "x") }, session.Committed);
    }

    [Fact]
    public void A_valid_session_keeps_the_menu_open_on_tick()
    {
        var (ctrl, _) = Make();

        ctrl.Tick(10.0);

        Assert.False(ctrl.IsFinished);
    }
}
