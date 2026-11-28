using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuModelTests
{
    sealed class FakeSaves : ISaveSource
    {
        public List<SaveSlotInfo> Slots { get; } = new();

        public bool DeleteSucceeds { get; set; } = true;

        public IReadOnlyList<SaveSlotInfo> List() => Slots.ToList();

        public bool Delete(string id)
        {
            if (!DeleteSucceeds) return false;
            return Slots.RemoveAll(s => s.Id == id) > 0;
        }
    }

    static SaveSlotInfo Slot(string id, int day, SaveKind kind = SaveKind.Manual) =>
        new(id, id, "Ann", 5, TimeSpan.FromHours(1), "Somewhere", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "", kind);

    static MainMenuAction[] Actions(MainMenuModel model) => model.Entries.Select(e => e.Action).ToArray();

    [Fact]
    public void Without_saves_continue_and_load_are_disabled_with_a_reason_and_new_game_is_selected()
    {
        var model = new MainMenuModel(new FakeSaves());

        Assert.Equal(
            new[] { MainMenuAction.NewGame, MainMenuAction.Continue, MainMenuAction.Load, MainMenuAction.Settings, MainMenuAction.Quit },
            Actions(model));
        Assert.False(model.Entries[1].Enabled);
        Assert.False(model.Entries[2].Enabled);
        Assert.NotEqual("", model.Entries[1].DisabledReason);
        Assert.Null(model.LastSave);
        Assert.Equal(MainMenuAction.NewGame, model.Selected.Action);
    }

    [Fact]
    public void With_saves_continue_targets_the_newest_save_of_any_kind_and_is_the_default_selection()
    {
        var saves = new FakeSaves();
        saves.Slots.Add(Slot("old", 3));
        saves.Slots.Add(Slot("auto", 9, SaveKind.Auto));
        saves.Slots.Add(Slot("mid", 5));

        var model = new MainMenuModel(saves);

        Assert.Equal("auto", model.LastSave?.Id);
        Assert.True(model.Entries.All(e => e.Enabled));
        Assert.Equal(MainMenuAction.Continue, model.Selected.Action);
    }

    [Fact]
    public void The_extras_entry_exists_only_when_a_label_is_given_and_sits_before_quit()
    {
        Assert.DoesNotContain(MainMenuAction.Extras, Actions(new MainMenuModel(new FakeSaves())));

        var model = new MainMenuModel(new FakeSaves(), "Mods");

        var extras = model.Entries.Single(e => e.Action == MainMenuAction.Extras);
        Assert.Equal("Mods", extras.Label);
        Assert.Equal(MainMenuAction.Quit, model.Entries[^1].Action);
        Assert.Equal(MainMenuAction.Settings, model.Entries[^3].Action);
    }

    [Fact]
    public void Moving_skips_disabled_entries_and_wraps()
    {
        var model = new MainMenuModel(new FakeSaves());

        model.Move(CursorMove.Down);
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(MainMenuAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(MainMenuAction.NewGame, model.Selected.Action);
        model.Move(CursorMove.Up);
        Assert.Equal(MainMenuAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Home);
        Assert.Equal(MainMenuAction.NewGame, model.Selected.Action);
        model.Move(CursorMove.End);
        Assert.Equal(MainMenuAction.Quit, model.Selected.Action);
    }

    [Fact]
    public void Disabled_entries_cannot_be_selected_and_quit_asks_for_confirmation()
    {
        var model = new MainMenuModel(new FakeSaves());

        Assert.False(model.Select(MainMenuAction.Continue));
        Assert.Equal(MainMenuAction.NewGame, model.Selected.Action);
        Assert.True(model.Select(MainMenuAction.Quit));
        Assert.Equal(MainMenuAction.Quit, model.Activate()?.Action);
        Assert.True(model.Selected.NeedsConfirmation);
        Assert.False(model.Entries.Where(e => e.Action != MainMenuAction.Quit).Any(e => e.NeedsConfirmation));
    }

    [Fact]
    public void Refresh_enables_entries_when_saves_appear_and_keeps_the_current_selection()
    {
        var saves = new FakeSaves();
        var model = new MainMenuModel(saves);
        model.Select(MainMenuAction.Settings);
        saves.Slots.Add(Slot("a", 2));

        model.Refresh();

        Assert.True(model.Entries[1].Enabled);
        Assert.Equal("a", model.LastSave?.Id);
        Assert.Equal(MainMenuAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Refresh_moves_the_selection_off_continue_when_the_last_save_disappears()
    {
        var saves = new FakeSaves();
        saves.Slots.Add(Slot("a", 2));
        var model = new MainMenuModel(saves);
        Assert.Equal(MainMenuAction.Continue, model.Selected.Action);
        saves.Slots.Clear();

        model.Refresh();

        Assert.Equal(MainMenuAction.NewGame, model.Selected.Action);
        Assert.False(model.Entries[1].Enabled);
        Assert.Null(model.LastSave);
    }
}
