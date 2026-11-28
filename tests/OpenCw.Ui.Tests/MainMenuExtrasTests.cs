using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuExtrasTests
{
    sealed class ThrowingWriter : ISaveWriter
    {
        public ActionResult SaveNew() => throw new InvalidOperationException("boom");

        public ActionResult Overwrite(string id) => throw new InvalidOperationException("boom");

        public ActionResult Quicksave() => throw new InvalidOperationException("boom");
    }

    static MainMenuModel Pause(InMemorySaveStore store, SettableCanSave gate, ISaveWriter? writer = null) =>
        new(store, null, false, MainMenuMode.Pause, gate, writer ?? store);

    static SaveSlotInfo Slot(string id, int day) =>
        new(id, id, "Ann", 5, TimeSpan.FromHours(1), "Somewhere", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "");

    static MainMenuAction[] Actions(MainMenuModel model) => model.Entries.Select(e => e.Action).ToArray();

    static InMemorySaveStore Saves(params SaveSlotInfo[] slots) => new(slots);

    [Fact]
    public void The_title_menu_has_no_credits_row_unless_asked()
    {
        var model = new MainMenuModel(Saves());

        Assert.DoesNotContain(MainMenuAction.Credits, Actions(model));
        Assert.Equal(MainMenuMode.Title, model.Mode);
    }

    [Fact]
    public void The_credits_row_sits_before_quit_and_is_always_enabled()
    {
        var model = new MainMenuModel(Saves(), null, true);

        Assert.Equal(
            new[]
            {
                MainMenuAction.NewGame, MainMenuAction.Continue, MainMenuAction.Load, MainMenuAction.Settings,
                MainMenuAction.Credits, MainMenuAction.Quit,
            },
            Actions(model));
        var credits = model.Entries.Single(e => e.Action == MainMenuAction.Credits);
        Assert.Equal("Credits", credits.Label);
        Assert.True(credits.Enabled);
        Assert.False(credits.NeedsConfirmation);
    }

    [Fact]
    public void The_credits_row_follows_the_extras_row()
    {
        var model = new MainMenuModel(Saves(), "Mods", true);

        Assert.Equal(MainMenuAction.Extras, model.Entries[^3].Action);
        Assert.Equal(MainMenuAction.Credits, model.Entries[^2].Action);
        Assert.Equal(MainMenuAction.Quit, model.Entries[^1].Action);
    }

    [Fact]
    public void Credits_can_be_selected_and_activated()
    {
        var model = new MainMenuModel(Saves(), null, true);

        Assert.True(model.Select(MainMenuAction.Credits));
        Assert.Equal(MainMenuAction.Credits, model.Activate()?.Action);
    }

    [Fact]
    public void The_pause_menu_lists_quicksave_save_load_settings_and_exit()
    {
        var model = new MainMenuModel(Saves(Slot("a", 2)), null, false, MainMenuMode.Pause, new SettableCanSave());

        Assert.Equal(
            new[]
            {
                MainMenuAction.Quicksave, MainMenuAction.Save, MainMenuAction.Load, MainMenuAction.Settings,
                MainMenuAction.Quit,
            },
            Actions(model));
        Assert.Equal(new[] { "Quicksave", "Save", "Load", "Settings", "Exit" }, model.Entries.Select(e => e.Label).ToArray());
        Assert.Equal(MainMenuMode.Pause, model.Mode);
    }

    [Fact]
    public void The_pause_menu_selects_save_first_when_saving_is_allowed()
    {
        var model = new MainMenuModel(Saves(Slot("a", 2)), null, false, MainMenuMode.Pause, new SettableCanSave());

        Assert.Equal(MainMenuAction.Save, model.Selected.Action);
        Assert.True(model.Entries[0].Enabled);
        Assert.True(model.Entries[1].Enabled);
        Assert.Equal(string.Empty, model.Entries[1].DisabledReason);
    }

    [Fact]
    public void When_saving_is_not_allowed_both_save_rows_say_why_and_selection_moves_on()
    {
        var canSave = new SettableCanSave { Allowed = false, Reason = "You can't save in combat." };
        var model = new MainMenuModel(Saves(Slot("a", 2)), null, false, MainMenuMode.Pause, canSave);

        Assert.False(model.Entries[0].Enabled);
        Assert.False(model.Entries[1].Enabled);
        Assert.Equal("You can't save in combat.", model.Entries[0].DisabledReason);
        Assert.Equal("You can't save in combat.", model.Entries[1].DisabledReason);
        Assert.False(model.Select(MainMenuAction.Save));
        Assert.Equal(MainMenuAction.Load, model.Selected.Action);
    }

    [Fact]
    public void Without_a_save_query_the_pause_menu_cannot_save()
    {
        var model = new MainMenuModel(Saves(), null, false, MainMenuMode.Pause);

        Assert.False(model.Entries[0].Enabled);
        Assert.False(model.Entries[1].Enabled);
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Pause_load_needs_a_save_and_exit_asks_first()
    {
        var model = new MainMenuModel(Saves(), null, false, MainMenuMode.Pause, new SettableCanSave());

        Assert.False(model.Entries[2].Enabled);
        Assert.NotEqual(string.Empty, model.Entries[2].DisabledReason);
        var exit = model.Entries[^1];
        Assert.Equal(MainMenuAction.Quit, exit.Action);
        Assert.True(exit.NeedsConfirmation);
        Assert.Equal("Exit to the title screen?", exit.ConfirmPrompt);
    }

    [Fact]
    public void Refresh_rechecks_whether_saving_is_allowed()
    {
        var canSave = new SettableCanSave { Allowed = false, Reason = "Not now." };
        var model = new MainMenuModel(Saves(), null, false, MainMenuMode.Pause, canSave);
        Assert.False(model.Entries[1].Enabled);

        canSave.Allowed = true;
        model.Refresh();

        Assert.True(model.Entries[0].Enabled);
        Assert.True(model.Entries[1].Enabled);
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Moving_in_the_pause_menu_skips_disabled_rows_and_wraps()
    {
        var model = new MainMenuModel(Saves(), null, false, MainMenuMode.Pause);

        model.Move(CursorMove.Down);
        Assert.Equal(MainMenuAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void The_title_menu_never_shows_the_pause_rows()
    {
        var model = new MainMenuModel(Saves(Slot("a", 2)), "Mods", true, MainMenuMode.Title, new SettableCanSave());

        Assert.DoesNotContain(MainMenuAction.Save, Actions(model));
        Assert.DoesNotContain(MainMenuAction.Quicksave, Actions(model));
        Assert.Equal(MainMenuAction.Continue, model.Selected.Action);
    }

    [Fact]
    public void Back_resumes_the_game_only_in_pause_mode()
    {
        Assert.True(Pause(Saves(), new SettableCanSave()).ResumesOnBack);
        Assert.False(new MainMenuModel(Saves()).ResumesOnBack);
    }

    [Fact]
    public void Quicksave_writes_through_the_writer_and_leaves_no_notice()
    {
        var store = Saves();
        var model = Pause(store, new SettableCanSave());

        var result = model.Quicksave();

        Assert.True(result.Ok);
        Assert.Equal(1, store.WriteCount);
        Assert.Equal(SaveKind.Quick, store.List().Single().Kind);
        Assert.Equal(string.Empty, model.Notice);
    }

    [Fact]
    public void Quicksave_rechecks_saving_at_activation_and_refuses_a_stale_enabled_row()
    {
        var store = Saves();
        var gate = new SettableCanSave();
        var model = Pause(store, gate);
        Assert.True(model.Entries[0].Enabled);
        gate.Allowed = false;
        gate.Reason = "You can't save in combat.";

        var result = model.Quicksave();

        Assert.False(result.Ok);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal("You can't save in combat.", model.Notice);
        Assert.False(model.Entries[0].Enabled);
    }

    [Fact]
    public void A_refusal_without_a_reason_uses_the_standard_sentence()
    {
        var gate = new SettableCanSave { Allowed = false };
        var model = Pause(Saves(), gate);

        model.Quicksave();

        Assert.Equal(SaveListController.CannotSaveText, model.Notice);
    }

    [Fact]
    public void A_failed_quicksave_keeps_the_menu_open_with_the_error_and_a_retry_works()
    {
        var store = Saves();
        store.FailNext = "Disk full.";
        var model = Pause(store, new SettableCanSave());
        model.Select(MainMenuAction.Quicksave);

        Assert.False(model.Quicksave().Ok);
        Assert.Equal("Disk full.", model.Notice);
        Assert.Equal(MainMenuAction.Quicksave, model.Selected.Action);

        Assert.True(model.Quicksave().Ok);
        Assert.Equal(string.Empty, model.Notice);
    }

    [Fact]
    public void A_writer_that_throws_becomes_a_notice()
    {
        var model = Pause(Saves(), new SettableCanSave(), new ThrowingWriter());

        var result = model.Quicksave();

        Assert.False(result.Ok);
        Assert.Equal("boom", model.Notice);
    }

    [Fact]
    public void Quicksave_without_a_writer_fails_with_a_notice()
    {
        var model = new MainMenuModel(Saves(), null, false, MainMenuMode.Pause, new SettableCanSave());

        Assert.False(model.Quicksave().Ok);
        Assert.Equal(SaveListController.FailedText, model.Notice);
    }

    [Fact]
    public void Moving_the_cursor_clears_the_notice()
    {
        var gate = new SettableCanSave { Allowed = false, Reason = "Not now." };
        var model = Pause(Saves(), gate);
        model.Quicksave();
        Assert.Equal("Not now.", model.Notice);

        model.Move(CursorMove.Down);

        Assert.Equal(string.Empty, model.Notice);
    }
}
