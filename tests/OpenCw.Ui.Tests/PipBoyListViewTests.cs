using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyListViewTests
{
    sealed record Item(string Id, string Name);

    sealed class Probe : PipBoyListView<Item>
    {
        public Probe(MenuList<Item> list)
            : base(list)
        {
        }

        public List<string> Calls { get; } = new();

        public bool IsLocked { get; set; }

        public string[] TabList { get; set; } = ["All"];

        public int Tab { get; set; }

        public string[] SortList { get; set; } = [];

        public int Sort { get; set; } = -1;

        public bool Desc { get; set; }

        public PipBoyCompare? Cmp { get; set; }

        public bool CanCompare { get; set; }

        public bool AcceptResult { get; set; }

        public bool FavouriteResult { get; set; }

        public string? FavLabel { get; set; }

        public string AcceptText { get; set; } = "Select";

        public HashSet<string> FavouriteIds { get; } = new();

        public MenuHint[] Extras { get; set; } = [];

        public override IReadOnlyList<string> Tabs => TabList;

        public override int TabIndex => Tab;

        public override IReadOnlyList<string> SortNames => SortList;

        public override int SortIndex => Sort;

        public override bool SortDescending => Desc;

        protected override bool Locked => IsLocked;

        protected override PipBoyCompare? Compare => Cmp;

        protected override bool HasCompare => CanCompare;

        protected override string AcceptLabel => AcceptText;

        protected override string? FavouriteLabel => FavLabel;

        protected override IEnumerable<MenuHint> ExtraHints => Extras;

        protected override string TextOf(Item item) => item.Name;

        protected override string DetailOf(Item item) => "d-" + item.Id;

        protected override bool IsFavourite(Item item) => FavouriteIds.Contains(item.Id);

        protected override bool ToggleFavourite()
        {
            Calls.Add("fav");
            return FavouriteResult;
        }

        protected override bool OnAccept(Item item)
        {
            Calls.Add("accept:" + item.Id);
            return AcceptResult;
        }

        protected override void SelectionChanged() => Calls.Add("sel");

        protected override void ApplyTab(int index)
        {
            Calls.Add("tab:" + index);
            Tab = index;
        }

        protected override void ApplySort(int index, bool descending)
        {
            Calls.Add("sort:" + index + ":" + descending);
            Sort = index;
            Desc = descending;
        }
    }

    static List<Item> Items(int count) =>
        Enumerable.Range(0, count).Select(i => new Item("i" + i, "Item" + i.ToString("00"))).ToList();

    static Probe Make(int count = 12) => Make(Items(count));

    static Probe Make(List<Item> items) => new(new MenuList<Item>(items, i => i.Id, i => i.Name));

    [Fact]
    public void A_new_view_shows_ten_rows_with_the_first_item_selected()
    {
        var view = Make();

        Assert.Equal(10, view.ViewRows);
        Assert.Equal(12, view.RowCount);
        Assert.Equal(0, view.SelectedIndex);
        Assert.True(view.HasSelection);
        Assert.Equal("i0", view.Selected!.Id);
        Assert.Equal(0, view.ScrollTop);
        Assert.Equal("12 shown", view.Summary);
    }

    sealed class PlainTabs : PipBoyListView<Item>
    {
        public PlainTabs()
            : base(new MenuList<Item>(new List<Item>(), i => i.Id, i => i.Name))
        {
        }

        protected override string TextOf(Item item) => item.Name;
    }

    [Fact]
    public void The_defaults_of_a_view_with_no_overrides_are_one_tab_no_sorts_and_no_hints_but_back()
    {
        var view = new PlainTabs();

        Assert.Equal(new[] { "All" }, view.Tabs.ToArray());
        Assert.Equal(0, view.TabIndex);
        Assert.Empty(view.SortNames);
        Assert.Equal(-1, view.SortIndex);
        Assert.False(view.SortDescending);
        Assert.False(view.IsComparing);
        Assert.False(view.HasSelection);
        Assert.Equal("0 shown", view.Summary);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Back, "Back") }, view.Hints.ToArray());
    }

    [Fact]
    public void Paging_moves_by_one_less_than_the_window_and_the_window_follows_the_cursor()
    {
        var view = Make();

        Assert.True(view.Handle(MenuInputAction.PageDown));
        Assert.Equal(9, view.SelectedIndex);
        Assert.Equal(0, view.ScrollTop);

        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(11, view.SelectedIndex);
        Assert.Equal(2, view.ScrollTop);

        view.Handle(MenuInputAction.PageUp);
        Assert.Equal(2, view.SelectedIndex);
        Assert.Equal(2, view.ScrollTop);
    }

    [Fact]
    public void Shrinking_the_window_shortens_the_page_step()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        view.SetViewRows(4);

        Assert.Equal(4, view.ViewRows);
        Assert.Equal(1, changes);
        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(3, view.SelectedIndex);
        Assert.Equal(0, view.ScrollTop);
        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(6, view.SelectedIndex);
        Assert.Equal(3, view.ScrollTop);
        view.Handle(MenuInputAction.PageDown);
        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(11, view.SelectedIndex);
        Assert.Equal(8, view.ScrollTop);
    }

    [Fact]
    public void A_window_of_zero_rows_becomes_one_row_and_steps_a_page_by_one()
    {
        var view = Make();

        view.SetViewRows(0);

        Assert.Equal(1, view.ViewRows);
        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(1, view.SelectedIndex);
        Assert.Equal(1, view.ScrollTop);
    }

    [Fact]
    public void Setting_the_window_to_its_current_size_does_not_notify()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        view.SetViewRows(10);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Walking_the_cursor_back_up_scrolls_only_when_it_leaves_the_top_of_the_window()
    {
        var view = Make(6);
        view.SetViewRows(3);

        for (var i = 0; i < 4; i++) view.Handle(MenuInputAction.Down);
        Assert.Equal(4, view.SelectedIndex);
        Assert.Equal(2, view.ScrollTop);

        view.Handle(MenuInputAction.Up);
        Assert.Equal(3, view.SelectedIndex);
        Assert.Equal(2, view.ScrollTop);

        view.Handle(MenuInputAction.Up);
        Assert.Equal(2, view.ScrollTop);

        view.Handle(MenuInputAction.Up);
        Assert.Equal(1, view.SelectedIndex);
        Assert.Equal(1, view.ScrollTop);
    }

    [Fact]
    public void Growing_the_window_past_the_list_pulls_the_scroll_back_to_zero()
    {
        var view = Make(6);
        view.SetViewRows(3);
        for (var i = 0; i < 5; i++) view.Handle(MenuInputAction.Down);
        Assert.Equal(3, view.ScrollTop);

        view.SetViewRows(10);

        Assert.Equal(0, view.ScrollTop);
        Assert.Equal(5, view.SelectedIndex);
    }

    [Fact]
    public void Moving_reports_a_selection_change_only_when_the_cursor_actually_moved()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        Assert.True(view.Handle(MenuInputAction.Up));
        Assert.Empty(view.Calls);
        Assert.Equal(1, changes);

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(new[] { "sel" }, view.Calls);
        Assert.Equal(2, changes);
        Assert.Equal(1, view.SelectedIndex);
    }

    [Fact]
    public void Select_accepts_only_rows_that_exist_and_reports_a_change_only_for_a_new_row()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        Assert.True(view.Select(0));
        Assert.Equal(0, changes);
        Assert.Empty(view.Calls);

        Assert.True(view.Select(3));
        Assert.Equal(3, view.SelectedIndex);
        Assert.Equal(1, changes);
        Assert.Equal(new[] { "sel" }, view.Calls);

        Assert.False(view.Select(-1));
        Assert.False(view.Select(12));
        Assert.Equal(3, view.SelectedIndex);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_locked_view_ignores_tab_sort_and_selection_requests()
    {
        var view = Make();
        view.TabList = ["A", "B", "C"];
        view.SortList = ["Name", "Group"];
        view.IsLocked = true;

        view.SelectTab(1);
        view.CycleTab(1);
        view.SortBy(0);
        view.CycleSort();
        Assert.False(view.Select(2));
        Assert.False(view.Activate(2));
        Assert.False(view.ToggleFavouriteAt(2));
        Assert.True(view.Handle(MenuInputAction.Left));
        Assert.True(view.Handle(MenuInputAction.Right));

        Assert.Empty(view.Calls);
        Assert.Equal(0, view.SelectedIndex);
    }

    [Fact]
    public void Selecting_a_tab_applies_it_once_and_ignores_the_current_or_missing_tab()
    {
        var view = Make();
        view.TabList = ["A", "B", "C"];
        var changes = 0;
        view.Changed += () => changes++;

        view.SelectTab(1);
        view.SelectTab(1);
        view.SelectTab(3);
        view.SelectTab(-1);

        Assert.Equal(new[] { "tab:1" }, view.Calls);
        Assert.Equal(1, view.TabIndex);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Left_and_right_cycle_the_tabs_and_wrap_at_both_ends()
    {
        var view = Make();
        view.TabList = ["A", "B", "C"];

        Assert.True(view.Handle(MenuInputAction.Left));
        Assert.Equal(2, view.TabIndex);
        Assert.True(view.Handle(MenuInputAction.Right));
        Assert.Equal(0, view.TabIndex);
        view.Handle(MenuInputAction.Right);
        Assert.Equal(1, view.TabIndex);

        Assert.Equal(new[] { "tab:2", "tab:0", "tab:1" }, view.Calls);
    }

    [Fact]
    public void Sorting_by_a_column_flips_to_descending_only_when_it_is_already_the_ascending_sort()
    {
        var view = Make();
        view.SortList = ["Name", "Group"];

        view.SortBy(0);
        view.SortBy(0);
        view.SortBy(0);
        view.SortBy(1);
        view.SortBy(2);
        view.SortBy(-1);

        Assert.Equal(new[] { "sort:0:False", "sort:0:True", "sort:0:False", "sort:1:False" }, view.Calls);
    }

    [Fact]
    public void The_sort_key_steps_through_each_column_ascending_then_descending_and_wraps()
    {
        var view = Make();
        view.SortList = ["Name", "Group"];
        var changes = 0;
        view.Changed += () => changes++;

        Assert.True(view.Handle(MenuInputAction.Sort));
        Assert.Equal(1, changes);
        for (var i = 0; i < 4; i++) Assert.True(view.Handle(MenuInputAction.Sort));

        Assert.Equal(
            new[] { "sort:0:False", "sort:0:True", "sort:1:False", "sort:1:True", "sort:0:False" },
            view.Calls);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void The_sort_key_does_nothing_and_is_not_consumed_when_the_view_has_no_sorts()
    {
        var view = Make();

        Assert.False(view.Handle(MenuInputAction.Sort));

        Assert.Empty(view.Calls);
    }

    [Fact]
    public void Accept_reports_success_and_notifies_only_when_the_item_handler_accepts()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        Assert.False(view.Handle(MenuInputAction.Accept));
        Assert.Equal(0, changes);

        view.AcceptResult = true;
        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal(1, changes);
        Assert.Equal(new[] { "accept:i0", "accept:i0" }, view.Calls);
    }

    [Fact]
    public void Accept_on_an_empty_list_is_refused_without_asking_the_handler()
    {
        var view = Make(0);
        view.AcceptResult = true;

        Assert.False(view.Handle(MenuInputAction.Accept));
        Assert.True(view.Handle(MenuInputAction.Down));

        Assert.Empty(view.Calls);
        Assert.False(view.HasSelection);
        Assert.Equal(-1, view.SelectedIndex);
    }

    [Fact]
    public void Activating_a_row_selects_it_then_accepts_it()
    {
        var view = Make();
        view.AcceptResult = true;

        Assert.True(view.Activate(2));

        Assert.Equal(2, view.SelectedIndex);
        Assert.Equal(new[] { "sel", "accept:i2" }, view.Calls);
    }

    [Fact]
    public void Activating_a_row_the_handler_refuses_still_moves_the_selection()
    {
        var view = Make();

        Assert.False(view.Activate(2));
        Assert.False(view.Activate(40));

        Assert.Equal(2, view.SelectedIndex);
        Assert.Equal(new[] { "sel", "accept:i2" }, view.Calls);
    }

    [Fact]
    public void The_favourite_key_reports_what_the_subclass_toggle_returned()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        Assert.False(view.Handle(MenuInputAction.Favourite));
        Assert.Equal(0, changes);

        view.FavouriteResult = true;
        Assert.True(view.Handle(MenuInputAction.Favourite));
        Assert.Equal(1, changes);
        Assert.Equal(new[] { "fav", "fav" }, view.Calls);
    }

    [Fact]
    public void Toggling_the_favourite_of_a_row_selects_it_first_and_fails_for_a_missing_row()
    {
        var view = Make();

        Assert.False(view.ToggleFavouriteAt(4));
        Assert.Equal(4, view.SelectedIndex);

        view.FavouriteResult = true;
        Assert.True(view.ToggleFavouriteAt(5));
        Assert.False(view.ToggleFavouriteAt(99));

        Assert.Equal(5, view.SelectedIndex);
        Assert.Equal(new[] { "sel", "fav", "sel", "fav" }, view.Calls);
    }

    [Fact]
    public void Compare_data_shows_until_the_compare_key_hides_it_and_again_after_it_is_pressed_a_second_time()
    {
        var view = Make();
        view.CanCompare = true;
        var rows = new[] { new StatComparison("Weight", 1, 2, false) };
        view.Cmp = new PipBoyCompare("Rifle vs Pistol", rows);

        Assert.True(view.IsComparing);
        Assert.Equal("Rifle vs Pistol", view.CompareHeading);
        Assert.Equal(rows, view.CompareRows.ToArray());

        Assert.True(view.Handle(MenuInputAction.Compare));
        Assert.False(view.IsComparing);
        Assert.Equal(string.Empty, view.CompareHeading);
        Assert.Empty(view.CompareRows);

        Assert.True(view.Handle(MenuInputAction.Compare));
        Assert.True(view.IsComparing);
        Assert.Equal("Rifle vs Pistol", view.CompareHeading);
    }

    [Fact]
    public void Without_compare_support_the_compare_key_is_not_consumed_and_no_compare_is_shown()
    {
        var view = Make();

        Assert.False(view.Handle(MenuInputAction.Compare));

        Assert.False(view.IsComparing);
        Assert.Equal(string.Empty, view.CompareHeading);
        Assert.Empty(view.CompareRows);
    }

    [Fact]
    public void Keys_the_view_does_not_map_are_not_consumed()
    {
        var view = Make();
        view.TabList = ["A", "B"];

        Assert.False(view.Handle(MenuInputAction.Back));
        Assert.False(view.Handle(MenuInputAction.Search));
        Assert.False(view.Handle(MenuInputAction.TabPrev));
        Assert.False(view.Handle(MenuInputAction.TabNext));

        Assert.Empty(view.Calls);
        Assert.Equal(0, view.TabIndex);
    }

    [Fact]
    public void Hints_list_accept_back_sort_compare_favourite_then_the_extras_in_that_order()
    {
        var view = Make();
        view.SortList = ["Name", "Group"];
        view.Sort = 1;
        view.Desc = true;
        view.CanCompare = true;
        view.FavLabel = "Track";
        view.AcceptText = "Open";
        view.Extras = [new MenuHint(MenuInputAction.Search, "Find")];

        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Open"),
                new MenuHint(MenuInputAction.Back, "Back"),
                new MenuHint(MenuInputAction.Sort, "Sort: Group descending"),
                new MenuHint(MenuInputAction.Compare, "Hide compare"),
                new MenuHint(MenuInputAction.Favourite, "Track"),
                new MenuHint(MenuInputAction.Search, "Find"),
            },
            view.Hints.ToArray());

        view.Handle(MenuInputAction.Compare);

        Assert.Equal(new MenuHint(MenuInputAction.Compare, "Show compare"), view.Hints.Single(h => h.Action == MenuInputAction.Compare));
    }

    [Fact]
    public void The_sort_hint_names_the_first_column_ascending_when_no_sort_is_active()
    {
        var view = Make();
        view.SortList = ["Name", "Group"];

        Assert.Equal(new MenuHint(MenuInputAction.Sort, "Sort: Name ascending"), view.Hints.Single(h => h.Action == MenuInputAction.Sort));
    }

    [Fact]
    public void Hints_for_an_empty_list_drop_accept_compare_and_favourite()
    {
        var view = Make(0);
        view.SortList = ["Name"];
        view.Sort = 0;
        view.CanCompare = true;
        view.FavLabel = "Track";
        view.Extras = [new MenuHint(MenuInputAction.Search, "Find")];

        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Back, "Back"),
                new MenuHint(MenuInputAction.Sort, "Sort: Name ascending"),
                new MenuHint(MenuInputAction.Search, "Find"),
            },
            view.Hints.ToArray());
    }

    [Fact]
    public void Row_accessors_return_blank_or_false_outside_the_visible_rows()
    {
        var view = Make();
        view.FavouriteIds.Add("i1");

        Assert.Equal("Item03", view.RowText(3));
        Assert.Equal("d-i3", view.RowDetail(3));
        Assert.True(view.RowIsFavourite(1));
        Assert.False(view.RowIsFavourite(0));

        Assert.Equal(string.Empty, view.RowText(-1));
        Assert.Equal(string.Empty, view.RowText(12));
        Assert.Equal(string.Empty, view.RowDetail(-1));
        Assert.Equal(string.Empty, view.RowDetail(12));
        Assert.False(view.RowIsFavourite(-1));
        Assert.False(view.RowIsFavourite(12));
    }

    [Fact]
    public void Search_members_do_nothing_and_leave_every_row_visible()
    {
        var view = Make();
        var changes = 0;
        view.Changed += () => changes++;

        view.SetSearch("Item05");
        view.BeginSearch();
        view.EndSearch(true);

        Assert.Equal(12, view.RowCount);
        Assert.Equal(string.Empty, view.SearchText);
        Assert.False(view.IsSearching);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Refresh_keeps_the_selected_item_by_key_when_rows_before_it_disappear()
    {
        var items = Items(3);
        var view = Make(items);
        view.Select(1);

        items.RemoveAt(0);
        view.Refresh();

        Assert.Equal(0, view.SelectedIndex);
        Assert.Equal("i1", view.Selected!.Id);
    }

    [Fact]
    public void Refresh_clamps_the_cursor_when_the_selected_item_is_gone_and_clears_it_when_the_list_empties()
    {
        var items = Items(3);
        var view = Make(items);
        view.Select(2);

        items.RemoveRange(1, 2);
        view.Refresh();
        Assert.Equal(0, view.SelectedIndex);
        Assert.Equal("i0", view.Selected!.Id);

        items.Clear();
        view.Refresh();
        Assert.Equal(-1, view.SelectedIndex);
        Assert.False(view.HasSelection);
        Assert.Null(view.Selected);
        Assert.Equal("0 shown", view.Summary);
    }
}
