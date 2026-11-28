using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyListViewDefaultsTests
{
    sealed class Bare : PipBoyListView<string>
    {
        public Bare(params string[] items)
            : base(new MenuList<string>(items, s => s, s => s))
        {
        }

        protected override string TextOf(string item) => item;
    }

    [Fact]
    public void A_view_that_overrides_only_the_row_text_has_one_all_tab_no_sorts_and_no_search()
    {
        var view = new Bare("a", "b", "c");

        Assert.Equal(new[] { "All" }, view.Tabs);
        Assert.Equal(0, view.TabIndex);
        Assert.Empty(view.SortNames);
        Assert.Equal(-1, view.SortIndex);
        Assert.False(view.SortDescending);
        Assert.Equal("", view.SearchText);
        Assert.False(view.IsSearching);
        Assert.Equal("3 shown", view.Summary);
    }

    [Fact]
    public void With_a_selection_the_hints_are_select_then_back_and_without_one_only_back()
    {
        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Select"),
                new MenuHint(MenuInputAction.Back, "Back"),
            },
            new Bare("a").Hints);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Back, "Back") }, new Bare().Hints);
    }

    [Fact]
    public void Rows_read_their_text_and_have_no_detail_and_are_never_favourites()
    {
        var view = new Bare("a", "b");

        Assert.Equal("b", view.RowText(1));
        Assert.Equal("", view.RowDetail(1));
        Assert.False(view.RowIsFavourite(1));
        Assert.Equal("", view.RowText(-1));
        Assert.Equal("", view.RowText(2));
        Assert.Equal("", view.RowDetail(2));
        Assert.False(view.RowIsFavourite(-1));
    }

    [Fact]
    public void There_is_no_compare_panel_by_default()
    {
        var view = new Bare("a");

        Assert.False(view.IsComparing);
        Assert.Equal("", view.CompareHeading);
        Assert.Empty(view.CompareRows);
        Assert.False(view.Handle(MenuInputAction.Compare));
    }

    [Theory]
    [InlineData(MenuInputAction.Accept)]
    [InlineData(MenuInputAction.Favourite)]
    [InlineData(MenuInputAction.Compare)]
    [InlineData(MenuInputAction.Sort)]
    [InlineData(MenuInputAction.Back)]
    [InlineData(MenuInputAction.Search)]
    public void Actions_the_view_does_not_implement_are_not_handled(MenuInputAction action)
    {
        var view = new Bare("a", "b");
        var changes = 0;
        view.Changed += () => changes++;

        Assert.False(view.Handle(action));

        Assert.Equal(0, changes);
        Assert.Equal(0, view.SelectedIndex);
    }

    [Fact]
    public void Tab_keys_are_handled_but_change_nothing_with_a_single_tab()
    {
        var view = new Bare("a", "b");
        var changes = 0;
        view.Changed += () => changes++;

        Assert.True(view.Handle(MenuInputAction.Left));
        Assert.True(view.Handle(MenuInputAction.Right));
        view.SelectTab(0);
        view.SelectTab(1);
        view.SortBy(0);
        view.CycleSort();

        Assert.Equal(0, changes);
        Assert.Equal(0, view.TabIndex);
    }

    [Fact]
    public void Activating_or_favouriting_a_row_selects_it_even_though_the_view_does_nothing_with_it()
    {
        var view = new Bare("a", "b", "c");

        Assert.False(view.Activate(1));
        Assert.Equal(1, view.SelectedIndex);
        Assert.Equal("b", view.Selected);

        Assert.False(view.ToggleFavouriteAt(2));
        Assert.Equal(2, view.SelectedIndex);

        Assert.False(view.Activate(3));
        Assert.Equal(2, view.SelectedIndex);
    }

    [Fact]
    public void Moving_the_cursor_raises_changed_once_per_move()
    {
        var view = new Bare("a", "b", "c");
        var changes = 0;
        view.Changed += () => changes++;

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(1, view.SelectedIndex);
        Assert.Equal(1, changes);

        Assert.True(view.Handle(MenuInputAction.Up));
        Assert.Equal(0, view.SelectedIndex);
        Assert.Equal(2, changes);
    }
}
