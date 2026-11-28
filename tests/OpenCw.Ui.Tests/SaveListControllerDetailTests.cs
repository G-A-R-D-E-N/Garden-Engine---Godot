using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SaveListControllerDetailTests
{
    static readonly DateTimeOffset Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    sealed class SilentFailWriter : ISaveWriter
    {
        public ActionResult SaveNew() => new(false);

        public ActionResult Overwrite(string id) => new(false);

        public ActionResult Quicksave() => new(false);
    }

    sealed class ReentrantWriter : ISaveWriter
    {
        public SaveListController? Controller { get; set; }

        public int Calls { get; private set; }

        public bool? InnerHandled { get; private set; }

        public ActionResult SaveNew()
        {
            Calls++;
            InnerHandled = Controller!.Handle(MenuInputAction.Accept);
            return new ActionResult(true);
        }

        public ActionResult Overwrite(string id) => new(true);

        public ActionResult Quicksave() => new(true);
    }

    static SaveSlotInfo Slot(string id, string name, int day) =>
        new(id, name, "Ann", 5, TimeSpan.FromHours(1), "Somewhere", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "");

    static (SaveListController Ctrl, InMemorySaveStore Store, SettableCanSave Gate) Make()
    {
        var store = new InMemorySaveStore(new[] { Slot("a", "Alpha", 3), Slot("b", "Beta", 5) }, () => Now);
        var gate = new SettableCanSave();
        return (new SaveListController(store, store, gate), store, gate);
    }

    static List<string> Sounds(SaveListController ctrl)
    {
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;
        return sounds;
    }

    [Fact]
    public void Rows_are_records_of_key_text_and_slot_with_the_new_save_row_first_and_ties_broken_by_ordinal_id()
    {
        var store = new InMemorySaveStore(new[] { Slot("a", "a", 3), Slot("B", "B", 3), Slot("c", "c", 4) }, () => Now);
        var ctrl = new SaveListController(store, store, new SettableCanSave());

        Assert.Equal(new SaveRow(SaveListController.NewSaveKey, "[NEW SAVE]", null), ctrl.Rows.Rows[0]);
        Assert.Equal(new[] { "c", "B", "a" }, ctrl.Rows.Rows.Skip(1).Select(r => r.Key).ToArray());
        Assert.Equal(new SaveRow("c", "c", Slot("c", "c", 4)), ctrl.Rows.Rows[1]);
    }

    [Fact]
    public void The_header_counts_saves_without_the_new_save_row_and_singularise_one()
    {
        var (ctrl, _, _) = Make();
        Assert.Equal("2 saved games", ctrl.Header);

        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Compare);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("1 saved game", ctrl.Header);
    }

    [Fact]
    public void A_saved_row_details_the_character_the_summary_and_the_date()
    {
        var (ctrl, _, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(
            new[]
            {
                new MenuTextLine("Ann", LineTone.Heading),
                new MenuTextLine("Level 5, Somewhere, 1h 00m", LineTone.Normal),
                new MenuTextLine("2026-01-03 00:00", LineTone.Muted),
            },
            ctrl.DetailLines.ToArray());
    }

    [Fact]
    public void The_new_save_row_explains_itself_and_adds_the_reason_only_when_saving_is_blocked_with_one()
    {
        var (ctrl, _, gate) = Make();
        Assert.Equal(new[] { new MenuTextLine("Writes a new save.", LineTone.Muted) }, ctrl.DetailLines.ToArray());

        gate.Allowed = false;
        gate.Reason = "Not now.";
        Assert.Equal(
            new[] { new MenuTextLine("Writes a new save.", LineTone.Muted), new MenuTextLine("Not now.", LineTone.Bad) },
            ctrl.DetailLines.ToArray());

        gate.Reason = "";
        Assert.Equal(new[] { new MenuTextLine("Writes a new save.", LineTone.Muted) }, ctrl.DetailLines.ToArray());
    }

    [Fact]
    public void Hints_offer_save_delete_and_back_on_a_save_and_confirm_and_cancel_while_asking()
    {
        var (ctrl, _, _) = Make();
        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Save"), new MenuHint(MenuInputAction.Back, "Back") },
            ctrl.Hints.ToArray());

        ctrl.Handle(MenuInputAction.Down);
        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Save"), new MenuHint(MenuInputAction.Compare, "Delete"),
                new MenuHint(MenuInputAction.Back, "Back"),
            },
            ctrl.Hints.ToArray());

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") },
            ctrl.Hints.ToArray());
    }

    [Fact]
    public void While_a_question_is_open_every_other_action_is_swallowed_and_the_question_stays()
    {
        var (ctrl, store, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.Handle(MenuInputAction.Down));
        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.True(ctrl.Handle(MenuInputAction.TabNext));

        Assert.Equal(1, ctrl.Rows.SelectedIndex);
        Assert.True(ctrl.IsConfirming);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void Asking_cancelling_blocking_and_deleting_each_request_their_own_sound()
    {
        var (ctrl, _, _) = Make();
        var sounds = Sounds(ctrl);

        ctrl.Handle(MenuInputAction.Compare);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Back);
        ctrl.Handle(MenuInputAction.Compare);
        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { "menu.blocked", "menu.forward", "menu.back", "menu.forward", "menu.back" }, sounds);
    }

    [Fact]
    public void A_writer_failure_without_a_reason_says_the_generic_failure_and_stays_open()
    {
        var store = new InMemorySaveStore();
        var ctrl = new SaveListController(store, new SilentFailWriter(), new SettableCanSave());
        var sounds = Sounds(ctrl);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(SaveListController.FailedText, ctrl.Message);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_blocked_save_with_no_reason_stays_silent_apart_from_the_blocked_sound()
    {
        var (ctrl, store, gate) = Make();
        gate.Allowed = false;
        var sounds = Sounds(ctrl);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("", ctrl.Message);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void An_overwrite_whose_save_vanished_reports_the_stores_reason_and_stays_open()
    {
        var (ctrl, store, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        store.Delete("b");
        var sounds = Sounds(ctrl);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("That save is gone.", ctrl.Message);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
        Assert.False(ctrl.IsConfirming);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_writer_that_calls_back_into_the_controller_during_a_write_is_ignored_and_writes_once()
    {
        var store = new InMemorySaveStore();
        var writer = new ReentrantWriter();
        var ctrl = new SaveListController(store, writer, new SettableCanSave());
        writer.Controller = ctrl;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, writer.Calls);
        Assert.True(writer.InnerHandled);
        Assert.True(ctrl.IsFinished);
        Assert.True(ctrl.IsConfirmed);
    }

    [Fact]
    public void A_status_message_lasts_three_seconds_and_other_actions_are_passed_to_the_row_list()
    {
        var (ctrl, _, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Compare);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(SaveListController.DeletedText, ctrl.Message);

        ctrl.Tick(1.5);
        Assert.Equal(SaveListController.DeletedText, ctrl.Message);
        ctrl.Tick(1.5);
        Assert.Equal("", ctrl.Message);

        Assert.True(ctrl.Handle(MenuInputAction.Up));
        Assert.Equal(0, ctrl.Rows.SelectedIndex);
        Assert.False(ctrl.Handle(MenuInputAction.TabNext));
        Assert.False(ctrl.Handle(MenuInputAction.Sort));
    }
}
