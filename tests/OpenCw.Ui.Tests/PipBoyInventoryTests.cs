using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyInventoryTests
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

    static InventoryModel Make(int slots = 8, IFavouriteStore? store = null) => new(Items(), slots, store);

    static string[] Ids(InventoryModel model) => model.View.Visible.Select(i => i.Id).ToArray();

    [Fact]
    public void Default_order_is_name_ascending_ignoring_case()
    {
        Assert.Equal(new[] { "hat", "rifle", "coat", "pistol", "junk", "tonic" }, Ids(Make()));
    }

    [Fact]
    public void Each_sort_key_orders_by_its_own_field_with_a_sensible_default_direction()
    {
        var model = Make();

        model.SetSort(InventorySort.Weight);
        Assert.Equal(new[] { "tonic", "hat", "junk", "pistol", "rifle", "coat" }, Ids(model));

        model.SetSort(InventorySort.Value);
        Assert.Equal(new[] { "rifle", "pistol", "tonic", "coat", "hat", "junk" }, Ids(model));

        model.SetSort(InventorySort.ValuePerWeight);
        Assert.Equal(new[] { "tonic", "rifle", "pistol", "hat", "coat", "junk" }, Ids(model));

        model.SetSort(InventorySort.Recent);
        Assert.Equal(new[] { "hat", "rifle", "coat", "tonic", "pistol", "junk" }, Ids(model));

        model.SetSort(InventorySort.Recent, false);
        Assert.Equal(new[] { "junk", "pistol", "tonic", "coat", "rifle", "hat" }, Ids(model));
    }

    [Fact]
    public void Value_per_weight_handles_weightless_items_without_dividing_by_zero()
    {
        Assert.Equal(double.PositiveInfinity, new InventoryItem("a", "A", "Misc", 0, 5).ValuePerWeight);
        Assert.Equal(0.0, new InventoryItem("b", "B", "Misc", 0, 0).ValuePerWeight);
        Assert.Equal(4.0, new InventoryItem("c", "C", "Misc", 2, 8).ValuePerWeight);
    }

    [Fact]
    public void Category_filter_text_filter_and_category_cycling_narrow_the_list()
    {
        var model = Make();

        model.SetCategory("Weapons");
        Assert.Equal(new[] { "rifle", "pistol" }, Ids(model));

        model.SetCategory(null);
        model.SetSearch("apparel");
        Assert.Equal(new[] { "hat", "coat" }, Ids(model));

        model.SetSearch("rifle");
        Assert.Equal(new[] { "rifle" }, Ids(model));

        model.SetSearch(null);
        Assert.Equal(new[] { "Weapons", "Aid", "Apparel", "Misc" }, model.Categories);
    }

    [Fact]
    public void Burden_sums_weight_times_count_and_reports_over_encumbrance()
    {
        var model = Make();
        model.Capacity = 30;

        Assert.Equal(30.5, model.Burden.Carried);
        Assert.True(model.Burden.IsOverEncumbered);
        Assert.Equal(-0.5, model.Burden.Remaining);

        model.Capacity = 100;
        Assert.False(model.Burden.IsOverEncumbered);
        Assert.Equal(69.5, model.Burden.Remaining);

        model.SetCount("junk", 4);
        Assert.Equal(24.5, model.Burden.Carried);
    }

    [Fact]
    public void Compare_shows_candidate_against_the_equipped_item_of_the_same_slot()
    {
        var model = Make();
        model.Select("rifle");

        var view = model.CompareWithEquipped();

        Assert.NotNull(view);
        Assert.Equal(new[] { "damage", "spread", "Weight", "Value" }, view!.Rows.Select(r => r.Name));
        var damage = view.Rows[0];
        Assert.Equal(12.0, damage.Left);
        Assert.Equal(20.0, damage.Right);
        Assert.True(damage.RightIsBetter);
        var spread = view.Rows[1];
        Assert.Equal(4.0, spread.Left);
        Assert.Equal(0.0, spread.Right);
        Assert.True(spread.RightIsBetter);
        Assert.True(view.Rows[2].RightIsWorse);
        Assert.True(view.Rows[3].RightIsBetter);
    }

    [Fact]
    public void Compare_is_absent_for_equipped_items_non_equippables_and_slots_with_nothing_worn()
    {
        var model = Make();

        model.Select("pistol");
        Assert.Null(model.CompareWithEquipped());
        model.Select("tonic");
        Assert.Null(model.CompareWithEquipped());
        model.Select("coat");
        Assert.Null(model.CompareWithEquipped());
        model.SetEquipped("hat", true);
        Assert.Null(model.CompareWithEquipped());
    }

    [Fact]
    public void Equipping_replaces_the_item_worn_in_the_same_slot_only()
    {
        var model = Make();
        model.SetEquipped("hat", true);

        model.SetEquipped("rifle", true);

        Assert.True(model.Items.Single(i => i.Id == "rifle").Equipped);
        Assert.False(model.Items.Single(i => i.Id == "pistol").Equipped);
        Assert.True(model.Items.Single(i => i.Id == "hat").Equipped);
    }

    [Fact]
    public void Commands_are_plain_events_and_do_not_change_the_inventory()
    {
        var model = Make();

        model.Select("rifle");
        Assert.Equal(new InventoryEvent(InventoryEventKind.Equip, "rifle"), model.Equip());
        Assert.False(model.Items.Single(i => i.Id == "rifle").Equipped);

        model.Select("pistol");
        Assert.Equal(new InventoryEvent(InventoryEventKind.Unequip, "pistol"), model.Unequip());
        Assert.Null(model.Equip());

        model.Select("tonic");
        Assert.Equal(new InventoryEvent(InventoryEventKind.Use, "tonic"), model.Use());
        Assert.Null(model.Equip());

        model.Select("junk");
        Assert.Null(model.Use());
        Assert.Equal(5, model.Items.Single(i => i.Id == "tonic").Count);
    }

    [Fact]
    public void Drop_clamps_the_count_and_refuses_equipped_items_and_non_positive_counts()
    {
        var model = Make();
        model.Select("junk");

        Assert.Equal(new InventoryEvent(InventoryEventKind.Drop, "junk", 3), model.Drop(3));
        Assert.Equal(new InventoryEvent(InventoryEventKind.Drop, "junk", 10), model.Drop(99));
        Assert.Equal(new InventoryEvent(InventoryEventKind.Drop, "junk", 1), model.Drop());
        Assert.Null(model.Drop(0));
        Assert.Null(model.Drop(-2));

        model.Select("pistol");
        Assert.Null(model.Drop());
    }

    [Fact]
    public void Removing_the_last_of_an_item_drops_it_from_the_list_and_frees_its_favourite_slot()
    {
        var model = Make();
        model.Select("tonic");
        model.AssignFavourite(2);

        model.SetCount("tonic", 0);

        Assert.DoesNotContain("tonic", Ids(model));
        Assert.Null(model.ItemInSlot(2));
        Assert.Equal(0, model.SlotOf("tonic"));
        Assert.Equal("junk", model.View.Current!.Id);
        Assert.Equal("junk", model.CurrentKey);
    }

    [Fact]
    public void Assigning_a_favourite_moves_it_between_slots_and_replaces_the_occupant()
    {
        var model = Make();
        model.Select("tonic");

        Assert.Equal(new InventoryEvent(InventoryEventKind.AssignFavourite, "tonic", 1, 3), model.AssignFavourite(3));
        Assert.Equal(3, model.SlotOf("tonic"));

        model.AssignFavourite(1);
        Assert.Equal(1, model.SlotOf("tonic"));
        Assert.Null(model.ItemInSlot(3));

        model.Select("rifle");
        model.AssignFavourite(1);
        Assert.Equal("rifle", model.ItemInSlot(1)?.Id);
        Assert.Equal(0, model.SlotOf("tonic"));

        Assert.Equal(new InventoryEvent(InventoryEventKind.UnassignFavourite, "rifle", 1, 1), model.UnassignFavourite(1));
        Assert.Null(model.UnassignFavourite(1));
    }

    [Fact]
    public void Favourite_slots_reject_out_of_range_numbers()
    {
        var model = Make(slots: 4);

        Assert.Null(model.AssignFavourite(0));
        Assert.Null(model.AssignFavourite(5));
        Assert.Null(model.AssignFavourite(-1));
        Assert.Null(model.ItemInSlot(0));
        Assert.Null(model.ItemInSlot(5));
        Assert.Equal(4, model.SlotCount);
    }

    [Fact]
    public void Toggling_a_favourite_takes_the_first_free_slot_then_releases_it_and_stops_when_full()
    {
        var model = Make(slots: 1);

        model.Select("rifle");
        Assert.Equal(1, model.ToggleFavouriteCurrent()?.Slot);
        model.Select("tonic");
        Assert.Null(model.ToggleFavouriteCurrent());
        Assert.Equal(0, model.SlotOf("tonic"));

        model.Select("rifle");
        Assert.Equal(InventoryEventKind.UnassignFavourite, model.ToggleFavouriteCurrent()?.Kind);
        Assert.Equal(0, model.SlotOf("rifle"));
    }

    [Fact]
    public void Favourites_only_shows_just_the_slotted_items_and_follows_later_changes()
    {
        var model = Make();
        model.Select("tonic");
        model.AssignFavourite(1);
        model.Select("rifle");
        model.AssignFavourite(2);

        model.SetFavouritesOnly(true);
        Assert.Equal(new[] { "rifle", "tonic" }, Ids(model));

        model.UnassignFavourite(2);
        Assert.Equal(new[] { "tonic" }, Ids(model));

        model.SetFavouritesOnly(false);
        Assert.Equal(6, Ids(model).Length);
    }

    [Fact]
    public void Quick_equip_equips_unequips_or_uses_according_to_what_is_in_the_slot()
    {
        var model = Make();
        model.Select("rifle");
        model.AssignFavourite(1);
        model.Select("pistol");
        model.AssignFavourite(2);
        model.Select("tonic");
        model.AssignFavourite(3);
        model.Select("junk");
        model.AssignFavourite(4);

        Assert.Equal(new InventoryEvent(InventoryEventKind.Equip, "rifle"), model.QuickEquip(1));
        Assert.Equal(new InventoryEvent(InventoryEventKind.Unequip, "pistol"), model.QuickEquip(2));
        Assert.Equal(new InventoryEvent(InventoryEventKind.Use, "tonic"), model.QuickEquip(3));
        Assert.Null(model.QuickEquip(4));
        Assert.Null(model.QuickEquip(5));
    }

    [Fact]
    public void Favourite_slots_persist_through_the_store_and_garbage_entries_are_ignored()
    {
        var store = new InMemoryFavouriteStore();
        var first = Make(store: store);
        first.Select("tonic");
        first.AssignFavourite(2);

        Assert.Equal(2, Make(store: store).SlotOf("tonic"));

        var dirty = new InMemoryFavouriteStore(new[] { "x", "0:a", "99:b", "abc:c", ":d", "2:tonic", "3:" });
        var loaded = Make(store: dirty);
        Assert.Equal(2, loaded.SlotOf("tonic"));
        Assert.Equal(0, loaded.SlotOf("a"));
        Assert.Equal(0, loaded.SlotOf("b"));
        Assert.Equal(0, loaded.SlotOf("c"));
        Assert.Equal(0, loaded.SlotOf("d"));
    }

    [Fact]
    public void Selecting_a_filtered_out_item_clears_the_filters_so_search_jumps_work()
    {
        var model = Make();
        model.SetCategory("Aid");
        model.SetSearch("tonic");

        Assert.True(model.Select("rifle"));

        Assert.Null(model.Category);
        Assert.Equal("rifle", model.CurrentKey);
        Assert.False(model.Select("nope"));
    }

    [Fact]
    public void Global_search_hits_cover_every_item_not_just_the_visible_ones()
    {
        var model = Make();
        model.SetCategory("Aid");

        var hits = model.Search(ListSearch.Tokenize("scrap"));

        var hit = Assert.Single(hits);
        Assert.Equal(new PipBoySearchHit(PipBoyTab.Inventory, "junk", "Scrap Metal", "Misc x10"), hit);
    }

    [Fact]
    public void Upsert_adds_new_items_and_replaces_existing_ones_by_id()
    {
        var model = Make();

        model.Upsert(new InventoryItem("cap", "Round Token", "Misc", 0, 1, Count: 50));
        model.Upsert(new InventoryItem("junk", "Scrap Metal", "Misc", 1, 2, Count: 12));

        Assert.Contains("cap", Ids(model));
        Assert.Equal(12, model.Items.Single(i => i.Id == "junk").Count);
        Assert.Equal(7, model.Items.Count);
    }

    [Fact]
    public void Negative_slot_count_is_treated_as_no_slots()
    {
        var model = Make(slots: -3);

        Assert.Equal(0, model.SlotCount);
        model.Select("rifle");
        Assert.Null(model.ToggleFavouriteCurrent());
    }
}
