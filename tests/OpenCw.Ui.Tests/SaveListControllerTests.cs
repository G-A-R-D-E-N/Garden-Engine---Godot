using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SaveListControllerTests
{
    static readonly DateTimeOffset Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    sealed class ThrowingWriter : ISaveWriter
    {
        public ActionResult SaveNew() => throw new InvalidOperationException("boom");

        public ActionResult Overwrite(string id) => throw new InvalidOperationException("boom");

        public ActionResult Quicksave() => throw new InvalidOperationException("boom");
    }

    sealed class StubbornSaves : ISaveSource
    {
        private readonly ISaveSource _inner;

        public StubbornSaves(ISaveSource inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<SaveSlotInfo> List() => _inner.List();

        public bool Delete(string id) => false;
    }

    static SaveSlotInfo Slot(string id, string name, int day, SaveKind kind = SaveKind.Manual) =>
        new(id, name, "Ann", 5, TimeSpan.FromHours(1), "Somewhere", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "", kind);

    static (SaveListController Ctrl, InMemorySaveStore Store, SettableCanSave Gate) Make()
    {
        var store = new InMemorySaveStore(new[] { Slot("a", "Alpha", 3), Slot("b", "Beta", 5) }, () => Now);
        var gate = new SettableCanSave();
        return (new SaveListController(store, store, gate), store, gate);
    }

    static string[] Rows(SaveListController ctrl) => WorkshopTestHelpers.RowTexts(ctrl.List);

    [Fact]
    public void The_new_save_row_comes_first_and_is_selected_with_saves_newest_first()
    {
        var (ctrl, _, _) = Make();

        Assert.Equal(new[] { "[NEW SAVE]", "Beta", "Alpha" }, Rows(ctrl));
        Assert.Equal(SaveListController.NewSaveKey, ctrl.Rows.Selected!.Key);
        Assert.Equal("2 saved games", ctrl.Header);
        Assert.Equal("Save game", ctrl.Title);
    }

    [Fact]
    public void One_saved_game_is_singular()
    {
        var store = new InMemorySaveStore(new[] { Slot("a", "Alpha", 3) }, () => Now);
        var ctrl = new SaveListController(store, store, new SettableCanSave());

        Assert.Equal("1 saved game", ctrl.Header);
    }

    [Fact]
    public void An_empty_store_still_offers_the_new_save_row()
    {
        var store = new InMemorySaveStore();
        var ctrl = new SaveListController(store, store, new SettableCanSave());

        Assert.Equal(new[] { "[NEW SAVE]" }, Rows(ctrl));
        Assert.Equal("0 saved games", ctrl.Header);
    }

    [Fact]
    public void Quick_and_auto_saves_keep_their_prefixes()
    {
        var store = new InMemorySaveStore(new[] { Slot("q", "Q", 4, SaveKind.Quick), Slot("u", "U", 3, SaveKind.Auto) }, () => Now);
        var ctrl = new SaveListController(store, store, new SettableCanSave());

        Assert.Equal(new[] { "[NEW SAVE]", "Quick: Q", "Auto: U" }, Rows(ctrl));
    }

    [Fact]
    public void The_delete_hint_is_offered_for_a_save_but_not_for_the_new_save_row()
    {
        var (ctrl, _, _) = Make();

        Assert.DoesNotContain(ctrl.Hints, hint => hint.Action == MenuInputAction.Compare);
        Assert.Equal("Save", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal("Delete", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Compare).Label);
    }

    [Fact]
    public void Accepting_the_new_save_row_writes_a_new_save_and_finishes()
    {
        var (ctrl, store, _) = Make();
        var sounds = new List<string>();
        var results = new List<bool>();
        ctrl.SoundRequested += sounds.Add;
        ctrl.Finished += results.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, store.WriteCount);
        Assert.Contains(store.List(), slot => slot.Id == "save_1");
        Assert.Equal(SaveListController.SavedText, ctrl.Message);
        Assert.Contains("menu.confirm", sounds);
        Assert.Equal(new[] { true }, results);
        Assert.True(ctrl.IsConfirmed);
    }

    [Fact]
    public void Accepting_a_save_asks_before_overwriting_and_a_second_accept_writes()
    {
        var (ctrl, store, _) = Make();
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.IsConfirming);
        Assert.Equal("Overwrite \"Beta\"?", ctrl.DetailLines.Single().Text);
        Assert.Equal(0, store.WriteCount);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, store.WriteCount);
        Assert.Equal(Now, store.List().Single(slot => slot.Id == "b").Timestamp);
        Assert.True(ctrl.IsFinished);
        Assert.True(ctrl.IsConfirmed);
    }

    [Fact]
    public void Back_cancels_the_overwrite_question_without_writing()
    {
        var (ctrl, store, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);

        Assert.False(ctrl.IsConfirming);
        Assert.False(ctrl.IsFinished);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void Back_with_nothing_pending_closes_the_list()
    {
        var (ctrl, _, _) = Make();

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.IsConfirmed);
    }

    [Fact]
    public void Delete_asks_first_then_removes_the_save_and_keeps_the_cursor_in_range()
    {
        var (ctrl, store, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.True(ctrl.IsConfirming);
        Assert.Equal("Delete \"Alpha\"?", ctrl.DetailLines.Single().Text);
        Assert.Equal(2, store.List().Count);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "[NEW SAVE]", "Beta" }, Rows(ctrl));
        Assert.Equal("b", ctrl.Rows.Selected!.Key);
        Assert.Equal(SaveListController.DeletedText, ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Delete_is_not_available_on_the_new_save_row()
    {
        var (ctrl, store, _) = Make();

        ctrl.Handle(MenuInputAction.Compare);

        Assert.False(ctrl.IsConfirming);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void A_delete_the_source_refuses_says_so_and_keeps_the_row()
    {
        var store = new InMemorySaveStore(new[] { Slot("a", "Alpha", 3) }, () => Now);
        var ctrl = new SaveListController(new StubbornSaves(store), store, new SettableCanSave());
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Compare);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(SaveListController.DeleteFailedText, ctrl.Message);
        Assert.Equal(new[] { "[NEW SAVE]", "Alpha" }, Rows(ctrl));
    }

    [Fact]
    public void When_saving_is_not_allowed_nothing_is_written_and_the_reason_is_shown()
    {
        var (ctrl, store, gate) = Make();
        gate.Allowed = false;
        gate.Reason = "You can't save in combat.";

        Assert.Contains(ctrl.DetailLines, line => line.Text == "You can't save in combat." && line.Tone == LineTone.Bad);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("You can't save in combat.", ctrl.Message);
        Assert.Equal(0, store.WriteCount);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Without_a_save_query_saving_is_refused_quietly()
    {
        var store = new InMemorySaveStore();
        var ctrl = new SaveListController(store, store, null);
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(0, store.WriteCount);
        Assert.Equal(string.Empty, ctrl.Message);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Saving_becoming_disallowed_after_the_overwrite_question_blocks_the_write()
    {
        var (ctrl, store, gate) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        gate.Allowed = false;
        gate.Reason = "Not now.";

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Not now.", ctrl.Message);
        Assert.Equal(0, store.WriteCount);
        Assert.False(ctrl.IsFinished);
        Assert.False(ctrl.IsConfirming);
    }

    [Fact]
    public void A_failed_write_shows_the_reason_and_stays_open_so_a_retry_works()
    {
        var (ctrl, store, _) = Make();
        store.FailNext = "Disk full.";

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Disk full.", ctrl.Message);
        Assert.False(ctrl.IsFinished);
        Assert.Equal(0, store.WriteCount);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.IsFinished);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public void A_writer_that_throws_becomes_a_failure_with_its_message()
    {
        var store = new InMemorySaveStore();
        var ctrl = new SaveListController(store, new ThrowingWriter(), new SettableCanSave());

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("boom", ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Nothing_is_handled_once_the_list_has_finished()
    {
        var (ctrl, _, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(ctrl.Handle(MenuInputAction.Down));
    }
}
