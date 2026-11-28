using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlainListModelTests
{
    static PlainListModel<string> Make(params string[] rows)
    {
        var model = new PlainListModel<string>(row => row, row => row.ToUpperInvariant(), row => "detail " + row);
        model.SetRows(rows);
        return model;
    }

    [Fact]
    public void Rows_start_on_the_first_and_text_and_detail_come_from_the_callbacks()
    {
        var model = Make("a", "b");

        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal("B", model.RowText(1));
        Assert.Equal("detail b", model.RowDetail(1));
        Assert.Equal(string.Empty, model.RowText(5));
    }

    [Fact]
    public void Up_and_down_clamp_and_accept_reports_the_selected_row()
    {
        var model = Make("a", "b");
        var accepted = new List<string>();
        model.Accepted += accepted.Add;

        Assert.True(model.Handle(MenuInputAction.Up));
        Assert.Equal(0, model.SelectedIndex);
        Assert.True(model.Handle(MenuInputAction.Down));
        Assert.True(model.Handle(MenuInputAction.Down));
        Assert.Equal(1, model.SelectedIndex);
        Assert.True(model.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { "b" }, accepted);
    }

    [Fact]
    public void Tabs_sorting_and_search_actions_are_not_handled()
    {
        var model = Make("a");

        Assert.False(model.Handle(MenuInputAction.TabNext));
        Assert.False(model.Handle(MenuInputAction.Sort));
        Assert.False(model.Handle(MenuInputAction.Search));
        Assert.False(model.Handle(MenuInputAction.Compare));
        Assert.False(model.Handle(MenuInputAction.Favourite));
    }

    [Fact]
    public void Setting_rows_keeps_the_selection_by_key_and_an_empty_list_has_none()
    {
        var model = Make("a", "b", "c");
        model.Select(2);

        model.SetRows(new[] { "x", "c", "a" });
        Assert.Equal("c", model.Selected);

        model.SetRows(new string[0]);
        Assert.False(model.HasSelection);
        Assert.False(model.Handle(MenuInputAction.Accept));
    }

    [Fact]
    public void Replacing_rows_selects_the_first_even_when_the_old_cursor_was_further_down()
    {
        var model = Make("a", "b", "c");
        model.Select(2);

        model.ReplaceRows(new[] { "x", "y" });

        Assert.Equal("x", model.Selected);
    }

    [Fact]
    public void Activate_selects_then_accepts_and_refuses_an_index_out_of_range()
    {
        var model = Make("a", "b");
        string? accepted = null;
        model.Accepted += row => accepted = row;

        Assert.True(model.Activate(1));
        Assert.Equal("b", accepted);
        Assert.False(model.Activate(9));
    }

    [Fact]
    public void Changing_the_summary_raises_changed_once_and_the_same_value_does_not()
    {
        var model = Make("a");
        var raised = 0;
        model.Changed += () => raised++;

        model.Summary = "text";
        model.Summary = "text";

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Short_counts_use_a_k_suffix_from_ten_thousand_and_have_need_marks_a_shortfall()
    {
        Assert.Equal("9999", CountText.Short(9999));
        Assert.Equal("12k", CountText.Short(12000));
        Assert.Equal("12.5k", CountText.Short(12500));
        Assert.Equal("Wood 3/5 short", CountText.HaveNeed("Wood", 3, 5));
        Assert.Equal("Wood 5/5", CountText.HaveNeed("Wood", 5, 5));
    }
}
