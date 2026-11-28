using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InventoryActionsTests
{
    sealed class Host : IInventoryHost
    {
        public bool WeaponOk = true;
        public bool ApplyOk = true;
        public Dictionary<string, IReadOnlyList<ConsumableEffect>> Effects = new();
        public List<string> Equipped = new();
        public int Unequips;
        public List<DroppedItem> Drops = new();
        public int SpawnLimit = int.MaxValue;
        public List<string> Applied = new();

        public bool EquipWeapon(string id, ItemState state) { if (WeaponOk) Equipped.Add(id); return WeaponOk; }
        public void UnequipWeapon() => Unequips++;
        public bool SpawnDrop(DroppedItem drop)
        {
            if (Drops.Count >= SpawnLimit) return false;
            Drops.Add(drop);
            return true;
        }
        public IReadOnlyList<ConsumableEffect>? EffectsOf(string id) => Effects.GetValueOrDefault(id);
        public bool ApplyEffects(string id, IReadOnlyList<ConsumableEffect> effects) { if (ApplyOk) Applied.Add(id); return ApplyOk; }
    }

    static readonly ItemState Modded = new(new[] { "mod_a" });

    const string Rifle = "000001:Test.esm", Pistol = "000002:Test.esm", Helmet = "000003:Test.esm", Hat = "000004:Test.esm",
        Chest = "000005:Test.esm", Stimpak = "000006:Test.esm", Junk = "000007:Test.esm", Steel = "000008:Test.esm", Mystery = "000009:Test.esm";

    static (InventoryActions Actions, PlayerItems Items, PlayerEquipment Equipment, Host Host, FakeRecordReader Reader) Make()
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Rifle");
        reader.Add(Pistol, "WEAP", name: "Pistol");
        reader.Add(Helmet, "ARMO", name: "Helmet").Scalars["ARMO\\FirstPersonFlags"] = "1";
        reader.Add(Hat, "ARMO", name: "Hat").Scalars["ARMO\\FirstPersonFlags"] = "3";
        reader.Add(Chest, "ARMO", name: "Chest").Scalars["ARMO\\FirstPersonFlags"] = "4";
        reader.Add(Stimpak, "ALCH", name: "Stimpak");
        reader.Add(Junk, "MISC", name: "Junk");
        reader.Add(Steel, "CMPO", name: "Steel");
        reader.Add(Mystery, "ARMO", name: "Mystery");
        var items = new PlayerItems();
        var equipment = new PlayerEquipment();
        var host = new Host();
        return (new InventoryActions(items, new RecordItems(reader), reader, equipment, host), items, equipment, host, reader);
    }

    [Fact]
    public void EquipWeaponGoesThroughHostAndRecordsEquipment()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, Modded, 1);
        Assert.True(actions.Equip(Rifle).Done);
        Assert.Equal(new[] { Rifle }, host.Equipped);
        Assert.Equal(new EquippedWeapon(Rifle, Modded), equipment.Weapon);
    }

    [Fact]
    public void EquipWeaponRefusedByHostLeavesEquipmentAlone()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, 1);
        host.WeaponOk = false;
        Assert.False(actions.Equip(Rifle).Done);
        Assert.Null(equipment.Weapon);
    }

    [Fact]
    public void EquipNotCarriedOrNotEquippableIsRefused()
    {
        var (actions, items, equipment, _, _) = Make();
        Assert.False(actions.Equip(Rifle).Done);
        items.Add(Stimpak, 1);
        Assert.False(actions.Equip(Stimpak).Done);
        Assert.Null(equipment.Weapon);
    }

    [Fact]
    public void SecondWeaponReplacesTheFirst()
    {
        var (actions, items, equipment, _, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Pistol, 1);
        actions.Equip(Rifle);
        actions.Equip(Pistol);
        Assert.Equal(Pistol, equipment.Weapon!.Id);
    }

    [Fact]
    public void ExplicitStatePicksThatInstance()
    {
        var (actions, items, equipment, _, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Rifle, Modded, 1);
        Assert.True(actions.Equip(Rifle, Modded).Done);
        Assert.Equal(Modded, equipment.Weapon!.State);
    }

    [Fact]
    public void ArmorWithOverlappingMaskDisplacesWornPiece()
    {
        var (actions, items, equipment, _, _) = Make();
        foreach (var id in new[] { Helmet, Hat, Chest }) items.Add(id, 1);
        actions.Equip(Helmet);
        actions.Equip(Chest);
        Assert.Equal(new[] { Helmet, Chest }, equipment.Armor.Select(a => a.Id).Order().ToArray());
        actions.Equip(Hat);
        Assert.Equal(new[] { Hat, Chest }, equipment.Armor.Select(a => a.Id).Order().ToArray());
    }

    [Fact]
    public void ArmorWithoutReadableMaskIsRefused()
    {
        var (actions, items, equipment, _, _) = Make();
        items.Add(Mystery, 1);
        Assert.False(actions.Equip(Mystery).Done);
        Assert.Empty(equipment.Armor);
    }

    [Fact]
    public void UnequipWeaponCallsHostAndArmorRemovesEntry()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Helmet, 1);
        actions.Equip(Rifle);
        actions.Equip(Helmet);
        Assert.True(actions.Unequip(Rifle).Done);
        Assert.Equal(1, host.Unequips);
        Assert.Null(equipment.Weapon);
        Assert.True(actions.Unequip(Helmet).Done);
        Assert.Empty(equipment.Armor);
        Assert.False(actions.Unequip(Helmet).Done);
    }

    [Fact]
    public void UseConsumesOneCopyAndAppliesEffects()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Stimpak, 2);
        host.Effects[Stimpak] = new[] { new ConsumableEffect("heal", 50, 0) };
        Assert.True(actions.Use(Stimpak).Done);
        Assert.Equal(1, items.CountOf(Stimpak));
        Assert.Equal(new[] { Stimpak }, host.Applied);
    }

    [Fact]
    public void UseWithUnreadableEffectsDoesNotConsume()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Stimpak, 1);
        Assert.False(actions.Use(Stimpak).Done);
        Assert.Equal(1, items.CountOf(Stimpak));
        Assert.Empty(host.Applied);
    }

    [Fact]
    public void UseWithEmptyEffectListConsumes()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Stimpak, 1);
        host.Effects[Stimpak] = Array.Empty<ConsumableEffect>();
        Assert.True(actions.Use(Stimpak).Done);
        Assert.Equal(0, items.CountOf(Stimpak));
    }

    [Fact]
    public void UseRejectedByHostKeepsTheItem()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Stimpak, 1);
        host.Effects[Stimpak] = new[] { new ConsumableEffect("heal", 50, 0) };
        host.ApplyOk = false;
        Assert.False(actions.Use(Stimpak).Done);
        Assert.Equal(1, items.CountOf(Stimpak));
    }

    [Fact]
    public void UseOnNonAlchIsRefused()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Junk, 1);
        host.Effects[Junk] = new[] { new ConsumableEffect("x", 1, 0) };
        Assert.False(actions.Use(Junk).Done);
        Assert.Equal(1, items.CountOf(Junk));
    }

    [Fact]
    public void DropSpawnsPickupableRefAndRemovesFromStore()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Junk, 5);
        Assert.True(actions.Drop(Junk, 3).Done);
        Assert.Equal(2, items.CountOf(Junk));
        var drop = Assert.Single(host.Drops);
        Assert.Equal(new DroppedItem(Junk, "MiscItem", ItemState.Plain, 3), drop);
        var pickedUp = new PlayerItems();
        var target = new ActivationTarget("ref", drop.Id, drop.BaseType, new HashSet<string>(), drop.Count);
        var pickup = new ItemPickup(pickedUp, _ => true, _ => { });
        Assert.True(pickup.Handles(target));
        pickup.Activate(target);
        Assert.Equal(3, pickedUp.CountOf(Junk));
    }

    [Fact]
    public void DropBaseTypesAreAllPickupable()
    {
        foreach (var signature in ItemRecordTypes.Core)
            Assert.Contains(PlayerInventoryItems.BaseTypeOf(signature), ItemPickup.ItemBaseTypes);
        Assert.Null(PlayerInventoryItems.BaseTypeOf("CMPO"));
        Assert.Null(PlayerInventoryItems.BaseTypeOf("LIGH"));
    }

    [Fact]
    public void DropOfUnpickableSignatureIsRefusedAndKeepsItem()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Steel, 4);
        Assert.False(actions.Drop(Steel, 1).Done);
        Assert.Equal(4, items.CountOf(Steel));
        Assert.Empty(host.Drops);
    }

    [Fact]
    public void DropMoreThanHeldIsRefusedWithoutPartialRemoval()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Junk, 2);
        Assert.False(actions.Drop(Junk, 3).Done);
        Assert.Equal(2, items.CountOf(Junk));
        Assert.Empty(host.Drops);
        Assert.False(actions.Drop(Junk, 0).Done);
    }

    [Fact]
    public void DropNeverTakesTheEquippedCopy()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Rifle, Modded, 1);
        actions.Equip(Rifle, Modded);
        Assert.False(actions.Drop(Rifle, 2).Done);
        Assert.True(actions.Drop(Rifle, 1).Done);
        Assert.Equal(1, items.CountOf(Rifle, Modded));
        Assert.Equal(ItemState.Plain, Assert.Single(host.Drops).State);
        Assert.NotNull(equipment.Weapon);
    }

    [Fact]
    public void DropOfTheOnlyEquippedCopyAsksToUnequipFirst()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, 1);
        actions.Equip(Rifle);
        var result = actions.Drop(Rifle, 1);
        Assert.False(result.Done);
        Assert.Equal("unequip it first", result.Message);
        Assert.Equal(1, items.CountOf(Rifle));
        Assert.Empty(host.Drops);
        Assert.NotNull(equipment.Weapon);
        Assert.Equal("not enough to drop", actions.Drop(Rifle, 2).Message);
    }

    [Fact]
    public void DropRefusedByTheHostKeepsTheItemCarried()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Junk, 3);
        host.SpawnLimit = 0;
        var result = actions.Drop(Junk, 2);
        Assert.False(result.Done);
        Assert.Equal("cannot be placed here", result.Message);
        Assert.Equal(3, items.CountOf(Junk));
    }

    [Fact]
    public void DropStopsAtTheFirstCopyTheHostCannotPlace()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Rifle, Modded, 1);
        host.SpawnLimit = 1;
        var result = actions.Drop(Rifle, 2);
        Assert.True(result.Done);
        Assert.Equal("dropped 1", result.Message);
        Assert.Equal(0, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Modded));
    }

    [Fact]
    public void DroppedModdedCopyKeepsItsStateWhenPickedUpAgain()
    {
        var (actions, items, _, host, _) = Make();
        var damaged = Modded.WithCondition(0.5);
        items.Add(Rifle, damaged, 1);
        Assert.True(actions.Drop(Rifle, 1).Done);
        var drop = Assert.Single(host.Drops);
        var states = new DroppedItemStates();
        var token = states.Remember(drop.State);
        var pickedUp = new PlayerItems();
        var pickup = new ItemPickup(pickedUp, _ => true, _ => { }, dropped: states);
        pickup.Activate(new ActivationTarget(token, drop.Id, drop.BaseType, new HashSet<string>(), drop.Count));
        Assert.Equal(1, pickedUp.CountOf(Rifle, damaged));
        pickup.Activate(new ActivationTarget(token, drop.Id, drop.BaseType, new HashSet<string>(), drop.Count));
        Assert.Equal(1, pickedUp.CountOf(Rifle, ItemState.Plain));
    }

    [Fact]
    public void PickingUpADroppedRefDoesNotMarkItTakenInTheLedger()
    {
        var states = new DroppedItemStates();
        var token = states.Remember(ItemState.Plain);
        var ledger = new ContainerLedger();
        var pickup = new ItemPickup(new PlayerItems(), _ => true, _ => { }, ledger, states);
        pickup.Activate(new ActivationTarget(token, Junk, "MiscItem", new HashSet<string>(), 1));
        pickup.Activate(new ActivationTarget("Ref:placed", Junk, "MiscItem", new HashSet<string>(), 1));
        Assert.False(ledger.IsTaken(token));
        Assert.True(ledger.IsTaken("Ref:placed"));
    }

    [Fact]
    public void PlainDropsLeaveNoStateBehindAndTokensAreUnique()
    {
        var states = new DroppedItemStates();
        var first = states.Remember(ItemState.Plain);
        var second = states.Remember(Modded);
        Assert.NotEqual(first, second);
        Assert.StartsWith(DroppedItemStates.TokenPrefix, first, StringComparison.Ordinal);
        Assert.Null(states.Take(first));
        Assert.Equal(Modded, states.Take(second));
        Assert.Null(states.Take(second));
    }

    [Fact]
    public void DropKeepsInstanceStateOfModdedCopies()
    {
        var (actions, items, _, host, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Rifle, Modded, 1);
        Assert.True(actions.Drop(Rifle, 2).Done);
        Assert.Equal(new[] { ItemState.Plain, Modded }, host.Drops.Select(d => d.State).ToArray());
        Assert.Equal(0, items.CountOf(Rifle));
    }

    [Fact]
    public void ApplyRoutesEveryInventoryEventKind()
    {
        var (actions, items, equipment, host, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Stimpak, 1);
        items.Add(Junk, 2);
        host.Effects[Stimpak] = Array.Empty<ConsumableEffect>();
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Equip, Rifle)).Done);
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Use, Stimpak)).Done);
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Drop, Junk, 2)).Done);
        Assert.True(actions.Apply(new InventoryEvent(InventoryEventKind.Unequip, Rifle)).Done);
        Assert.False(actions.Apply(new InventoryEvent(InventoryEventKind.AssignFavourite, Rifle, 1, 1)).Done);
        Assert.Null(equipment.Weapon);
    }

    [Fact]
    public void ReconcileDropsEquipmentThatLeftTheInventory()
    {
        var (actions, items, equipment, _, _) = Make();
        items.Add(Rifle, 1);
        items.Add(Helmet, 1);
        actions.Equip(Rifle);
        actions.Equip(Helmet);
        items.Remove(Rifle, 1);
        items.Remove(Helmet, 1);
        equipment.Reconcile(items);
        Assert.Null(equipment.Weapon);
        Assert.Empty(equipment.Armor);
    }

    [Fact]
    public void PipBoyListShowsEquipUseFlagsAndFiltersByCategory()
    {
        var (actions, items, equipment, host, reader) = Make();
        foreach (var id in new[] { Rifle, Helmet, Stimpak, Junk }) items.Add(id, 1);
        host.Effects[Stimpak] = Array.Empty<ConsumableEffect>();
        actions.Equip(Rifle);
        var built = PlayerInventoryItems.Build(items.Stacks, new RecordItems(reader), equipment, reader, id => host.EffectsOf(id) is not null);
        var rifle = built.Single(i => i.Id == Rifle);
        Assert.True(rifle.Equipped);
        Assert.True(rifle.Equippable);
        Assert.Equal("Weapon", rifle.EquipSlot);
        Assert.False(built.Single(i => i.Id == Helmet).Equipped);
        Assert.True(built.Single(i => i.Id == Helmet).Equippable);
        Assert.True(built.Single(i => i.Id == Stimpak).Usable);
        Assert.False(built.Single(i => i.Id == Junk).Equippable);

        var model = new InventoryModel(built);
        Assert.Equal(new[] { "Aid", "Apparel", "Misc", "Weapons" }, model.Categories.Order().ToArray());
        model.SetCategory("Weapons");
        Assert.Equal(new[] { Rifle }, model.View.Visible.Select(i => i.Id).ToArray());
        model.SetCategory("Aid");
        Assert.Equal(new[] { Stimpak }, model.View.Visible.Select(i => i.Id).ToArray());
        model.SetCategory(null);
        Assert.Equal(4, model.View.Visible.Count);
    }

    [Fact]
    public void UsableStaysFalseWhenEffectsAreUnreadable()
    {
        var (_, items, equipment, host, reader) = Make();
        items.Add(Stimpak, 1);
        var built = PlayerInventoryItems.Build(items.Stacks, new RecordItems(reader), equipment, reader, id => host.EffectsOf(id) is not null);
        Assert.False(built.Single().Usable);
    }

    [Fact]
    public void SyncAfterActionKeepsCategoryFilterAndDropsStaleFavourites()
    {
        var (actions, items, equipment, _, reader) = Make();
        items.Add(Junk, 1);
        items.Add(Rifle, 1);
        var records = new RecordItems(reader);
        var model = new InventoryModel(PlayerInventoryItems.Build(items.Stacks, records, equipment, reader));
        model.SetCategory("Misc");
        model.Select(Junk);
        model.AssignFavourite(1);
        Assert.Equal(1, model.SlotOf(Junk));
        Assert.True(actions.Apply(model.Drop()!).Done);
        model.Sync(PlayerInventoryItems.Build(items.Stacks, records, equipment, reader));
        Assert.Equal(0, model.SlotOf(Junk));
        Assert.Equal("Misc", model.Category);
        Assert.Empty(model.View.Visible);
        model.SetCategory(null);
        Assert.Equal(new[] { Rifle }, model.View.Visible.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void BuildWithoutEquipmentOffersNoEquipOrUse()
    {
        var (_, items, _, _, reader) = Make();
        foreach (var id in new[] { Rifle, Helmet, Stimpak }) items.Add(id, 1);
        var built = PlayerInventoryItems.Build(items.Stacks, new RecordItems(reader));
        Assert.All(built, item => Assert.False(item.Equippable || item.Equipped || item.Usable));
    }

    [Fact]
    public void EquipIsCaseInsensitiveOnIds()
    {
        var (actions, items, equipment, _, _) = Make();
        items.Add(Rifle, 1);
        Assert.True(actions.Equip(Rifle.ToLowerInvariant()).Done);
        Assert.True(actions.Unequip(Rifle.ToUpperInvariant()).Done);
        Assert.Null(equipment.Weapon);
    }
}
