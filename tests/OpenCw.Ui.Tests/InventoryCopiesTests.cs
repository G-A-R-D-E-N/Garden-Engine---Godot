using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InventoryCopiesTests
{
    sealed class Host : IInventoryHost
    {
        public List<ItemState> Equipped = new();

        public bool EquipWeapon(string id, ItemState state) { Equipped.Add(state); return true; }
        public void UnequipWeapon() { }
        public bool SpawnDrop(DroppedItem drop) => true;
        public IReadOnlyList<ConsumableEffect>? EffectsOf(string id) => null;
        public bool ApplyEffects(string id, IReadOnlyList<ConsumableEffect> effects) => false;
    }

    static readonly ItemState Modded = new(new[] { "mod_a" });

    const string Rifle = "000001:Test.esm";

    static (InventoryActions Actions, PlayerItems Items, PlayerEquipment Equipment, FakeRecordReader Reader) Make()
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Rifle");
        var items = new PlayerItems();
        var equipment = new PlayerEquipment();
        items.Add(Rifle, ItemState.Plain, 1);
        items.Add(Rifle, Modded, 1);
        return (new InventoryActions(items, new RecordItems(reader), reader, equipment, new Host()), items, equipment, reader);
    }

    // Detects: a modified copy that can never be equipped because Equip always takes the first entry; stepping to the next copy must reach it and then wrap back.
    [Fact]
    public void Next_copy_walks_through_the_distinct_copies_of_one_item()
    {
        var (actions, _, equipment, _) = Make();
        Assert.True(actions.EquipNextCopy(Rifle).Done);
        Assert.Equal(new EquippedWeapon(Rifle, ItemState.Plain), equipment.Weapon);
        Assert.True(actions.EquipNextCopy(Rifle).Done);
        Assert.Equal(new EquippedWeapon(Rifle, Modded), equipment.Weapon);
        Assert.True(actions.EquipNextCopy(Rifle).Done);
        Assert.Equal(new EquippedWeapon(Rifle, ItemState.Plain), equipment.Weapon);
    }

    [Fact]
    public void The_pip_boy_event_for_next_copy_reaches_the_action()
    {
        var (actions, _, equipment, _) = Make();
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Equip, Rifle, NextCopy: true)).Done);
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Equip, Rifle, NextCopy: true)).Done);
        Assert.Equal(Modded, equipment.Weapon!.State);
    }

    // Detects: the row not telling the menu that more than one copy exists, so the Next copy action is never offered; the row must report its copies.
    [Fact]
    public void A_row_with_two_copies_offers_the_next_copy_event()
    {
        var (_, items, equipment, reader) = Make();
        var rows = PlayerInventoryItems.Build(items.Stacks, new RecordItems(reader), equipment, reader, items: items);
        var model = new InventoryModel(rows);
        Assert.Equal(2, Assert.Single(rows).Copies);
        model.Select(Rifle);
        Assert.Equal(new InventoryEvent(InventoryEventKind.Equip, Rifle, NextCopy: true), model.EquipNextCopy());
    }

    // Detects: a transfer that moves a customized copy while a plain one is still carried; the plain copy must go first.
    [Fact]
    public void A_transfer_moves_the_plain_copy_before_the_modified_one()
    {
        var items = new PlayerItems();
        items.Add(Rifle, Modded, 1);
        items.Add(Rifle, ItemState.Plain, 1);
        var target = new PlayerItems();
        Assert.True(items.MoveTo(target, Rifle, 1));
        Assert.Equal(1, target.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Modded));
    }
}
