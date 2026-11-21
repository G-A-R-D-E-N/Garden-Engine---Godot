using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class FavouritesCrossFeedTests
{
    static InventoryModel Model() => new(new[]
    {
        new InventoryItem("a", "Pipe Pistol", "Weapons", 2, 10, Equipped: true, Equippable: true),
        new InventoryItem("b", "Stimpak", "Aid", 0, 5, Count: 3, Usable: true),
    });

    [Fact]
    public void Assigned_favourites_fill_their_cells_with_the_key_label()
    {
        // Detects: saved favourites that must show in the live cross at their slot with the item name, count and key label.
        var model = Model();
        Assert.True(model.Select("a"));
        model.AssignFavourite(3);
        var slots = FavouritesCrossFeed.Slots(model);
        Assert.Equal(FavouritesCrossModel.SlotCount, slots.Count);
        Assert.Equal(new FavoriteEntry("Pipe Pistol", 1, KeyLabel: "3", Equipped: true), slots[2]);
        Assert.Single(slots.Where(s => s is not null));
    }

    [Fact]
    public void A_used_slot_maps_to_the_item_command()
    {
        // Detects: a used cross slot that must equip, unequip or use the favourite through InventoryModel.QuickEquip.
        var model = Model();
        Assert.True(model.Select("a"));
        model.AssignFavourite(1);
        Assert.Equal(new InventoryEvent(InventoryEventKind.Unequip, "a"), model.QuickEquip(1));
        Assert.Null(model.QuickEquip(2));
    }
}
