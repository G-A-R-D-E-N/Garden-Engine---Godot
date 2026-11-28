using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ListMenuModelTests
{
    sealed record Row(string Id, string Name, string Category, double Weight, double Damage);

    static readonly Row[] Rows =
    {
        new("a", "Alpha Rifle", "Guns", 4, 10),
        new("b", "Bravo Pistol", "Guns", 2, 6),
        new("c", "Charlie Hat", "Apparel", 1, 0),
        new("d", "Delta Rifle", "Guns", 5, 12),
        new("e", "Echo Vest", "Apparel", 3, 0),
    };

    static ListMenuModel<Row> Make(IFavouriteStore? store = null, IReadOnlyList<Row>? items = null) =>
        new(items ?? Rows, r => r.Id, r => r.Name, r => r.Category, r => r.Weight.ToString("0.##"), store,
            new[] { SortKey<Row>.By("Name", r => r.Name), SortKey<Row>.By("Weight", r => r.Weight) },
            new[]
            {
                new CompareStat<Row>("Weight", r => r.Weight, false),
                new CompareStat<Row>("Damage", r => r.Damage, true),
            });

    static string[] Ids(ListMenuModel<Row> model) => model.Rows.Select(r => r.Id).ToArray();

    static string[] Labels(ListMenuModel<Row> model, MenuInputAction action) =>
        model.Hints.Where(h => h.Action == action).Select(h => h.Label).ToArray();

    [Fact]
    public void A_new_model_shows_every_row_with_the_first_selected_and_the_tab_strip_built_from_categories()
    {
        var model = Make();

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Ids(model));
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal(new[] { "All", "Guns", "Apparel", "Favourites" }, model.Tabs.ToArray());
        Assert.Equal(0, model.TabIndex);
        Assert.Equal("Alpha Rifle", model.RowText(0));
        Assert.Equal("4", model.RowDetail(0));
        Assert.Equal("", model.RowText(99));
    }

    [Fact]
    public void Search_filters_rows_and_clearing_it_restores_them()
    {
        var model = Make();
        var changes = 0;
        model.Changed += () => changes++;

        model.SetSearch("rifle");

        Assert.Equal(new[] { "a", "d" }, Ids(model));
        Assert.Equal("rifle", model.SearchText);
        Assert.Equal(1, changes);

        model.SetSearch("");

        Assert.Equal(5, model.RowCount);
    }

    [Fact]
    public void Setting_the_same_search_text_does_not_raise_changed()
    {
        var model = Make();
        model.SetSearch("rifle");
        var changes = 0;
        model.Changed += () => changes++;

        model.SetSearch("rifle");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Back_clears_search_text_first_and_only_then_falls_through()
    {
        var model = Make();
        model.SetSearch("hat");

        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.Equal("", model.SearchText);
        Assert.Equal(5, model.RowCount);
        Assert.False(model.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void Search_action_starts_editing_and_accept_keeps_the_text_while_back_discards_it()
    {
        var model = Make();

        Assert.True(model.Handle(MenuInputAction.Search));
        Assert.True(model.IsSearching);
        model.SetSearch("vest");
        Assert.True(model.Handle(MenuInputAction.Accept));
        Assert.False(model.IsSearching);
        Assert.Equal("vest", model.SearchText);
        Assert.Equal(new[] { "e" }, Ids(model));

        model.Handle(MenuInputAction.Search);
        Assert.True(model.Handle(MenuInputAction.Back));

        Assert.False(model.IsSearching);
        Assert.Equal("", model.SearchText);
        Assert.Equal(5, model.RowCount);
    }

    [Fact]
    public void While_editing_search_only_navigation_and_exit_actions_do_anything()
    {
        var model = Make();
        model.Handle(MenuInputAction.Search);

        Assert.True(model.Handle(MenuInputAction.Sort));
        Assert.True(model.Handle(MenuInputAction.Favourite));
        Assert.True(model.Handle(MenuInputAction.TabNext));
        Assert.Equal(-1, model.SortIndex);
        Assert.Equal(0, model.TabIndex);
        Assert.False(model.RowIsFavourite(0));

        model.Handle(MenuInputAction.Down);
        Assert.Equal(1, model.SelectedIndex);
        Assert.Equal(new[] { MenuInputAction.Accept, MenuInputAction.Back }, model.Hints.Select(h => h.Action).ToArray());
    }

    [Fact]
    public void Sort_action_cycles_each_key_ascending_then_descending_then_back_to_source_order()
    {
        var model = Make();

        model.Handle(MenuInputAction.Sort);
        Assert.Equal((0, false), (model.SortIndex, model.SortDescending));
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Ids(model));

        model.Handle(MenuInputAction.Sort);
        Assert.Equal((0, true), (model.SortIndex, model.SortDescending));
        Assert.Equal(new[] { "e", "d", "c", "b", "a" }, Ids(model));

        model.Handle(MenuInputAction.Sort);
        Assert.Equal((1, false), (model.SortIndex, model.SortDescending));
        Assert.Equal(new[] { "c", "b", "e", "a", "d" }, Ids(model));

        model.Handle(MenuInputAction.Sort);
        Assert.Equal((1, true), (model.SortIndex, model.SortDescending));
        Assert.Equal(new[] { "d", "a", "e", "b", "c" }, Ids(model));

        model.Handle(MenuInputAction.Sort);
        Assert.Equal(-1, model.SortIndex);
        Assert.Null(model.SortName);
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Ids(model));
    }

    [Fact]
    public void Clicking_a_sort_header_sorts_then_toggles_direction_then_flips_back()
    {
        var model = Make();

        model.SortBy(1);
        Assert.Equal((1, false), (model.SortIndex, model.SortDescending));
        model.SortBy(1);
        Assert.Equal((1, true), (model.SortIndex, model.SortDescending));
        model.SortBy(1);
        Assert.Equal((1, false), (model.SortIndex, model.SortDescending));
        model.SortBy(0);
        Assert.Equal((0, false), (model.SortIndex, model.SortDescending));
        model.SortBy(7);
        Assert.Equal(0, model.SortIndex);
    }

    [Fact]
    public void Resorting_keeps_the_selected_item_selected()
    {
        var model = Make();
        model.Select(3);
        Assert.Equal("d", model.Selected?.Id);

        model.SortBy(1);

        Assert.Equal("d", model.Selected?.Id);
        Assert.Equal(4, model.SelectedIndex);
    }

    [Fact]
    public void Favourite_toggles_the_selected_row_and_persists_through_the_store()
    {
        var store = new InMemoryFavouriteStore();
        var model = Make(store);
        model.Handle(MenuInputAction.Down);
        model.Handle(MenuInputAction.Down);

        Assert.True(model.Handle(MenuInputAction.Favourite));

        Assert.True(model.RowIsFavourite(2));
        Assert.False(model.RowIsFavourite(1));
        Assert.Equal(new[] { "c" }, store.Load().ToArray());
        Assert.Equal(new[] { "Unfavourite" }, Labels(model, MenuInputAction.Favourite));

        model.Handle(MenuInputAction.Favourite);

        Assert.Empty(store.Load());
        Assert.Equal(new[] { "Favourite" }, Labels(model, MenuInputAction.Favourite));
    }

    [Fact]
    public void Favourites_saved_earlier_are_shown_on_a_new_model_and_in_the_favourites_tab()
    {
        var store = new InMemoryFavouriteStore(new[] { "b", "e" });
        var model = Make(store);

        Assert.True(model.RowIsFavourite(1));
        Assert.True(model.RowIsFavourite(4));

        model.SelectTab(model.Tabs.Count - 1);

        Assert.True(model.FavouritesOnly);
        Assert.Equal(new[] { "b", "e" }, Ids(model));
    }

    [Fact]
    public void Unfavouriting_the_last_favourite_in_the_favourites_tab_leaves_an_empty_list_with_no_selection()
    {
        var model = Make(new InMemoryFavouriteStore(new[] { "c" }));
        model.SelectTab(model.Tabs.Count - 1);

        Assert.True(model.Handle(MenuInputAction.Favourite));

        Assert.Equal(0, model.RowCount);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.False(model.HasSelection);
        Assert.False(model.Handle(MenuInputAction.Accept));
        Assert.False(model.Handle(MenuInputAction.Favourite));
        Assert.Empty(Labels(model, MenuInputAction.Accept));
        Assert.Empty(Labels(model, MenuInputAction.Favourite));
    }

    [Fact]
    public void Tab_actions_cycle_all_then_each_category_then_favourites_and_wrap()
    {
        var model = Make();

        model.Handle(MenuInputAction.TabNext);
        Assert.Equal("Guns", model.Category);
        Assert.Equal(new[] { "a", "b", "d" }, Ids(model));

        model.Handle(MenuInputAction.TabNext);
        Assert.Equal("Apparel", model.Category);
        Assert.Equal(new[] { "c", "e" }, Ids(model));

        model.Handle(MenuInputAction.TabNext);
        Assert.True(model.FavouritesOnly);
        Assert.Null(model.Category);

        model.Handle(MenuInputAction.TabNext);
        Assert.Equal(0, model.TabIndex);
        Assert.Equal(5, model.RowCount);

        model.Handle(MenuInputAction.TabPrev);
        Assert.True(model.FavouritesOnly);
    }

    [Fact]
    public void Search_and_category_combine()
    {
        var model = Make();
        model.SelectTab(1);

        model.SetSearch("rifle");

        Assert.Equal(new[] { "a", "d" }, Ids(model));
        model.SetSearch("pistol");
        Assert.Equal(new[] { "b" }, Ids(model));
    }

    [Fact]
    public void Compare_pins_the_selected_item_and_compares_it_with_whatever_is_selected_next()
    {
        var model = Make();

        Assert.True(model.Handle(MenuInputAction.Compare));
        Assert.True(model.HasPin);
        Assert.False(model.IsComparing);
        Assert.Empty(model.CompareRows);

        model.Handle(MenuInputAction.Down);

        Assert.True(model.IsComparing);
        Assert.Equal("Alpha Rifle  vs  Bravo Pistol", model.CompareHeading);
        var rows = model.CompareRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Weight", rows[0].Name);
        Assert.Equal((4.0, 2.0), (rows[0].Left, rows[0].Right));
        Assert.True(rows[0].RightIsBetter);
        Assert.Equal("Damage", rows[1].Name);
        Assert.Equal((10.0, 6.0), (rows[1].Left, rows[1].Right));
        Assert.True(rows[1].RightIsWorse);
    }

    [Fact]
    public void Compare_on_the_pinned_item_unpins_and_on_another_item_moves_the_pin()
    {
        var model = Make();
        model.Handle(MenuInputAction.Compare);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(new[] { "Move compare" }, Labels(model, MenuInputAction.Compare));

        model.Handle(MenuInputAction.Compare);

        Assert.Equal("b", model.PinnedItem?.Id);
        Assert.False(model.IsComparing);
        Assert.Equal(new[] { "Clear compare" }, Labels(model, MenuInputAction.Compare));

        model.Handle(MenuInputAction.Compare);

        Assert.False(model.HasPin);
        Assert.Equal(new[] { "Compare" }, Labels(model, MenuInputAction.Compare));
    }

    [Fact]
    public void Back_clears_the_compare_pin_before_it_closes_the_menu()
    {
        var model = Make();
        model.Handle(MenuInputAction.Compare);
        model.Handle(MenuInputAction.Down);

        Assert.True(model.Handle(MenuInputAction.Back));

        Assert.False(model.HasPin);
        Assert.False(model.IsComparing);
        Assert.False(model.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void The_compare_pin_survives_filtering_the_pinned_item_out_of_view()
    {
        var model = Make();
        model.Handle(MenuInputAction.Compare);
        model.SetSearch("vest");

        Assert.Equal(new[] { "e" }, Ids(model));
        Assert.True(model.IsComparing);
        Assert.Equal("Alpha Rifle  vs  Echo Vest", model.CompareHeading);
    }

    [Fact]
    public void Without_compare_stats_the_compare_hint_is_not_offered()
    {
        var model = new ListMenuModel<Row>(Rows, r => r.Id, r => r.Name);

        Assert.Empty(Labels(model, MenuInputAction.Compare));
        Assert.Empty(Labels(model, MenuInputAction.Sort));
    }

    [Fact]
    public void Hints_reflect_search_text_sort_and_neighbouring_tabs()
    {
        var model = Make();
        Assert.Equal(new[] { "Back" }, Labels(model, MenuInputAction.Back));
        Assert.Equal(new[] { "Select" }, Labels(model, MenuInputAction.Accept));
        Assert.Equal(new[] { "Sort" }, Labels(model, MenuInputAction.Sort));
        Assert.Equal(new[] { "Previous: Favourites" }, Labels(model, MenuInputAction.TabPrev));
        Assert.Equal(new[] { "Next: Guns" }, Labels(model, MenuInputAction.TabNext));

        model.SetSearch("x");
        model.Handle(MenuInputAction.Sort);

        Assert.Equal(new[] { "Clear search" }, Labels(model, MenuInputAction.Back));
        Assert.Equal(new[] { "Sort: Name ascending" }, Labels(model, MenuInputAction.Sort));
    }

    [Fact]
    public void Accept_raises_accepted_with_the_selected_item_and_activate_selects_first()
    {
        var model = Make();
        var accepted = new List<string>();
        model.Accepted += item => accepted.Add(item.Id);

        Assert.True(model.Handle(MenuInputAction.Accept));
        Assert.True(model.Activate(3));
        Assert.False(model.Activate(99));

        Assert.Equal(new[] { "a", "d" }, accepted.ToArray());
        Assert.Equal(3, model.SelectedIndex);
    }

    [Fact]
    public void Activating_a_row_while_searching_ends_the_search_edit_and_keeps_the_text()
    {
        var model = Make();
        model.Handle(MenuInputAction.Search);
        model.SetSearch("rifle");

        Assert.True(model.Activate(1));

        Assert.False(model.IsSearching);
        Assert.Equal("rifle", model.SearchText);
    }

    [Fact]
    public void Scrolling_keeps_the_selected_row_inside_the_view_and_paging_moves_by_one_less_than_the_view()
    {
        var model = Make();
        model.SetViewRows(2);
        Assert.Equal(0, model.ScrollTop);

        model.Handle(MenuInputAction.Down);
        Assert.Equal(0, model.ScrollTop);
        model.Handle(MenuInputAction.Down);
        Assert.Equal(1, model.ScrollTop);

        model.Handle(MenuInputAction.PageDown);
        Assert.Equal(3, model.SelectedIndex);
        Assert.Equal(2, model.ScrollTop);

        model.Handle(MenuInputAction.PageDown);
        Assert.Equal(4, model.SelectedIndex);
        Assert.Equal(3, model.ScrollTop);

        model.Handle(MenuInputAction.PageUp);
        model.Handle(MenuInputAction.PageUp);
        model.Handle(MenuInputAction.PageUp);
        model.Handle(MenuInputAction.PageUp);
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal(0, model.ScrollTop);
    }

    [Fact]
    public void Shrinking_the_result_list_pulls_the_scroll_position_back()
    {
        var model = Make();
        model.SetViewRows(2);
        model.Select(4);
        Assert.Equal(3, model.ScrollTop);

        model.SetSearch("rifle");

        Assert.Equal(0, model.ScrollTop);
        Assert.InRange(model.SelectedIndex, 0, 1);
    }

    [Fact]
    public void Left_and_right_are_left_for_the_embedding_menu()
    {
        var model = Make();

        Assert.False(model.Handle(MenuInputAction.Left));
        Assert.False(model.Handle(MenuInputAction.Right));
    }

    [Fact]
    public void Select_rejects_out_of_range_rows_and_does_not_notify_for_the_current_row()
    {
        var model = Make();
        var changes = 0;
        model.Changed += () => changes++;

        Assert.False(model.Select(-1));
        Assert.False(model.Select(5));
        Assert.True(model.Select(0));

        Assert.Equal(0, changes);
        Assert.True(model.Select(2));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Refresh_picks_up_new_source_rows_and_categories()
    {
        var items = new List<Row>(Rows);
        var model = Make(items: items);
        model.SelectTab(1);

        items.Add(new Row("f", "Foxtrot Helmet", "Armor", 2, 0));
        model.Refresh();

        Assert.Equal(new[] { "All", "Guns", "Apparel", "Armor", "Favourites" }, model.Tabs.ToArray());
        Assert.Equal("Guns", model.Category);
        Assert.Equal(new[] { "a", "b", "d" }, Ids(model));
    }

    [Fact]
    public void Summary_describes_the_visible_rows_sort_and_search()
    {
        var model = Make();
        Assert.Equal("5 of 5 shown", model.Summary);

        model.SetSearch("rifle");
        model.SortBy(1);

        Assert.Equal("2 of 5 shown, sorted by Weight (ascending), search \"rifle\"", model.Summary);
    }

    [Fact]
    public void Toggle_favourite_at_selects_the_row_first()
    {
        var model = Make();

        Assert.True(model.ToggleFavouriteAt(3));

        Assert.Equal(3, model.SelectedIndex);
        Assert.True(model.RowIsFavourite(3));
        Assert.False(model.ToggleFavouriteAt(42));
    }
}
