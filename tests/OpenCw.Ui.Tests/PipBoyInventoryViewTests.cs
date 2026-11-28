using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyInventoryViewTests
{
    static InventoryItem[] Items() => new[]
    {
        new InventoryItem("rifle", "Hunting Rifle", "Weapons", 6, 100, Recent: 5, Equippable: true,
                          Stats: new[] { new ItemStat("damage", 20) }),
        new InventoryItem("pistol", "Plain Pistol", "Weapons", 3, 45, Equipped: true, Recent: 2, Equippable: true,
                          Stats: new[] { new ItemStat("damage", 12), new ItemStat("spread", 4, false) }),
        new InventoryItem("tonic", "Tonic", "Aid", 0.5, 40, Count: 5, Recent: 3, Usable: true),
        new InventoryItem("coat", "Leather Coat", "Apparel", 8, 30, Recent: 4, EquipSlot: "body", Equippable: true),
        new InventoryItem("hat", "Cap", "Apparel", 1, 10, Recent: 9, EquipSlot: "head", Equippable: true),
        new InventoryItem("junk", "Scrap Metal", "Misc", 1, 2, Count: 10, Recent: 1),
    };

    static (InventoryListView View, List<InventoryEvent> Events) Make(double capacity = 0)
    {
        var view = new InventoryListView(new InventoryModel(Items(), 8, null, capacity));
        var events = new List<InventoryEvent>();
        view.Command += events.Add;
        return (view, events);
    }

    static string[] Labels(IReadOnlyList<InventoryAction> actions) => actions.Select(a => a.Label).ToArray();

    [Fact]
    public void Sub_tabs_are_all_each_category_then_favourites_and_left_right_cycle_them()
    {
        var (view, _) = Make();

        Assert.Equal(new[] { "All", "Weapons", "Aid", "Apparel", "Misc", "Favourites" }, view.Tabs);
        Assert.Equal(0, view.TabIndex);

        view.SelectTab(1);
        Assert.Equal(1, view.TabIndex);
        Assert.Equal(2, view.RowCount);

        view.SelectTab(5);
        Assert.Equal(5, view.TabIndex);
        Assert.Equal(0, view.RowCount);

        Assert.True(view.Handle(MenuInputAction.Right));
        Assert.Equal(0, view.TabIndex);
        Assert.Equal(6, view.RowCount);
        view.Handle(MenuInputAction.Left);
        Assert.Equal(5, view.TabIndex);
    }

    [Fact]
    public void Sorting_by_the_same_key_twice_flips_direction_and_cycling_walks_the_keys()
    {
        var (view, _) = Make();
        Assert.Equal(0, view.SortIndex);
        Assert.False(view.SortDescending);

        view.SortBy(1);
        Assert.Equal(1, view.SortIndex);
        Assert.Equal("Tonic (5)", view.RowText(0));

        view.SortBy(1);
        Assert.True(view.SortDescending);
        Assert.Equal("Leather Coat", view.RowText(0));

        var (fresh, _) = Make();
        fresh.CycleSort();
        Assert.Equal(0, fresh.SortIndex);
        Assert.True(fresh.SortDescending);
        fresh.CycleSort();
        Assert.Equal(1, fresh.SortIndex);
        Assert.False(fresh.SortDescending);
    }

    [Fact]
    public void Rows_show_counts_and_equipped_state_and_the_detail_column_follows_the_sort()
    {
        var (view, _) = Make();

        Assert.Equal("Cap", view.RowText(0));
        Assert.Equal("Plain Pistol (equipped)", view.RowText(3));
        Assert.Equal("Scrap Metal (10)", view.RowText(4));
        Assert.Equal("6 wt", view.RowDetail(1));
        Assert.Equal("0.5 wt", view.RowDetail(5));
        Assert.Equal(string.Empty, view.RowText(99));

        view.SortBy(2);
        Assert.Equal("2", view.RowDetail(0));
        view.SortBy(2);
        Assert.Equal("100", view.RowDetail(0));
    }

    [Fact]
    public void Accept_offers_the_actions_that_fit_the_item()
    {
        var (view, _) = Make();
        IReadOnlyList<InventoryAction>? offered = null;
        view.ActionsRequested += actions => offered = actions;

        view.Select(1);
        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal(new[] { "Equip", "Drop one", "Add favourite" }, Labels(offered!));

        view.Select(5);
        view.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { "Use", "Drop one", "Drop all (5)", "Add favourite" }, Labels(offered!));

        view.Select(3);
        view.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { "Unequip", "Add favourite" }, Labels(offered!));

        view.Select(4);
        view.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { "Drop one", "Drop...", "Drop all (10)", "Add favourite" }, Labels(offered!));
    }

    [Fact]
    public void Running_an_action_raises_the_command_for_the_host()
    {
        var (view, events) = Make();
        IReadOnlyList<InventoryAction>? offered = null;
        view.ActionsRequested += actions => offered = actions;

        view.Select(1);
        view.Handle(MenuInputAction.Accept);
        Assert.True(view.Run(offered![0]));

        view.Select(4);
        view.Handle(MenuInputAction.Accept);
        Assert.True(view.Run(offered![2]));

        Assert.Equal(new[]
        {
            new InventoryEvent(InventoryEventKind.Equip, "rifle"),
            new InventoryEvent(InventoryEventKind.Drop, "junk", 10),
        }, events);
    }

    [Fact]
    public void Favourite_assigns_a_slot_and_the_star_toggle_releases_it()
    {
        var (view, events) = Make();
        view.Select(1);

        Assert.True(view.Handle(MenuInputAction.Favourite));
        Assert.True(view.RowIsFavourite(1));
        Assert.Contains(new MenuHint(MenuInputAction.Favourite, "Remove favourite"), view.Hints);

        Assert.True(view.ToggleFavouriteAt(1));
        Assert.False(view.RowIsFavourite(1));
        Assert.Equal(new[] { InventoryEventKind.AssignFavourite, InventoryEventKind.UnassignFavourite }, events.Select(e => e.Kind));
    }

    [Fact]
    public void Compare_shows_the_selection_against_the_equipped_item_and_can_be_toggled_off()
    {
        var (view, _) = Make();

        view.Select(1);
        Assert.True(view.IsComparing);
        Assert.Equal("Plain Pistol (equipped)  vs  Hunting Rifle", view.CompareHeading);
        Assert.Equal(4, view.CompareRows.Count);

        view.Select(5);
        Assert.False(view.IsComparing);

        view.Select(1);
        Assert.True(view.Handle(MenuInputAction.Compare));
        Assert.False(view.IsComparing);
        Assert.Contains(new MenuHint(MenuInputAction.Compare, "Show compare"), view.Hints);
        view.Handle(MenuInputAction.Compare);
        Assert.True(view.IsComparing);
    }

    [Fact]
    public void Summary_reports_carried_weight_and_over_encumbrance()
    {
        Assert.Equal("6 shown, carrying 30.5 of 30 (overburdened)", Make(30).View.Summary);
        Assert.Equal("6 shown, carrying 30.5", Make().View.Summary);
        Assert.Equal("6 shown, carrying 30.5 of 100", Make(100).View.Summary);
    }

    [Fact]
    public void Scrolling_follows_the_cursor_and_paging_moves_by_a_page_minus_one()
    {
        var (view, _) = Make();
        var changes = 0;
        view.Changed += () => changes++;

        view.SetViewRows(3);
        view.Select(5);
        Assert.Equal(3, view.ScrollTop);
        view.Select(0);
        Assert.Equal(0, view.ScrollTop);

        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(2, view.SelectedIndex);
        Assert.False(view.Select(99));
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Quick_equip_and_slot_assignment_go_through_the_command_event()
    {
        var (view, events) = Make();
        view.Select(1);

        Assert.True(view.AssignSlot(2));
        Assert.True(view.QuickEquip(2));
        Assert.False(view.QuickEquip(3));

        Assert.Equal(new[] { InventoryEventKind.AssignFavourite, InventoryEventKind.Equip }, events.Select(e => e.Kind));
    }

    [Fact]
    public void Search_selection_through_the_model_is_visible_to_the_view_after_a_refresh()
    {
        var (view, _) = Make();
        view.SelectTab(3);

        Assert.True(view.Inventory.Select("junk"));
        view.Refresh();

        Assert.Equal(0, view.TabIndex);
        Assert.Equal("Scrap Metal (10)", view.RowText(view.SelectedIndex));
    }

    [Fact]
    public void The_drop_action_opens_the_selector_from_the_action_list_without_an_event()
    {
        var (view, events) = Make();
        view.Select(4);
        var drop = view.CurrentActions().Single(action => action.Label == "Drop...");

        Assert.False(view.Run(drop));

        Assert.NotNull(view.Quantity);
        Assert.Equal("junk", view.Quantity!.ItemId);
        Assert.Empty(events);
    }

    [Fact]
    public void Stacks_of_five_or_fewer_get_no_drop_dots_action()
    {
        var (view, _) = Make();
        view.Select(5);

        Assert.DoesNotContain(view.CurrentActions(), action => action.Label == "Drop...");
    }

    [Fact]
    public void An_open_selector_freezes_selection_activation_favourites_tabs_and_sort()
    {
        var (view, events) = Make();
        view.Select(4);
        view.BeginDrop();
        var tab = view.TabIndex;
        var sort = view.SortIndex;
        var shown = view.RowText(view.SelectedIndex);

        Assert.False(view.Select(0));
        Assert.False(view.Activate(0));
        Assert.False(view.ToggleFavouriteAt(0));
        view.SelectTab(2);
        view.CycleTab(1);
        view.SortBy(2);
        view.CycleSort();

        Assert.Equal(shown, view.RowText(view.SelectedIndex));
        Assert.Equal(tab, view.TabIndex);
        Assert.Equal(sort, view.SortIndex);
        Assert.Empty(events);
        Assert.Equal("junk", view.Quantity!.ItemId);
        Assert.Equal(0, view.Inventory.SlotOf("junk"));
    }

    [Fact]
    public void Selection_works_again_once_the_selector_closes()
    {
        var (view, _) = Make();
        view.Select(4);
        view.BeginDrop();
        view.CancelDrop();

        Assert.True(view.Select(0));
    }
}
