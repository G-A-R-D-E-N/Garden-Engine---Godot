using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuListTests
{
    sealed record Row(string Id, string Name, string Category, double Weight);

    static readonly Row[] Rows =
    {
        new("a", "Alpha Rifle", "Guns", 4),
        new("b", "Bravo Pistol", "Guns", 2),
        new("c", "Charlie Hat", "Apparel", 1),
        new("d", "Delta Rifle", "Guns", 5),
        new("e", "Echo Vest", "Apparel", 3),
    };

    static readonly SortKey<Row> ByWeight = SortKey<Row>.By("Weight", r => r.Weight);
    static readonly SortKey<Row> ByCategory = SortKey<Row>.By("Category", r => r.Category);

    static MenuList<Row> Make(IFavouriteStore? store = null) =>
        new MenuList<Row>(Rows, r => r.Id, r => r.Name, r => r.Category, store);

    static string[] Ids(MenuList<Row> list) => list.Visible.Select(r => r.Id).ToArray();

    [Fact]
    public void Selection_follows_its_item_when_the_list_is_resorted_not_the_row_index()
    {
        var list = Make();
        list.Move(CursorMove.Down);
        list.Move(CursorMove.Down);
        Assert.Equal("c", list.Current?.Id);

        list.SetSort(ByWeight);

        Assert.Equal(0, list.CursorIndex);
        Assert.Equal("c", list.Current?.Id);
    }

    [Fact]
    public void Selection_falls_back_to_the_nearest_row_when_its_item_is_filtered_out()
    {
        var list = Make();
        list.Move(CursorMove.Down);
        list.Move(CursorMove.Down);
        list.Move(CursorMove.Down);
        Assert.Equal("d", list.Current?.Id);

        list.SetCategory("Apparel");

        Assert.Equal(new[] { "c", "e" }, Ids(list));
        Assert.Equal(1, list.CursorIndex);
        Assert.Equal("e", list.Current?.Id);
    }

    [Fact]
    public void Favourites_added_and_removed_round_trip_through_the_store()
    {
        var store = new InMemoryFavouriteStore();
        var first = Make(store);
        first.Move(CursorMove.Down);
        Assert.True(first.ToggleFavouriteCurrent());

        var reloaded = Make(store);
        Assert.True(reloaded.IsFavourite(Rows[1]));
        Assert.False(reloaded.IsFavourite(Rows[0]));

        reloaded.Move(CursorMove.Down);
        Assert.False(reloaded.ToggleFavouriteCurrent());
        Assert.False(Make(store).IsFavourite(Rows[1]));
    }

    [Fact]
    public void Search_splits_on_whitespace_and_matches_every_token_case_insensitively()
    {
        var list = Make();

        list.SetSearch("  RIFLE   alp ");
        Assert.Equal(new[] { "a" }, Ids(list));

        list.SetSearch("ifl");
        Assert.Equal(new[] { "a", "d" }, Ids(list));

        list.SetSearch("   ");
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Ids(list));
    }

    [Fact]
    public void Up_and_down_clamp_at_the_ends_by_default_and_wrap_only_when_asked()
    {
        var clamped = Make();
        clamped.Move(CursorMove.Up);
        Assert.Equal(0, clamped.CursorIndex);
        clamped.Move(CursorMove.End);
        clamped.Move(CursorMove.Down);
        Assert.Equal(4, clamped.CursorIndex);

        var wrapping = Make();
        wrapping.Wrap = true;
        wrapping.Move(CursorMove.Up);
        Assert.Equal(4, wrapping.CursorIndex);
        wrapping.Move(CursorMove.Down);
        Assert.Equal(0, wrapping.CursorIndex);
    }

    [Fact]
    public void Descending_sort_keeps_source_order_for_equal_keys_instead_of_reversing_ties()
    {
        var list = Make();

        list.SetSort(ByCategory);
        Assert.Equal(new[] { "c", "e", "a", "b", "d" }, Ids(list));

        list.SetSort(ByCategory, descending: true);
        Assert.Equal(new[] { "a", "b", "d", "c", "e" }, Ids(list));
    }

    [Fact]
    public void Next_category_jumps_to_the_first_row_of_the_next_group_and_stops_without_wrap()
    {
        var list = Make();
        list.SetSort(ByCategory);
        list.Move(CursorMove.Home);
        Assert.Equal("c", list.Current?.Id);

        list.Move(CursorMove.NextCategory);
        Assert.Equal("a", list.Current?.Id);

        list.Move(CursorMove.NextCategory);
        Assert.Equal("a", list.Current?.Id);

        list.Wrap = true;
        list.Move(CursorMove.NextCategory);
        Assert.Equal("c", list.Current?.Id);
    }

    [Fact]
    public void Favourites_only_filter_is_anded_with_the_search_instead_of_replacing_it()
    {
        var list = Make();
        list.ToggleFavouriteCurrent();
        list.Move(CursorMove.End);
        list.ToggleFavouriteCurrent();

        list.SetFavouritesOnly(true);
        Assert.Equal(new[] { "a", "e" }, Ids(list));

        list.SetSearch("rifle");
        Assert.Equal(new[] { "a" }, Ids(list));
    }

    [Fact]
    public void Mouse_selection_moves_the_same_cursor_the_keyboard_uses()
    {
        var list = Make();

        Assert.True(list.SelectVisible(3));
        Assert.Equal("d", list.Current?.Id);

        list.Move(CursorMove.Up);
        Assert.Equal("c", list.Current?.Id);

        Assert.False(list.SelectVisible(9));
        Assert.Equal("c", list.Current?.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_page_size_below_one_is_clamped_so_paging_still_moves(int pageSize)
    {
        var list = Make();
        list.PageSize = pageSize;
        Assert.Equal(1, list.PageSize);

        list.Move(CursorMove.PageDown);
        Assert.Equal(1, list.CursorIndex);
        list.Move(CursorMove.PageDown);
        Assert.Equal(2, list.CursorIndex);
        list.Move(CursorMove.PageUp);
        Assert.Equal(1, list.CursorIndex);
    }
}
