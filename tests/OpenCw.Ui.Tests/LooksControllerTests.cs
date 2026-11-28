using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Looks;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LooksControllerTests
{
    static (LooksController Ctrl, InMemoryLooks Session) Make(LooksContext context = LooksContext.NewCharacter)
    {
        var session = new InMemoryLooks(context);
        var ctrl = LooksController.TryCreate(session);
        Assert.NotNull(ctrl);
        return (ctrl!, session);
    }

    static string[] RowTexts(LooksController ctrl) =>
        Enumerable.Range(0, ctrl.Rows.RowCount).Select(ctrl.Rows.RowText).ToArray();

    static void Press(LooksController ctrl, MenuInputAction action, int times = 1)
    {
        for (var i = 0; i < times; i++)
            ctrl.Handle(action);
    }

    [Fact]
    public void It_lists_each_category_with_its_current_option_then_done()
    {
        var (ctrl, _) = Make();

        Assert.Equal("Looks", ctrl.Title);
        Assert.Equal(LooksController.CategoriesHeader, ctrl.Header);
        Assert.Equal(
            new[] { "Hair style: Style 1", "Hair colour: Style 1", "Facial hair: None", "Eyes: Style 1", "Done" },
            RowTexts(ctrl));
        Assert.False(ctrl.IsDirty);
    }

    [Theory]
    [InlineData(LooksContext.NewCharacter, "Looks")]
    [InlineData(LooksContext.Haircut, "Haircut")]
    [InlineData(LooksContext.BodyMod, "Body")]
    public void The_title_follows_the_context(LooksContext context, string title)
    {
        var (ctrl, _) = Make(context);

        Assert.Equal(title, ctrl.Title);
    }

    [Fact]
    public void Right_and_left_step_the_option_with_wrap_and_preview_it()
    {
        var (ctrl, session) = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Right));

        Assert.Equal(new[] { ("hair_style", (string?)"hair2") }, session.Previews);
        Assert.Equal("Hair style: Style 2", ctrl.Rows.RowText(0));
        Assert.Equal("hair2", ctrl.Current("hair_style"));
        Assert.True(ctrl.IsDirty);

        Press(ctrl, MenuInputAction.Left, 2);

        Assert.Equal("hair4", ctrl.Current("hair_style"));
        Assert.Equal("Hair style: Style 4", ctrl.Rows.RowText(0));
    }

    [Fact]
    public void An_optional_category_steps_through_none()
    {
        var (ctrl, _) = Make();
        Press(ctrl, MenuInputAction.Down, 2);

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal("beard1", ctrl.Current("facial_hair"));

        Press(ctrl, MenuInputAction.Left, 2);

        Assert.Equal("beard3", ctrl.Current("facial_hair"));
    }

    [Fact]
    public void Left_and_right_do_nothing_on_the_done_row()
    {
        var (ctrl, session) = Make();
        Press(ctrl, MenuInputAction.Down, 4);

        Assert.False(ctrl.Handle(MenuInputAction.Right));
        Assert.Empty(session.Previews);
    }

    [Fact]
    public void Opening_a_category_previews_the_cursor_live_and_accept_keeps_it()
    {
        var (ctrl, session) = Make();

        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.InOptions);
        Assert.Equal("Hair style", ctrl.Header);
        Assert.Equal(new[] { "Style 1", "Style 2", "Style 3", "Style 4" }, RowTexts(ctrl));
        Assert.Equal(0, ctrl.Rows.SelectedIndex);
        Assert.Empty(session.Previews);

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(new[] { ("hair_style", (string?)"hair2") }, session.Previews);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(ctrl.InOptions);
        Assert.Equal("Hair style: Style 2", ctrl.Rows.RowText(0));
        Assert.Equal(0, ctrl.Rows.SelectedIndex);
        Assert.True(ctrl.IsDirty);
    }

    [Fact]
    public void Back_in_the_options_restores_the_option_held_on_entry()
    {
        var (ctrl, session) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { ("hair_style", (string?)"hair2"), ("hair_style", (string?)"hair1") }, session.Previews);
        Assert.False(ctrl.InOptions);
        Assert.Equal("hair1", ctrl.Current("hair_style"));
        Assert.False(ctrl.IsDirty);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_refused_revert_on_back_still_returns_to_the_entry_option_and_leaves()
    {
        var (ctrl, session) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);
        Assert.Equal("hair2", ctrl.Current("hair_style"));
        session.RefusedOptions.Add("hair1");

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal("hair1", ctrl.Current("hair_style"));
        Assert.False(ctrl.IsDirty);
        Assert.False(ctrl.InOptions);
        Assert.False(ctrl.IsFinished);
        Assert.Equal("That option cannot be shown.", ctrl.Message);
        Assert.Equal("Hair style: Style 1", ctrl.Rows.RowText(0));
    }

    [Fact]
    public void The_none_row_of_an_optional_category_previews_nothing_selected()
    {
        var (ctrl, session) = Make();
        Press(ctrl, MenuInputAction.Down, 2);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { "None", "Style 1", "Style 2", "Style 3" }, RowTexts(ctrl));
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Up);

        Assert.Equal(("facial_hair", (string?)null), session.Previews[^1]);
        Assert.Null(ctrl.Current("facial_hair"));

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Facial hair: None", ctrl.Rows.RowText(2));
    }

    [Fact]
    public void A_refused_preview_in_the_category_list_keeps_the_old_option_and_shows_the_reason()
    {
        var (ctrl, session) = Make();
        session.RefusedOptions.Add("hair2");

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal("hair1", ctrl.Current("hair_style"));
        Assert.Equal("That option cannot be shown.", ctrl.Message);
        Assert.Empty(session.Previews);
        Assert.False(ctrl.IsDirty);
    }

    [Fact]
    public void A_refused_preview_in_the_options_puts_the_cursor_back()
    {
        var (ctrl, session) = Make();
        session.RefusedOptions.Add("hair2");
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(0, ctrl.Rows.SelectedIndex);
        Assert.Equal("hair1", ctrl.Current("hair_style"));
        Assert.Equal("That option cannot be shown.", ctrl.Message);
        Assert.Empty(session.Previews);
    }

    [Fact]
    public void Done_commits_every_category_choice_once_and_finishes()
    {
        var (ctrl, session) = Make();
        var results = new List<bool>();
        ctrl.Finished += results.Add;
        ctrl.Handle(MenuInputAction.Right);
        Press(ctrl, MenuInputAction.Down, 4);

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(
            new[]
            {
                new LooksChoice("hair_style", "hair2"),
                new LooksChoice("hair_color", "tint1"),
                new LooksChoice("facial_hair", null),
                new LooksChoice("eyes", "eyes1"),
            },
            session.Committed);
        Assert.Equal(new[] { true }, results);
        Assert.True(ctrl.IsConfirmed);
        Assert.Equal(0, session.Cancels);
    }

    [Fact]
    public void Back_with_changes_asks_first_and_a_second_back_discards_and_reverts_the_preview()
    {
        var (ctrl, session) = Make();
        var results = new List<bool>();
        ctrl.Finished += results.Add;
        ctrl.Handle(MenuInputAction.Right);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Empty(results);
        Assert.Equal(LooksController.DiscardText, ctrl.Message);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { false }, results);
        Assert.Equal(("hair_style", (string?)"hair1"), session.Previews[^1]);
        Assert.Equal(1, session.Cancels);
        Assert.Empty(session.Committed);
    }

    [Fact]
    public void Another_input_clears_the_discard_warning()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Back);

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(string.Empty, ctrl.Message);

        ctrl.Handle(MenuInputAction.Back);

        Assert.False(ctrl.IsFinished);
        Assert.Equal(LooksController.DiscardText, ctrl.Message);
    }

    [Fact]
    public void Back_with_no_changes_closes_at_once_and_tells_the_session()
    {
        var (ctrl, session) = Make();
        var results = new List<bool>();
        ctrl.Finished += results.Add;

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { false }, results);
        Assert.Equal(1, session.Cancels);
        Assert.Empty(session.Previews);
    }

    [Fact]
    public void Putting_every_option_back_clears_the_changed_state()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Right);
        Assert.True(ctrl.IsDirty);

        ctrl.Handle(MenuInputAction.Left);

        Assert.False(ctrl.IsDirty);
        Assert.DoesNotContain(new MenuTextLine(LooksController.ChangedText, LineTone.Good), ctrl.DetailLines);
    }

    [Fact]
    public void The_detail_pane_shows_the_current_option_its_position_and_description()
    {
        var (ctrl, _) = Make();

        Assert.Equal(
            new[]
            {
                new MenuTextLine("Hair style", LineTone.Heading),
                new MenuTextLine("Current: Style 1 (1 / 4)"),
                new MenuTextLine("Sample description 1.", LineTone.Muted),
            },
            ctrl.DetailLines);

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(
            new[]
            {
                new MenuTextLine("Hair style", LineTone.Heading),
                new MenuTextLine("Current: Style 2 (2 / 4)"),
                new MenuTextLine("Sample description 2.", LineTone.Muted),
                new MenuTextLine(LooksController.ChangedText, LineTone.Good),
            },
            ctrl.DetailLines);
    }

    [Fact]
    public void Left_and_right_are_not_handled_while_choosing_an_option()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(ctrl.Handle(MenuInputAction.Right));
        Assert.Equal(
            new[] { MenuInputAction.Accept, MenuInputAction.Back },
            ctrl.Hints.Select(hint => hint.Action));
    }

    [Fact]
    public void Options_without_names_or_ids_are_skipped_and_empty_categories_hidden()
    {
        var categories = new[]
        {
            new LooksCategory("a", "Alpha", new[] { new LooksOption("x", string.Empty), new LooksOption("y", "Why") }, "y"),
            new LooksCategory("b", "Beta", Array.Empty<LooksOption>(), null),
            new LooksCategory("c", "Gamma", new[] { new LooksOption(string.Empty, "Nameless id") }, null),
        };

        var ctrl = LooksController.TryCreate(new InMemoryLooks(LooksContext.NewCharacter, categories));

        Assert.NotNull(ctrl);
        Assert.Equal(new[] { "a" }, ctrl!.Categories.Select(category => category.Id));
        Assert.Equal(new[] { "y" }, ctrl.Categories[0].Options.Select(option => option.Id));
        Assert.Equal(new[] { "Alpha: Why", "Done" }, RowTexts(ctrl));
        Assert.Null(LooksController.TryCreate(new InMemoryLooks(LooksContext.NewCharacter, categories.Skip(1))));
    }

    [Fact]
    public void A_missing_or_invalid_session_cannot_open_and_the_menu_closes_when_it_goes_invalid()
    {
        Assert.Null(LooksController.TryCreate(null));
        Assert.Null(LooksController.TryCreate(new InMemoryLooks { IsValid = false }));

        var session = new InMemoryLooks();
        var ctrl = LooksController.TryCreate(session)!;
        session.IsValid = false;

        ctrl.Tick(0.1);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.IsConfirmed);
    }
}
