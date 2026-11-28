using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlainListModelViewTests
{
    static PlainListModel<string> Make(int count, int viewRows = 10)
    {
        var model = new PlainListModel<string>(row => row, row => row);
        model.SetRows(Enumerable.Range(0, count).Select(i => "r" + i));
        model.SetViewRows(viewRows);
        return model;
    }

    [Fact]
    public void Missing_callbacks_and_row_sources_are_refused_by_name()
    {
        Assert.Equal("keyOf", Assert.Throws<ArgumentNullException>(() => new PlainListModel<string>(null!, r => r)).ParamName);
        Assert.Equal("textOf", Assert.Throws<ArgumentNullException>(() => new PlainListModel<string>(r => r, null!)).ParamName);
        var model = Make(1);
        Assert.Equal("rows", Assert.Throws<ArgumentNullException>(() => model.SetRows(null!)).ParamName);
        Assert.Equal("rows", Assert.Throws<ArgumentNullException>(() => model.ReplaceRows(null!)).ParamName);
    }

    [Fact]
    public void An_empty_model_has_no_selection_and_the_fixed_view_state_of_a_plain_list()
    {
        var model = new PlainListModel<string>(row => row, row => row);

        Assert.Equal(0, model.RowCount);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.False(model.HasSelection);
        Assert.Null(model.Selected);
        Assert.Equal(10, model.ViewRows);
        Assert.Equal(0, model.ScrollTop);
        Assert.Equal("", model.Summary);
        Assert.Equal("", model.SearchText);
        Assert.False(model.IsSearching);
        Assert.Empty(model.Tabs);
        Assert.Equal(0, model.TabIndex);
        Assert.Empty(model.SortNames);
        Assert.Equal(-1, model.SortIndex);
        Assert.False(model.SortDescending);
        Assert.False(model.IsComparing);
        Assert.Equal("", model.CompareHeading);
        Assert.Empty(model.CompareRows);
        Assert.Empty(model.Hints);
    }

    [Fact]
    public void Movement_on_an_empty_list_is_swallowed_but_accept_and_other_actions_are_not()
    {
        var model = new PlainListModel<string>(row => row, row => row);
        var raised = 0;
        model.Changed += () => raised++;

        Assert.True(model.Handle(MenuInputAction.Up));
        Assert.True(model.Handle(MenuInputAction.Down));
        Assert.True(model.Handle(MenuInputAction.PageUp));
        Assert.True(model.Handle(MenuInputAction.PageDown));
        Assert.False(model.Handle(MenuInputAction.Accept));
        Assert.False(model.Handle(MenuInputAction.Back));
        Assert.False(model.Handle(MenuInputAction.Left));
        Assert.False(model.Handle(MenuInputAction.Right));

        Assert.Equal(-1, model.SelectedIndex);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Page_keys_move_one_row_less_than_the_view_and_clamp_at_both_ends()
    {
        var model = Make(30, viewRows: 5);

        model.Handle(MenuInputAction.PageDown);
        Assert.Equal(4, model.SelectedIndex);
        model.Handle(MenuInputAction.PageDown);
        Assert.Equal(8, model.SelectedIndex);
        model.Handle(MenuInputAction.PageUp);
        Assert.Equal(4, model.SelectedIndex);
        model.Handle(MenuInputAction.PageUp);
        model.Handle(MenuInputAction.PageUp);
        Assert.Equal(0, model.SelectedIndex);

        model.Select(27);
        model.Handle(MenuInputAction.PageDown);
        Assert.Equal(29, model.SelectedIndex);
    }

    [Fact]
    public void A_one_row_view_still_pages_by_one()
    {
        var model = Make(5, viewRows: 1);

        model.Handle(MenuInputAction.PageDown);

        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void The_scroll_position_follows_the_cursor_down_and_back_up()
    {
        var model = Make(30, viewRows: 5);

        model.Select(4);
        Assert.Equal(0, model.ScrollTop);
        model.Select(5);
        Assert.Equal(1, model.ScrollTop);
        model.Select(29);
        Assert.Equal(25, model.ScrollTop);
        model.Select(0);
        Assert.Equal(0, model.ScrollTop);

        model.Select(10);
        Assert.Equal(6, model.ScrollTop);
        model.Select(8);
        Assert.Equal(6, model.ScrollTop);
        model.Select(5);
        Assert.Equal(5, model.ScrollTop);
    }

    [Fact]
    public void Resizing_the_view_keeps_the_cursor_visible_and_the_scroll_within_range()
    {
        var model = Make(30);
        model.Select(29);
        Assert.Equal(20, model.ScrollTop);

        model.SetViewRows(5);
        Assert.Equal(25, model.ScrollTop);

        model.SetViewRows(40);
        Assert.Equal(0, model.ScrollTop);
    }

    [Fact]
    public void The_view_size_is_at_least_one_and_an_unchanged_size_raises_nothing()
    {
        var model = Make(3);
        var raised = 0;
        model.Changed += () => raised++;

        model.SetViewRows(10);
        Assert.Equal(0, raised);

        model.SetViewRows(0);
        Assert.Equal(1, model.ViewRows);
        Assert.Equal(1, raised);

        model.SetViewRows(-4);
        Assert.Equal(1, model.ViewRows);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Setting_rows_whose_selected_key_is_gone_keeps_the_old_position_clamped_into_range()
    {
        var model = new PlainListModel<string>(row => row, row => row);
        model.SetRows(new[] { "a", "b", "c" });
        model.Select(2);

        model.SetRows(new[] { "x", "y" });

        Assert.Equal(1, model.SelectedIndex);
        Assert.Equal("y", model.Selected);
    }

    [Fact]
    public void An_explicit_select_key_wins_over_the_current_selection_and_a_missing_one_falls_back_to_the_old_position()
    {
        var model = new PlainListModel<string>(row => row, row => row);
        model.SetRows(new[] { "a", "b", "c" });
        model.Select(2);

        model.SetRows(new[] { "x", "c", "a" }, "x");
        Assert.Equal(0, model.SelectedIndex);

        model.Select(1);
        model.SetRows(new[] { "p", "q", "r" }, "zzz");
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void Setting_fewer_rows_pulls_the_scroll_back_with_the_clamped_cursor()
    {
        var model = Make(30);
        model.Select(29);

        model.SetRows(Enumerable.Range(0, 3).Select(i => "n" + i));

        Assert.Equal(2, model.SelectedIndex);
        Assert.Equal(0, model.ScrollTop);
    }

    [Fact]
    public void Replacing_rows_resets_the_cursor_and_scroll_and_an_empty_replacement_clears_the_selection()
    {
        var model = Make(30);
        model.Select(29);
        var raised = 0;
        model.Changed += () => raised++;

        model.ReplaceRows(Enumerable.Range(0, 12).Select(i => "n" + i));
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal(0, model.ScrollTop);
        Assert.Equal(1, raised);

        model.ReplaceRows(new string[0]);
        Assert.Equal(-1, model.SelectedIndex);
        Assert.False(model.HasSelection);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Show_rows_replaces_without_a_key_and_keeps_the_keyed_row_with_one()
    {
        var model = new PlainListModel<string>(row => row, row => row);
        model.SetRows(new[] { "a", "b", "c" });
        model.Select(2);

        model.ShowRows(new[] { "x", "c", "y" }, null);
        Assert.Equal(0, model.SelectedIndex);

        model.ShowRows(new[] { "x", "c", "y" }, "c");
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void Select_key_finds_a_row_and_refuses_an_unknown_one_and_reselecting_raises_nothing()
    {
        var model = Make(5);
        var raised = 0;
        model.Changed += () => raised++;

        Assert.True(model.SelectKey("r3"));
        Assert.Equal(3, model.SelectedIndex);
        Assert.Equal(1, raised);

        Assert.True(model.SelectKey("r3"));
        Assert.Equal(1, raised);

        Assert.False(model.SelectKey("nope"));
        Assert.Equal(3, model.SelectedIndex);
        Assert.False(model.Select(5));
        Assert.False(model.Select(-1));
    }

    [Fact]
    public void Activating_the_selected_row_accepts_it_and_an_unsubscribed_accept_is_harmless()
    {
        var model = Make(3);
        Assert.True(model.Handle(MenuInputAction.Accept));

        var accepted = new List<string>();
        model.Accepted += accepted.Add;

        Assert.True(model.Activate(0));
        Assert.Equal(new[] { "r0" }, accepted);
    }

    [Fact]
    public void A_null_summary_reads_back_empty_and_raises_only_when_the_text_actually_changes()
    {
        var model = Make(1);
        var raised = 0;
        model.Changed += () => raised++;

        model.Summary = null!;
        Assert.Equal(0, raised);

        model.Summary = "x";
        model.Summary = null!;
        Assert.Equal("", model.Summary);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Detail_is_empty_without_a_detail_callback_or_outside_the_rows_and_the_search_tab_sort_and_favourite_calls_do_nothing()
    {
        var model = Make(3);
        var raised = 0;
        model.Changed += () => raised++;

        Assert.Equal("", model.RowDetail(0));
        Assert.Equal("", model.RowText(-1));
        Assert.Equal("", model.RowText(3));
        Assert.False(model.RowIsFavourite(0));
        Assert.False(model.ToggleFavouriteAt(0));
        model.SetSearch("r1");
        model.BeginSearch();
        model.EndSearch(true);
        model.SelectTab(1);
        model.SortBy(0);

        Assert.Equal("", model.SearchText);
        Assert.False(model.IsSearching);
        Assert.Equal(3, model.RowCount);
        Assert.Equal(0, model.TabIndex);
        Assert.Equal(-1, model.SortIndex);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Detail_comes_from_the_callback_only_for_rows_in_range()
    {
        var model = new PlainListModel<string>(row => row, row => row, row => "d:" + row);
        model.SetRows(new[] { "a", "b" });

        Assert.Equal("d:a", model.RowDetail(0));
        Assert.Equal("d:b", model.RowDetail(1));
        Assert.Equal("", model.RowDetail(2));
        Assert.Equal("", model.RowDetail(-1));
    }
}
