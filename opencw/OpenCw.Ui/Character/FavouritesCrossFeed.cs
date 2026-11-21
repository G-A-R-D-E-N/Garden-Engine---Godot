using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Character;

public static class FavouritesCrossFeed
{
    public static IReadOnlyList<FavoriteEntry?> Slots(InventoryModel inventory)
    {
        var slots = new FavoriteEntry?[FavouritesCrossModel.SlotCount];
        for (var slot = 1; slot <= Math.Min(inventory.SlotCount, slots.Length); slot++)
            if (inventory.ItemInSlot(slot) is { } item)
                slots[slot - 1] = new FavoriteEntry(item.Name, item.Count, KeyLabel: FavouritesCrossModel.LabelOf(slot), Equipped: item.Equipped);
        return slots;
    }
}
