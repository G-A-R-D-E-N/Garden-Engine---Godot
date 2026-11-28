using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuModelPromptTests
{
    sealed class NullReasonGate : ICanSaveQuery
    {
        public bool CanSave(out string reason)
        {
            reason = null!;
            return false;
        }
    }

    sealed class SilentFailWriter : ISaveWriter
    {
        public ActionResult SaveNew() => new(false);

        public ActionResult Overwrite(string id) => new(false);

        public ActionResult Quicksave() => new(false);
    }

    sealed class BlankMessageWriter : ISaveWriter
    {
        public ActionResult SaveNew() => throw new InvalidOperationException("");

        public ActionResult Overwrite(string id) => throw new InvalidOperationException("");

        public ActionResult Quicksave() => throw new InvalidOperationException("");
    }

    static SaveSlotInfo Slot(string id, int day, SaveKind kind = SaveKind.Manual) =>
        new(id, id, "Ann", 5, TimeSpan.FromHours(1), "Somewhere", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "", kind);

    static MainMenuModel Title(params SaveSlotInfo[] slots) => new(new InMemorySaveStore(slots));

    static MainMenuModel Pause(SettableCanSave gate, params SaveSlotInfo[] slots)
    {
        var store = new InMemorySaveStore(slots);
        return new MainMenuModel(store, null, false, MainMenuMode.Pause, gate, store);
    }

    static SettableCanSave Blocked() => new() { Allowed = false, Reason = "Not now." };

    static MainMenuAction[] Actions(MainMenuModel model) => model.Entries.Select(e => e.Action).ToArray();

    static MainMenuEntry Entry(MainMenuModel model, MainMenuAction action) => model.Entries.Single(e => e.Action == action);

    [Fact]
    public void The_title_prompts_name_the_newest_save_and_only_ask_before_new_game_when_saves_exist()
    {
        var empty = Title();
        Assert.False(Entry(empty, MainMenuAction.NewGame).NeedsConfirmation);
        Assert.Equal("Start a new game? Your saved games are kept.", Entry(empty, MainMenuAction.NewGame).ConfirmPrompt);
        Assert.False(Entry(empty, MainMenuAction.Continue).NeedsConfirmation);
        Assert.Equal("", Entry(empty, MainMenuAction.Continue).ConfirmPrompt);

        var model = Title(Slot("a", 2), Slot("b", 5));
        Assert.True(Entry(model, MainMenuAction.NewGame).NeedsConfirmation);
        Assert.True(Entry(model, MainMenuAction.Continue).NeedsConfirmation);
        Assert.Equal("Continue from \"b\"? Level 5, Somewhere, 1h 00m", Entry(model, MainMenuAction.Continue).ConfirmPrompt);
        Assert.False(Entry(model, MainMenuAction.Load).NeedsConfirmation);
        Assert.False(Entry(model, MainMenuAction.Settings).NeedsConfirmation);
        Assert.Equal("Quit to the desktop?", Entry(model, MainMenuAction.Quit).ConfirmPrompt);
    }

    [Fact]
    public void The_pause_exit_prompt_is_worded_for_the_title_screen_and_labelled_exit()
    {
        var exit = Entry(Pause(new SettableCanSave()), MainMenuAction.Quit);

        Assert.Equal("Exit", exit.Label);
        Assert.True(exit.NeedsConfirmation);
        Assert.Equal("Exit to the title screen?", exit.ConfirmPrompt);
    }

    [Fact]
    public void Extras_then_credits_sit_between_settings_and_quit_and_credits_alone_is_optional()
    {
        Assert.Equal(
            new[]
            {
                MainMenuAction.NewGame, MainMenuAction.Continue, MainMenuAction.Load, MainMenuAction.Settings, MainMenuAction.Extras,
                MainMenuAction.Credits, MainMenuAction.Quit,
            },
            Actions(new MainMenuModel(new InMemorySaveStore(), "Extras", true)));
        Assert.Equal(
            new[]
            {
                MainMenuAction.NewGame, MainMenuAction.Continue, MainMenuAction.Load, MainMenuAction.Settings, MainMenuAction.Credits,
                MainMenuAction.Quit,
            },
            Actions(new MainMenuModel(new InMemorySaveStore(), null, true)));
        Assert.Equal("Credits", Entry(new MainMenuModel(new InMemorySaveStore(), null, true), MainMenuAction.Credits).Label);
    }

    [Fact]
    public void An_empty_extras_label_still_adds_the_entry_with_that_label()
    {
        var model = new MainMenuModel(new InMemorySaveStore(), "");

        Assert.Equal("", Entry(model, MainMenuAction.Extras).Label);
        Assert.True(Entry(model, MainMenuAction.Extras).Enabled);
    }

    [Fact]
    public void Two_saves_with_the_same_timestamp_continue_from_the_one_listed_first()
    {
        var model = Title(Slot("first", 4), Slot("second", 4));

        Assert.Equal("first", model.LastSave!.Id);
    }

    [Fact]
    public void Home_and_page_up_select_the_first_enabled_entry_and_end_and_page_down_the_last()
    {
        var model = Title(Slot("a", 2));
        Assert.Equal(MainMenuAction.Continue, model.Selected.Action);

        model.Move(CursorMove.Home);
        Assert.Equal(0, model.SelectedIndex);
        model.Move(CursorMove.End);
        Assert.Equal(4, model.SelectedIndex);
        model.Move(CursorMove.PageUp);
        Assert.Equal(0, model.SelectedIndex);
        model.Move(CursorMove.PageDown);
        Assert.Equal(4, model.SelectedIndex);
    }

    [Fact]
    public void Home_and_page_up_skip_disabled_leading_entries_in_the_pause_menu()
    {
        var model = Pause(Blocked());
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);

        model.Move(CursorMove.End);
        Assert.Equal(MainMenuAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Home);
        Assert.Equal(3, model.SelectedIndex);
        model.Move(CursorMove.End);
        model.Move(CursorMove.PageUp);
        Assert.Equal(3, model.SelectedIndex);
    }

    [Fact]
    public void Up_from_the_first_enabled_entry_wraps_to_the_last_and_down_wraps_back_over_the_disabled_ones()
    {
        var model = Pause(Blocked());
        Assert.Equal(3, model.SelectedIndex);

        model.Move(CursorMove.Up);
        Assert.Equal(4, model.SelectedIndex);
        model.Move(CursorMove.Down);
        Assert.Equal(3, model.SelectedIndex);
    }

    [Fact]
    public void Next_category_changes_nothing_but_still_clears_the_notice()
    {
        var model = Pause(Blocked());
        model.Quicksave();
        Assert.Equal("Not now.", model.Notice);
        var before = model.SelectedIndex;

        model.Move(CursorMove.NextCategory);

        Assert.Equal(before, model.SelectedIndex);
        Assert.Equal("", model.Notice);
    }

    [Fact]
    public void Selecting_clears_the_notice_even_when_the_entry_is_disabled_and_refused()
    {
        var model = Pause(Blocked());
        model.Quicksave();
        Assert.Equal("Not now.", model.Notice);

        Assert.False(model.Select(MainMenuAction.Quicksave));

        Assert.Equal("", model.Notice);
        Assert.Equal(3, model.SelectedIndex);
        model.Quicksave();
        Assert.True(model.Select(MainMenuAction.Quit));
        Assert.Equal("", model.Notice);
        Assert.Equal(4, model.SelectedIndex);
    }

    [Fact]
    public void An_action_the_menu_does_not_list_cannot_be_selected()
    {
        var model = Title(Slot("a", 2));

        Assert.False(model.Select(MainMenuAction.Save));
        Assert.False(model.Select(MainMenuAction.Extras));
        Assert.Equal(MainMenuAction.Continue, model.Selected.Action);
    }

    [Fact]
    public void Activating_returns_the_selected_entry()
    {
        var model = Title(Slot("a", 2));
        model.Select(MainMenuAction.Load);

        Assert.Same(Entry(model, MainMenuAction.Load), model.Activate());
    }

    [Fact]
    public void A_gate_that_gives_no_reason_leaves_the_disabled_reason_empty_and_quicksave_uses_the_standard_sentence()
    {
        var store = new InMemorySaveStore();
        var model = new MainMenuModel(store, null, false, MainMenuMode.Pause, new NullReasonGate(), store);

        Assert.Equal("", Entry(model, MainMenuAction.Save).DisabledReason);
        Assert.Equal("", Entry(model, MainMenuAction.Quicksave).DisabledReason);

        var result = model.Quicksave();

        Assert.Equal(new ActionResult(false, SaveListController.CannotSaveText), result);
        Assert.Equal(SaveListController.CannotSaveText, model.Notice);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void Quicksave_with_a_writer_but_no_save_query_is_refused_with_the_standard_sentence()
    {
        var store = new InMemorySaveStore();
        var model = new MainMenuModel(store, null, false, MainMenuMode.Pause, null, store);

        var result = model.Quicksave();

        Assert.Equal(new ActionResult(false, SaveListController.CannotSaveText), result);
        Assert.Equal(SaveListController.CannotSaveText, model.Notice);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void A_writer_failing_without_a_reason_shows_the_generic_failure_but_returns_the_bare_result()
    {
        var model = new MainMenuModel(new InMemorySaveStore(), null, false, MainMenuMode.Pause, new SettableCanSave(), new SilentFailWriter());

        var result = model.Quicksave();

        Assert.Equal(new ActionResult(false), result);
        Assert.Equal(SaveListController.FailedText, model.Notice);
    }

    [Fact]
    public void A_writer_that_throws_with_a_blank_message_shows_the_generic_failure()
    {
        var model = new MainMenuModel(new InMemorySaveStore(), null, false, MainMenuMode.Pause, new SettableCanSave(), new BlankMessageWriter());

        var result = model.Quicksave();

        Assert.False(result.Ok);
        Assert.Equal("", result.Reason);
        Assert.Equal(SaveListController.FailedText, model.Notice);
    }

    [Fact]
    public void A_successful_quicksave_enables_load_and_makes_the_new_save_the_latest()
    {
        var store = new InMemorySaveStore();
        var model = new MainMenuModel(store, null, false, MainMenuMode.Pause, new SettableCanSave(), store);
        Assert.False(Entry(model, MainMenuAction.Load).Enabled);
        Assert.Null(model.LastSave);

        Assert.True(model.Quicksave().Ok);

        Assert.True(Entry(model, MainMenuAction.Load).Enabled);
        Assert.Equal(InMemorySaveStore.QuicksaveId, model.LastSave!.Id);
        Assert.Equal("", model.Notice);
        Assert.Equal(MainMenuAction.Save, model.Selected.Action);
    }

    [Fact]
    public void Refresh_falls_back_to_the_first_enabled_entry_when_the_selected_one_becomes_disabled()
    {
        var gate = new SettableCanSave();
        var model = Pause(gate);
        Assert.Equal(MainMenuAction.Save, model.Selected.Action);

        gate.Allowed = false;
        gate.Reason = "Not now.";
        model.Refresh();

        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
        Assert.Equal("Not now.", Entry(model, MainMenuAction.Save).DisabledReason);
    }
}
