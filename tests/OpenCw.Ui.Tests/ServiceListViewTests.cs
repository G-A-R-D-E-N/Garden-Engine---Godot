using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceListViewTests
{
    sealed record Row(string Id, string Name, double Weight);

    static readonly Row[] Rows =
    {
        new("a", "Alpha Rifle", 4),
        new("b", "Bravo Pistol", 2),
        new("c", "Charlie Hat", 1),
    };

    static ListMenuModel<Row> Model() =>
        new(Rows, r => r.Id, r => r.Name, null, r => r.Weight.ToString("0.##"), null,
            new[] { SortKey<Row>.By("Name", r => r.Name), SortKey<Row>.By("Weight", r => r.Weight) },
            new[] { new CompareStat<Row>("Weight", r => r.Weight, false) });

    static ServiceListView<Row> Make(out ListMenuModel<Row> model)
    {
        model = Model();
        return new ServiceListView<Row>(model, r => r.Name + "!");
    }

    static string[] Ids(ListMenuModel<Row> model) => model.Rows.Select(r => r.Id).ToArray();

    [Fact]
    public void Row_text_goes_through_the_display_function_and_detail_through_the_model()
    {
        var view = Make(out _);

        Assert.Equal("Alpha Rifle!", view.RowText(0));
        Assert.Equal("Charlie Hat!", view.RowText(2));
        Assert.Equal("2", view.RowDetail(1));
        Assert.Equal(string.Empty, view.RowText(-1));
        Assert.Equal(string.Empty, view.RowText(3));
        Assert.Equal(string.Empty, view.RowDetail(3));
    }

    [Fact]
    public void The_view_has_no_tabs_and_no_favourites_even_when_the_model_does()
    {
        var view = Make(out var model);
        model.ToggleFavourite();
        Assert.True(model.RowIsFavourite(0));

        view.SelectTab(1);

        Assert.Empty(view.Tabs);
        Assert.Equal(0, view.TabIndex);
        Assert.Equal(0, model.TabIndex);
        Assert.False(view.RowIsFavourite(0));
        Assert.False(view.ToggleFavouriteAt(1));
        Assert.False(model.RowIsFavourite(1));
    }

    [Fact]
    public void Comparing_is_always_off_even_when_the_model_has_a_pin_to_compare_against()
    {
        var view = Make(out var model);
        model.TogglePin();
        model.Select(1);
        Assert.True(model.IsComparing);

        Assert.False(view.IsComparing);
        Assert.Equal(string.Empty, view.CompareHeading);
        Assert.Empty(view.CompareRows);
    }

    [Fact]
    public void Selection_search_sort_and_window_state_come_from_the_model()
    {
        var view = Make(out var model);

        Assert.Equal(3, view.RowCount);
        Assert.True(view.Select(2));
        Assert.Equal(2, view.SelectedIndex);
        Assert.Equal("c", view.Selected!.Id);
        Assert.False(view.Select(3));
        Assert.False(view.Select(-1));

        view.SetViewRows(2);
        Assert.Equal(2, view.ViewRows);
        Assert.Equal(1, view.ScrollTop);

        view.SetSearch("pistol");
        Assert.Equal("pistol", view.SearchText);
        Assert.Equal(new[] { "b" }, Ids(model));
        Assert.Equal(1, view.RowCount);

        Assert.Equal(new[] { "Name", "Weight" }, view.SortNames.ToArray());
        view.SortBy(1);
        Assert.Equal(1, view.SortIndex);
        Assert.False(view.SortDescending);
        view.SortBy(1);
        Assert.True(view.SortDescending);
    }

    [Fact]
    public void The_sort_label_names_the_active_sort_and_direction()
    {
        var view = Make(out _);
        Assert.Equal("Sort", view.SortLabel);

        view.SortBy(0);
        Assert.Equal("Sort: Name ascending", view.SortLabel);

        view.SortBy(0);
        Assert.Equal("Sort: Name descending", view.SortLabel);

        view.SortBy(1);
        Assert.Equal("Sort: Weight ascending", view.SortLabel);
    }

    [Fact]
    public void The_summary_is_the_model_summary_unless_a_summary_text_is_supplied()
    {
        var view = Make(out _);
        Assert.Equal("3 of 3 shown", view.Summary);

        view.SummaryText = () => "custom";
        Assert.Equal("custom", view.Summary);

        view.SummaryText = null;
        Assert.Equal("3 of 3 shown", view.Summary);
    }

    [Fact]
    public void Hints_come_from_the_model_unless_a_hint_supplier_is_set_and_the_view_is_not_searching()
    {
        var view = Make(out _);

        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Select"),
                new MenuHint(MenuInputAction.Back, "Back"),
                new MenuHint(MenuInputAction.Search, "Search"),
                new MenuHint(MenuInputAction.Sort, "Sort"),
                new MenuHint(MenuInputAction.Compare, "Compare"),
                new MenuHint(MenuInputAction.Favourite, "Favourite"),
                new MenuHint(MenuInputAction.TabPrev, "Previous: Favourites"),
                new MenuHint(MenuInputAction.TabNext, "Next: Favourites"),
            },
            view.Hints.ToArray());

        var custom = new[] { new MenuHint(MenuInputAction.Accept, "Open") };
        view.HintsOf = () => custom;
        Assert.Equal(custom, view.Hints.ToArray());

        view.BeginSearch();
        Assert.True(view.IsSearching);
        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Done"), new MenuHint(MenuInputAction.Back, "Clear search") },
            view.Hints.ToArray());

        view.EndSearch(false);
        Assert.Equal(custom, view.Hints.ToArray());
    }

    [Fact]
    public void Activating_a_row_selects_it_ends_the_search_and_returns_what_the_extra_handler_says_for_accept()
    {
        var view = Make(out var model);
        var seen = new List<MenuInputAction>();
        view.Extra = action =>
        {
            seen.Add(action);
            return true;
        };
        view.BeginSearch();

        Assert.True(view.Activate(1));

        Assert.Equal(1, view.SelectedIndex);
        Assert.False(view.IsSearching);
        Assert.Equal(new[] { MenuInputAction.Accept }, seen);

        view.Extra = _ => false;
        Assert.False(view.Activate(2));
        Assert.Equal(2, model.SelectedIndex);
    }

    [Fact]
    public void Activating_a_missing_row_or_without_an_extra_handler_does_not_accept()
    {
        var view = Make(out _);
        var calls = 0;

        Assert.False(view.Activate(0));

        view.Extra = _ =>
        {
            calls++;
            return true;
        };
        Assert.False(view.Activate(3));
        Assert.False(view.Activate(-1));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void The_extra_handler_sees_a_key_first_and_can_consume_it()
    {
        var view = Make(out _);
        var seen = new List<MenuInputAction>();
        view.Extra = action =>
        {
            seen.Add(action);
            return action == MenuInputAction.Down;
        };

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(0, view.SelectedIndex);

        Assert.True(view.Handle(MenuInputAction.Up));
        Assert.Equal(new[] { MenuInputAction.Down, MenuInputAction.Up }, seen);
    }

    [Fact]
    public void A_key_the_extra_handler_declines_falls_through_to_the_model()
    {
        var view = Make(out var model);
        view.Extra = _ => false;
        Accepted(model, out var accepted);

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(1, view.SelectedIndex);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal(new[] { "b" }, accepted);

        Assert.False(view.Handle(MenuInputAction.Left));
    }

    [Fact]
    public void Without_an_extra_handler_keys_go_straight_to_the_model()
    {
        var view = Make(out var model);

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(1, model.SelectedIndex);

        Assert.True(view.Handle(MenuInputAction.Sort));
        Assert.Equal(0, model.SortIndex);
    }

    [Fact]
    public void Tab_favourite_and_compare_keys_are_offered_to_the_extra_handler_but_never_reach_the_model()
    {
        var view = Make(out var model);
        var seen = new List<MenuInputAction>();
        view.Extra = action =>
        {
            seen.Add(action);
            return false;
        };

        Assert.False(view.Handle(MenuInputAction.TabPrev));
        Assert.False(view.Handle(MenuInputAction.TabNext));
        Assert.False(view.Handle(MenuInputAction.Favourite));
        Assert.False(view.Handle(MenuInputAction.Compare));

        Assert.Equal(
            new[] { MenuInputAction.TabPrev, MenuInputAction.TabNext, MenuInputAction.Favourite, MenuInputAction.Compare },
            seen);
        Assert.Equal(0, model.TabIndex);
        Assert.False(model.RowIsFavourite(0));
        Assert.False(model.HasPin);

        view.Extra = action => action == MenuInputAction.Compare;
        Assert.True(view.Handle(MenuInputAction.Compare));
        Assert.False(model.HasPin);
    }

    [Fact]
    public void While_searching_every_key_goes_to_the_model_and_the_extra_handler_is_not_asked()
    {
        var view = Make(out var model);
        var calls = 0;
        view.Extra = _ =>
        {
            calls++;
            return true;
        };
        view.SetSearch("a");
        view.BeginSearch();

        Assert.True(view.Handle(MenuInputAction.Down));
        Assert.Equal(1, model.SelectedIndex);
        Assert.True(view.Handle(MenuInputAction.TabNext));
        Assert.True(view.IsSearching);

        Assert.True(view.Handle(MenuInputAction.Back));
        Assert.False(view.IsSearching);
        Assert.Equal(string.Empty, view.SearchText);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Back_with_a_search_text_clears_it_without_asking_the_extra_handler()
    {
        var view = Make(out var model);
        var calls = 0;
        view.Extra = _ =>
        {
            calls++;
            return true;
        };
        view.SetSearch("pistol");
        Assert.False(view.IsSearching);
        Assert.Equal(new[] { "b" }, Ids(model));

        Assert.True(view.Handle(MenuInputAction.Back));

        Assert.Equal(string.Empty, view.SearchText);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(model));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Back_with_no_search_text_is_offered_to_the_extra_handler_and_then_left_unhandled()
    {
        var view = Make(out _);
        var seen = new List<MenuInputAction>();
        view.Extra = action =>
        {
            seen.Add(action);
            return false;
        };

        Assert.False(view.Handle(MenuInputAction.Back));
        Assert.Equal(new[] { MenuInputAction.Back }, seen);

        view.Extra = _ => true;
        Assert.True(view.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void The_search_key_starts_a_search_when_the_extra_handler_declines_it()
    {
        var view = Make(out _);
        view.Extra = _ => false;

        Assert.True(view.Handle(MenuInputAction.Search));

        Assert.True(view.IsSearching);
    }

    [Fact]
    public void Changed_is_the_models_event_and_can_be_unsubscribed()
    {
        var view = Make(out _);
        var changes = 0;
        void Count() => changes++;

        view.Changed += Count;
        view.Select(1);
        Assert.Equal(1, changes);

        view.Changed -= Count;
        view.Select(2);
        Assert.Equal(1, changes);
    }

    static void Accepted(ListMenuModel<Row> model, out List<string> accepted)
    {
        var list = new List<string>();
        model.Accepted += row => list.Add(row.Id);
        accepted = list;
    }
}
