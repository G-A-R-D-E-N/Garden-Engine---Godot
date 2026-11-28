using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuSaveSlotViewTests
{
    sealed class FakeSaves : ISaveSource
    {
        public List<SaveSlotInfo> Slots { get; } = new();

        public IReadOnlyList<SaveSlotInfo> List() => Slots.ToList();

        public bool Delete(string id) => Slots.RemoveAll(s => s.Id == id) > 0;
    }

    static SaveSlotInfo Slot(string id, string name, string character, int level, int day, SaveKind kind) =>
        new(id, name, character, level, TimeSpan.FromMinutes(level * 10), "Harbor",
            new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), "", kind);

    static FakeSaves Saves()
    {
        var saves = new FakeSaves();
        saves.Slots.Add(Slot("s1", "Alpha", "Ann", 10, 3, SaveKind.Manual));
        saves.Slots.Add(Slot("s2", "Bravo", "Ben", 20, 5, SaveKind.Manual));
        saves.Slots.Add(Slot("s3", "Quicksave", "Ann", 12, 6, SaveKind.Quick));
        saves.Slots.Add(Slot("s4", "Autosave 1", "Ben", 15, 7, SaveKind.Auto));
        saves.Slots.Add(Slot("s5", "Autosave 2", "Ann", 5, 4, SaveKind.Auto));
        return saves;
    }

    static SaveSlotListView Make(FakeSaves? saves = null, IFavouriteStore? store = null) =>
        new(new SaveSlotListModel(saves ?? Saves(), store));

    static readonly Dictionary<string, string> IdByName = new()
    {
        ["Alpha"] = "s1", ["Bravo"] = "s2", ["Quicksave"] = "s3", ["Autosave 1"] = "s4", ["Autosave 2"] = "s5", ["Solo"] = "s6",
    };

    static string[] Ids(SaveSlotListView view) =>
        Enumerable.Range(0, view.RowCount).Select(i => IdByName[view.RowText(i).Split(": ").Last()]).ToArray();

    [Fact]
    public void Rows_show_the_save_name_with_a_kind_prefix_and_the_level_as_detail()
    {
        var view = Make();

        Assert.Equal(new[] { "s2", "s1", "s3", "s4", "s5" }, Ids(view));
        Assert.Equal("Bravo", view.RowText(0));
        Assert.Equal("Lv 20", view.RowDetail(0));
        Assert.Equal("Quick: Quicksave", view.RowText(2));
        Assert.Equal("Auto: Autosave 1", view.RowText(3));
        Assert.Equal("", view.RowText(9));
        Assert.Equal("", view.RowDetail(-1));
    }

    [Fact]
    public void Tabs_are_all_then_each_character_then_favourites_and_selecting_one_filters_the_rows()
    {
        var view = Make();
        Assert.Equal(new[] { "All", "Ann", "Ben", "Favourites" }, view.Tabs.ToArray());

        view.SelectTab(1);

        Assert.Equal(1, view.TabIndex);
        Assert.Equal(new[] { "s1", "s3", "s5" }, Ids(view));
        view.SelectTab(0);
        Assert.Equal(0, view.TabIndex);
        Assert.Equal(5, view.RowCount);
    }

    [Fact]
    public void Tab_actions_wrap_around_the_ends()
    {
        var view = Make();

        view.Handle(MenuInputAction.TabPrev);
        Assert.Equal(3, view.TabIndex);
        view.Handle(MenuInputAction.TabNext);
        Assert.Equal(0, view.TabIndex);
    }

    [Fact]
    public void Sorting_by_the_current_key_flips_the_direction_and_a_new_key_uses_its_natural_one()
    {
        var view = Make();
        Assert.Equal(new[] { "Date", "Name", "Level" }, view.SortNames.ToArray());
        Assert.Equal(0, view.SortIndex);
        Assert.True(view.SortDescending);

        view.SortBy(0);
        Assert.False(view.SortDescending);
        Assert.Equal(new[] { "s1", "s2", "s3", "s5", "s4" }, Ids(view));

        view.SortBy(1);
        Assert.Equal(1, view.SortIndex);
        Assert.False(view.SortDescending);
        Assert.Equal("Alpha", view.RowText(0));

        view.SortBy(2);
        Assert.True(view.SortDescending);
        Assert.Equal("Bravo", view.RowText(0));
    }

    [Fact]
    public void The_sort_action_steps_through_the_sorts_and_wraps()
    {
        var view = Make();

        view.Handle(MenuInputAction.Sort);
        Assert.Equal(1, view.SortIndex);
        view.Handle(MenuInputAction.Sort);
        view.Handle(MenuInputAction.Sort);
        Assert.Equal(0, view.SortIndex);
    }

    [Fact]
    public void Favourites_are_stored_and_the_favourites_tab_shows_only_them()
    {
        var store = new InMemoryFavouriteStore();
        var view = Make(store: store);

        Assert.True(view.ToggleFavouriteAt(1));

        Assert.True(view.RowIsFavourite(1));
        Assert.False(view.RowIsFavourite(0));
        Assert.Contains("s1", store.Load());
        view.SelectTab(view.Tabs.Count - 1);
        Assert.Equal(new[] { "s1" }, Ids(view));
        Assert.Equal(view.Tabs.Count - 1, view.TabIndex);
    }

    [Fact]
    public void Delete_asks_first_locks_the_view_and_only_deletes_when_confirmed()
    {
        var saves = Saves();
        var view = Make(saves);
        SaveSlotInfo? asked = null;
        view.DeleteRequested += slot => asked = slot;
        view.Select(1);

        Assert.True(view.Handle(MenuInputAction.Compare));

        Assert.Equal("s1", asked?.Id);
        Assert.Empty(view.Hints);
        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.False(view.Select(0));
        Assert.Equal(1, view.SelectedIndex);
        Assert.Equal(5, saves.Slots.Count);

        view.CancelDelete();
        Assert.NotEmpty(view.Hints);
        Assert.Equal(5, saves.Slots.Count);

        view.Handle(MenuInputAction.Compare);
        Assert.True(view.ConfirmDelete());
        Assert.Equal(new[] { "s2", "s3", "s4", "s5" }, Ids(view));
        Assert.DoesNotContain(saves.Slots, s => s.Id == "s1");
        Assert.Equal("s3", view.Selected?.Id);
    }

    [Fact]
    public void Deleting_the_last_save_of_a_character_returns_to_the_all_tab()
    {
        var saves = Saves();
        saves.Slots.Add(Slot("s6", "Solo", "Cy", 1, 20, SaveKind.Manual));
        var view = Make(saves);
        view.SelectTab(view.Tabs.ToList().IndexOf("Cy"));
        Assert.Equal(new[] { "s6" }, Ids(view));

        view.Handle(MenuInputAction.Compare);
        view.ConfirmDelete();

        Assert.Equal(0, view.TabIndex);
        Assert.DoesNotContain("Cy", view.Tabs);
        Assert.Equal(5, view.RowCount);
    }

    [Fact]
    public void Search_filters_rows_and_back_clears_it_before_leaving()
    {
        var view = Make();

        view.SetSearch("auto");
        Assert.Equal(2, view.RowCount);
        Assert.Contains("2 of 5 saves", view.Summary);
        Assert.True(view.Handle(MenuInputAction.Back));
        Assert.Equal("", view.SearchText);
        Assert.Equal(5, view.RowCount);
        Assert.False(view.Handle(MenuInputAction.Back));

        view.BeginSearch();
        view.SetSearch("auto");
        Assert.True(view.IsSearching);
        Assert.True(view.Handle(MenuInputAction.Back));
        Assert.False(view.IsSearching);
        Assert.Equal(5, view.RowCount);
    }

    [Fact]
    public void Accepting_raises_the_chosen_save_and_a_click_selects_before_accepting()
    {
        var view = Make();
        var chosen = new List<string>();
        view.Accepted += slot => chosen.Add(slot.Id);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.True(view.Activate(1));
        Assert.False(view.Activate(99));

        Assert.Equal(new[] { "s2", "s1" }, chosen.ToArray());
        Assert.Equal(1, view.SelectedIndex);
        view.SetSearch("zzz");
        Assert.False(view.Handle(MenuInputAction.Accept));
    }

    [Fact]
    public void Scrolling_follows_the_cursor_and_paging_uses_the_visible_row_count()
    {
        var view = Make();
        view.SetViewRows(2);
        view.Handle(MenuInputAction.Down);
        view.Handle(MenuInputAction.Down);
        view.Handle(MenuInputAction.Down);
        Assert.Equal(3, view.SelectedIndex);
        Assert.Equal(2, view.ScrollTop);

        view.Handle(MenuInputAction.Down);
        Assert.Equal(3, view.ScrollTop);
        view.Handle(MenuInputAction.PageUp);
        Assert.Equal(3, view.SelectedIndex);
    }

    [Fact]
    public void Hints_name_load_delete_and_sort_and_the_neighbouring_tabs()
    {
        var view = Make();

        var hints = view.Hints.ToDictionary(h => h.Action, h => h.Label);

        Assert.Equal("Load", hints[MenuInputAction.Accept]);
        Assert.Equal("Delete", hints[MenuInputAction.Compare]);
        Assert.Equal("Sort: Date descending", hints[MenuInputAction.Sort]);
        Assert.Equal("Next: Ann", hints[MenuInputAction.TabNext]);
        Assert.Equal("Previous: Favourites", hints[MenuInputAction.TabPrev]);
    }

    [Fact]
    public void Changes_are_reported_to_listeners()
    {
        var view = Make();
        var count = 0;
        view.Changed += () => count++;

        view.SelectTab(1);
        view.Handle(MenuInputAction.Down);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Save_text_formats_playtime_dates_and_summaries()
    {
        Assert.Equal("3h 20m", SaveSlotText.Playtime(TimeSpan.FromMinutes(200)));
        Assert.Equal("1h 01m", SaveSlotText.Playtime(TimeSpan.FromMinutes(61)));
        Assert.Equal("45m", SaveSlotText.Playtime(TimeSpan.FromMinutes(45)));
        Assert.Equal("0m", SaveSlotText.Playtime(TimeSpan.FromMinutes(-5)));
        Assert.Equal("Unknown date", SaveSlotText.Timestamp(DateTimeOffset.MinValue));
        Assert.Equal("2026-03-04 05:06", SaveSlotText.Timestamp(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2))));
        Assert.Equal("Level 10, Harbor, 1h 40m", SaveSlotText.Summary(Slot("a", "n", "c", 10, 1, SaveKind.Manual)));
        Assert.Equal("Level 1, 10m", SaveSlotText.Summary(Slot("a", "n", "c", 1, 1, SaveKind.Manual) with { Location = "" }));
    }
}
