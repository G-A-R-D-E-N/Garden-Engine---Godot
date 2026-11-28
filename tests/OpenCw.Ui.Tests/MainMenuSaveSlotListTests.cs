using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuSaveSlotListTests
{
    sealed class FakeSaves : ISaveSource
    {
        public List<SaveSlotInfo> Slots { get; } = new();

        public bool DeleteSucceeds { get; set; } = true;

        public IReadOnlyList<SaveSlotInfo> List() => Slots.ToList();

        public bool Delete(string id) => DeleteSucceeds && Slots.RemoveAll(s => s.Id == id) > 0;
    }

    static SaveSlotInfo Slot(string id, string name, string character, int level, int day, SaveKind kind,
                             string location = "Harbor") =>
        new(id, name, character, level, TimeSpan.FromMinutes(level * 10), location,
            new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "thumb/" + id + ".png", kind);

    static FakeSaves Saves()
    {
        var saves = new FakeSaves();
        saves.Slots.Add(Slot("s1", "Alpha", "Ann", 10, 3, SaveKind.Manual));
        saves.Slots.Add(Slot("s2", "Bravo", "Ben", 20, 5, SaveKind.Manual));
        saves.Slots.Add(Slot("s3", "Quicksave", "Ann", 12, 6, SaveKind.Quick, "Town A"));
        saves.Slots.Add(Slot("s4", "Autosave 1", "Ben", 15, 7, SaveKind.Auto));
        saves.Slots.Add(Slot("s5", "Autosave 2", "Ann", 5, 4, SaveKind.Auto));
        return saves;
    }

    static string[] Ids(SaveSlotListModel model) => model.Visible.Select(s => s.Id).ToArray();

    [Fact]
    public void Default_order_is_newest_first_within_manual_then_quick_then_auto_groups()
    {
        var model = new SaveSlotListModel(Saves());

        Assert.Equal(new[] { "s2", "s1", "s3", "s4", "s5" }, Ids(model));
        Assert.Equal("s2", model.Current?.Id);
    }

    [Fact]
    public void Name_sort_is_ascending_by_default_and_case_insensitive()
    {
        var model = new SaveSlotListModel(Saves());

        model.SetSort(SaveSort.Name);
        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s5" }, Ids(model));
    }

    [Fact]
    public void Level_sort_is_highest_first_by_default_and_the_direction_can_be_toggled()
    {
        var model = new SaveSlotListModel(Saves());

        model.SetSort(SaveSort.Level);
        Assert.Equal(new[] { "s2", "s1", "s3", "s4", "s5" }, Ids(model));

        model.ToggleDirection();
        Assert.False(model.Descending);
        Assert.Equal(new[] { "s1", "s2", "s3", "s5", "s4" }, Ids(model));
    }

    [Fact]
    public void Equal_sort_values_fall_back_to_a_stable_id_order()
    {
        var saves = new FakeSaves();
        saves.Slots.Add(Slot("b", "Same", "Ann", 1, 1, SaveKind.Manual));
        saves.Slots.Add(Slot("a", "Same", "Ann", 1, 1, SaveKind.Manual));

        Assert.Equal(new[] { "a", "b" }, Ids(new SaveSlotListModel(saves)));
    }

    [Fact]
    public void Character_filter_narrows_the_list_and_lists_the_distinct_characters()
    {
        var model = new SaveSlotListModel(Saves());
        Assert.Equal(new[] { "Ann", "Ben" }, model.Characters.ToArray());

        model.SetCharacter("Ann");
        Assert.Equal(new[] { "s1", "s3", "s5" }, Ids(model));

        model.SetCharacter(null);
        Assert.Equal(5, model.Visible.Count);
    }

    [Fact]
    public void Search_matches_name_character_and_location()
    {
        var model = new SaveSlotListModel(Saves());

        model.SetSearch("autosave");
        Assert.Equal(new[] { "s4", "s5" }, Ids(model));
        model.SetSearch("town");
        Assert.Equal(new[] { "s3" }, Ids(model));
        model.SetSearch("ben");
        Assert.Equal(new[] { "s2", "s4" }, Ids(model));
    }

    [Fact]
    public void The_selected_save_follows_a_resort()
    {
        var model = new SaveSlotListModel(Saves());
        model.Move(CursorMove.Down);
        Assert.Equal("s1", model.Current?.Id);

        model.SetSort(SaveSort.Level);

        Assert.Equal("s1", model.Current?.Id);
    }

    [Fact]
    public void Delete_needs_a_confirmation_locks_the_cursor_and_only_removes_the_confirmed_save()
    {
        var saves = Saves();
        var model = new SaveSlotListModel(saves);
        model.Move(CursorMove.Down);

        Assert.True(model.RequestDelete());
        Assert.True(model.IsConfirmingDelete);
        Assert.Equal("s1", model.PendingDelete?.Id);
        Assert.False(model.RequestDelete());
        model.Move(CursorMove.Down);
        Assert.False(model.SelectVisible(0));
        Assert.Equal(1, model.CursorIndex);
        Assert.Equal(5, saves.Slots.Count);

        model.CancelDelete();
        Assert.False(model.IsConfirmingDelete);
        Assert.Equal(5, saves.Slots.Count);

        model.RequestDelete();
        Assert.True(model.ConfirmDelete());

        Assert.DoesNotContain(saves.Slots, s => s.Id == "s1");
        Assert.Equal(new[] { "s2", "s3", "s4", "s5" }, Ids(model));
        Assert.Equal("s3", model.Current?.Id);
        Assert.False(model.IsConfirmingDelete);
    }

    [Fact]
    public void A_refused_delete_leaves_the_list_alone_and_ends_the_confirmation()
    {
        var saves = Saves();
        saves.DeleteSucceeds = false;
        var model = new SaveSlotListModel(saves);
        model.RequestDelete();

        Assert.False(model.ConfirmDelete());

        Assert.Equal(5, model.Visible.Count);
        Assert.False(model.IsConfirmingDelete);
    }

    [Fact]
    public void Delete_is_unavailable_with_nothing_selected_and_confirm_without_request_does_nothing()
    {
        var saves = Saves();
        var model = new SaveSlotListModel(saves);
        Assert.False(model.ConfirmDelete());
        model.SetSearch("zzz");

        Assert.False(model.RequestDelete());
        Assert.Equal(5, saves.Slots.Count);
    }

    [Fact]
    public void Reload_picks_up_new_saves_and_drops_a_pending_delete_whose_save_vanished()
    {
        var saves = Saves();
        var model = new SaveSlotListModel(saves);
        model.RequestDelete();
        saves.Slots.RemoveAll(s => s.Id == "s2");
        saves.Slots.Add(Slot("s6", "Fresh", "Cy", 1, 20, SaveKind.Manual));

        model.Reload();

        Assert.False(model.IsConfirmingDelete);
        Assert.Equal("s6", model.Visible[0].Id);
        Assert.Contains("Cy", model.Characters);
    }
}
