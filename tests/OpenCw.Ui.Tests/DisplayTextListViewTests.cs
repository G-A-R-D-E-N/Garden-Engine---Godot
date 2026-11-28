using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DisplayTextListViewTests
{
    static readonly string[] Items = { "alpha", "beta", "gamma" };

    static (ListMenuModel<string> Model, DisplayTextListView<string> View) Make()
    {
        var model = new ListMenuModel<string>(Items, s => s, s => s);
        return (model, new DisplayTextListView<string>(model, s => s.ToUpperInvariant() + "!"));
    }

    [Fact]
    public void Row_text_comes_from_the_display_function_and_out_of_range_rows_are_empty()
    {
        var (_, view) = Make();

        Assert.Equal("ALPHA!", view.RowText(0));
        Assert.Equal("GAMMA!", view.RowText(2));
        Assert.Equal("", view.RowText(3));
        Assert.Equal("", view.RowText(-1));
    }

    [Fact]
    public void Row_text_follows_the_filtered_rows_of_the_inner_model()
    {
        var (model, view) = Make();

        model.SetSearch("be");

        Assert.Equal(1, view.RowCount);
        Assert.Equal("BETA!", view.RowText(0));
        Assert.Equal("", view.RowText(1));
    }

    [Fact]
    public void The_summary_override_wins_and_a_null_or_missing_one_falls_back_to_the_inner_summary()
    {
        var (_, view) = Make();
        Assert.Equal("3 of 3 shown", view.Summary);

        view.SummaryText = () => "custom";
        Assert.Equal("custom", view.Summary);

        view.SummaryText = () => null!;
        Assert.Equal("3 of 3 shown", view.Summary);

        view.SummaryText = null;
        Assert.Equal("3 of 3 shown", view.Summary);
    }

    [Fact]
    public void Changed_is_forwarded_from_the_inner_model_until_the_handler_is_removed()
    {
        var (model, view) = Make();
        var raised = 0;
        void Count() => raised++;

        view.Changed += Count;
        model.Select(1);
        Assert.Equal(1, raised);

        view.Changed -= Count;
        model.Select(2);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Selection_scroll_and_view_size_pass_through()
    {
        var (_, view) = Make();

        Assert.Equal(10, view.ViewRows);
        view.SetViewRows(2);
        Assert.Equal(2, view.ViewRows);
        Assert.False(view.Select(3));
        Assert.True(view.Select(2));
        Assert.Equal(2, view.SelectedIndex);
        Assert.Equal(1, view.ScrollTop);
        Assert.True(view.Handle(MenuInputAction.Up));
        Assert.Equal(1, view.SelectedIndex);
        Assert.False(view.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void Activate_selects_the_row_and_raises_accepted_on_the_inner_model()
    {
        var (model, view) = Make();
        var accepted = new List<string>();
        model.Accepted += accepted.Add;

        Assert.True(view.Activate(1));
        Assert.False(view.Activate(7));

        Assert.Equal(new[] { "beta" }, accepted);
        Assert.Equal(1, view.SelectedIndex);
    }

    [Fact]
    public void Search_state_tabs_and_sorting_pass_through()
    {
        var (_, view) = Make();
        Assert.Equal(new[] { "All", "Favourites" }, view.Tabs.ToArray());
        Assert.Empty(view.SortNames);
        Assert.Equal(-1, view.SortIndex);
        Assert.False(view.SortDescending);

        view.BeginSearch();
        Assert.True(view.IsSearching);
        view.SetSearch("al");
        Assert.Equal("al", view.SearchText);
        Assert.Equal(1, view.RowCount);
        view.EndSearch(true);
        Assert.False(view.IsSearching);
        Assert.Equal("", view.SearchText);
        Assert.Equal(3, view.RowCount);

        view.SelectTab(1);
        Assert.Equal(1, view.TabIndex);
        Assert.Equal(0, view.RowCount);
        view.SortBy(0);
        Assert.Equal(-1, view.SortIndex);
    }

    [Fact]
    public void Favouriting_goes_through_the_inner_model_and_the_compare_state_is_idle()
    {
        var (_, view) = Make();

        Assert.True(view.ToggleFavouriteAt(1));

        Assert.True(view.RowIsFavourite(1));
        Assert.False(view.RowIsFavourite(0));
        Assert.Equal(1, view.SelectedIndex);
        Assert.False(view.IsComparing);
        Assert.Equal("", view.CompareHeading);
        Assert.Empty(view.CompareRows);
    }

    [Fact]
    public void Hints_are_the_inner_models_hints()
    {
        var (_, view) = Make();

        Assert.Equal(
            new[] { "Select", "Back", "Search", "Favourite", "Previous: Favourites", "Next: Favourites" },
            view.Hints.Select(h => h.Label).ToArray());
    }
}
