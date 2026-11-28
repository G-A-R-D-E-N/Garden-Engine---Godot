using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InventoryEquipBindingTests
{
    const string Rifle = "000001:Test.esm", Pistol = "000002:Test.esm", Helmet = "000003:Test.esm", Hat = "000004:Test.esm",
        Chest = "000005:Test.esm", Stimpak = "000006:Test.esm", Junk = "000007:Test.esm", Gone = "000008:Test.esm";

    static readonly WeaponInfo Gun = new(9, "AMMO1", 30, 20, 100, 3000, 1, 0, 0.5, 2, 0);

    sealed class Fixture
    {
        public readonly PlayerItems Items = new();
        public readonly PlayerEquipment Equipment = new();
        public readonly WeaponController Weapons;
        public readonly InventoryEquipBinding Binding;
        public readonly InventoryModel Model;

        public Fixture(bool lookupWorks = true, System.Func<DroppedItem, bool>? spawn = null)
        {
            var reader = new FakeRecordReader();
            reader.Add(Rifle, "WEAP", name: "Rifle");
            reader.Add(Pistol, "WEAP", name: "Pistol");
            reader.Add(Gone, "WEAP", name: "Gone");
            reader.Add(Helmet, "ARMO", name: "Helmet").Scalars[PlayerInventoryItems.ArmorMaskPath] = "1";
            reader.Add(Hat, "ARMO", name: "Hat").Scalars[PlayerInventoryItems.ArmorMaskPath] = "3";
            reader.Add(Chest, "ARMO", name: "Chest").Scalars[PlayerInventoryItems.ArmorMaskPath] = "4";
            reader.Add(Stimpak, "ALCH", name: "Stimpak");
            reader.Add(Junk, "MISC", name: "Junk");
            Items.Add(Rifle, 1);
            Items.Add(Pistol, 1);
            Items.Add(Gone, 1);
            Items.Add(Helmet, 1);
            Items.Add(Hat, 1);
            Items.Add(Chest, 1);
            Items.Add(Stimpak, 2);
            Items.Add(Junk, 3);
            Weapons = new WeaponController(Items);
            var host = new WeaponInventoryHost(Weapons, Items, id => lookupWorks && id != Gone ? Gun : null, spawn: spawn);
            Binding = new InventoryEquipBinding(Items, new RecordItems(reader), reader, Equipment, Weapons, host);
            Model = Binding.NewModel();
        }

        public InventoryItem Row(string id) => Model.Items.Single(item => item.Id == id);
    }

    static InventoryEvent Equip(string id) => new(InventoryEventKind.Equip, id);

    static InventoryEvent Unequip(string id) => new(InventoryEventKind.Unequip, id);

    [Fact]
    public void WeaponsAndArmorAreEquippableAndOthersAreNot()
    {
        var f = new Fixture();
        Assert.True(f.Row(Rifle).Equippable);
        Assert.True(f.Row(Helmet).Equippable);
        Assert.False(f.Row(Stimpak).Equippable);
        Assert.False(f.Row(Junk).Equippable);
        Assert.DoesNotContain(f.Model.Items, item => item.Equipped);
    }

    [Fact]
    public void EquippingAWeaponPutsItInTheControllerAndMarksTheRow()
    {
        var f = new Fixture();
        var result = f.Binding.Handle(Equip(Rifle));
        Assert.True(result!.Done);
        Assert.Equal(Rifle, f.Weapons.WeaponKey);
        Assert.True(f.Row(Rifle).Equipped);
        Assert.False(f.Row(Pistol).Equipped);
    }

    [Fact]
    public void EquippingAnotherWeaponSwapsTheHeldWeapon()
    {
        var f = new Fixture();
        f.Binding.Handle(Equip(Rifle));
        f.Binding.Handle(Equip(Pistol));
        Assert.Equal(Pistol, f.Weapons.WeaponKey);
        Assert.False(f.Row(Rifle).Equipped);
        Assert.True(f.Row(Pistol).Equipped);
    }

    [Fact]
    public void UnequippingClearsTheControllerAndTheRow()
    {
        var f = new Fixture();
        f.Binding.Handle(Equip(Rifle));
        var result = f.Binding.Handle(Unequip(Rifle));
        Assert.True(result!.Done);
        Assert.Null(f.Weapons.WeaponKey);
        Assert.False(f.Row(Rifle).Equipped);
    }

    [Fact]
    public void AWeaponHeldBeforeThePipBoyOpenedShowsEquippedAndCanBeUnequipped()
    {
        var f = new Fixture();
        f.Weapons.Equip(Pistol, Gun);
        var model = f.Binding.NewModel();
        Assert.True(model.Items.Single(item => item.Id == Pistol).Equipped);
        Assert.True(f.Binding.Handle(Unequip(Pistol))!.Done);
        Assert.Null(f.Weapons.WeaponKey);
        Assert.False(model.Items.Single(item => item.Id == Pistol).Equipped);
    }

    [Fact]
    public void ReEquippingTheHeldWeaponKeepsItsClipAndAnotherWeaponStartsEmpty()
    {
        var f = new Fixture();
        f.Weapons.Equip(Rifle, Gun, 12);
        f.Binding.Handle(Equip(Rifle));
        Assert.Equal(12, f.Weapons.Clip);
        Assert.Equal(0, f.Items.CountOf("AMMO1"));
        f.Binding.Handle(Equip(Pistol));
        Assert.Equal(0, f.Weapons.Clip);
    }

    [Fact]
    public void ReEquippingTheSameIdWithAnotherStateRefundsTheLoadedRounds()
    {
        var f = new Fixture();
        var modded = new ItemState(new[] { "mod_a" });
        f.Items.Add(Rifle, modded, 1);
        f.Weapons.Equip(Rifle, Gun, 12);
        var host = new WeaponInventoryHost(f.Weapons, f.Items, _ => Gun);
        Assert.True(host.EquipWeapon(Rifle, modded));
        Assert.Equal(modded, f.Weapons.Item);
        Assert.Equal(0, f.Weapons.Clip);
        Assert.Equal(12, f.Items.CountOf("AMMO1"));
    }

    [Fact]
    public void LoadedRoundsGoBackToTheReserveWhenTheWeaponIsSwappedOrUnequipped()
    {
        var f = new Fixture();
        f.Items.Add("AMMO1", 5);
        f.Weapons.Equip(Rifle, Gun, 12);
        f.Binding.Handle(Equip(Pistol));
        Assert.Equal(17, f.Items.CountOf("AMMO1"));
        f.Weapons.Equip(Pistol, Gun, 4);
        f.Binding.Handle(Unequip(Pistol));
        Assert.Equal(21, f.Items.CountOf("AMMO1"));
    }

    [Fact]
    public void ARefusedSwapKeepsTheLoadedRoundsInTheWeapon()
    {
        var f = new Fixture();
        f.Weapons.Equip(Rifle, Gun, 12);
        f.Binding.Handle(Equip(Gone));
        Assert.Equal(12, f.Weapons.Clip);
        Assert.Equal(0, f.Items.CountOf("AMMO1"));
    }

    [Fact]
    public void ADrawnWeaponStaysDrawnAcrossAnEquipSwap()
    {
        var f = new Fixture();
        f.Binding.Handle(Equip(Rifle));
        f.Weapons.Draw();
        f.Binding.Handle(Equip(Pistol));
        Assert.True(f.Weapons.IsDrawn);
    }

    [Fact]
    public void AWeaponWithNoReadableRecordIsRefusedAndStaysUnequipped()
    {
        var f = new Fixture();
        var result = f.Binding.Handle(Equip(Gone));
        Assert.False(result!.Done);
        Assert.Null(f.Weapons.WeaponKey);
        Assert.False(f.Row(Gone).Equipped);
    }

    [Fact]
    public void ArmorEquipDisplacesOverlappingArmorAndUnequipRemovesIt()
    {
        var f = new Fixture();
        Assert.True(f.Binding.Handle(Equip(Helmet))!.Done);
        Assert.True(f.Row(Helmet).Equipped);
        Assert.True(f.Binding.Handle(Equip(Hat))!.Done);
        Assert.False(f.Row(Helmet).Equipped);
        Assert.True(f.Row(Hat).Equipped);
        Assert.True(f.Binding.Handle(Equip(Chest))!.Done);
        Assert.True(f.Row(Hat).Equipped);
        Assert.True(f.Binding.Handle(Unequip(Hat))!.Done);
        Assert.False(f.Row(Hat).Equipped);
        Assert.True(f.Row(Chest).Equipped);
        Assert.Null(f.Weapons.WeaponKey);
    }

    [Fact]
    public void ItemsPickedUpInTheWorldShowInTheNextPipBoyAndArmorFromThemCanBeWorn()
    {
        var f = new Fixture();
        var pickup = new ItemPickup(f.Items, _ => true, _ => { });
        pickup.Activate(new ActivationTarget("000A05:Test.esm", Junk, "MiscItem", new HashSet<string>(), 4));
        pickup.Activate(new ActivationTarget("000A06:Test.esm", Chest, "Armor", new HashSet<string>(), 1));

        var model = f.Binding.NewModel();
        Assert.Equal((7, 2), (model.Items.Single(item => item.Id == Junk).Count, model.Items.Single(item => item.Id == Chest).Count));
        Assert.True(f.Binding.Handle(Equip(Chest))!.Done);
        Assert.True(model.Items.Single(item => item.Id == Chest).Equipped);
        Assert.Equal(2, f.Items.CountOf(Chest));
    }

    [Fact]
    public void FavouriteEventsAreNotRoutedAndAnUnreadableUseChangesNothing()
    {
        var f = new Fixture();
        Assert.Equal("effects unreadable", f.Binding.Handle(new InventoryEvent(InventoryEventKind.Use, Stimpak))!.Message);
        Assert.Null(f.Binding.Handle(new InventoryEvent(InventoryEventKind.AssignFavourite, Rifle, 1, 1)));
        Assert.Equal(3, f.Items.CountOf(Junk));
        Assert.Equal(2, f.Items.CountOf(Stimpak));
        Assert.Equal(3, f.Row(Junk).Count);
    }

    [Fact]
    public void DropIsRoutedAndWithoutAPlacingHostChangesNothing()
    {
        var f = new Fixture();
        var result = f.Binding.Handle(new InventoryEvent(InventoryEventKind.Drop, Junk, 3))!;
        Assert.False(result.Done);
        Assert.Equal("cannot be placed here", result.Message);
        Assert.Equal(3, f.Items.CountOf(Junk));
        Assert.Equal(3, f.Row(Junk).Count);
    }

    [Fact]
    public void DropThroughAPlacingHostRemovesTheStackAndUpdatesTheModel()
    {
        var placed = new List<DroppedItem>();
        var f = new Fixture(spawn: drop => { placed.Add(drop); return true; });
        Assert.True(f.Binding.Handle(new InventoryEvent(InventoryEventKind.Drop, Junk, 3))!.Done);
        Assert.Equal(new DroppedItem(Junk, "MiscItem", ItemState.Plain, 3), Assert.Single(placed));
        Assert.Equal(0, f.Items.CountOf(Junk));
        Assert.DoesNotContain(f.Model.Items, item => item.Id == Junk);
    }

    [Fact]
    public void AWeaponNoLongerCarriedIsNotShownEquipped()
    {
        var f = new Fixture();
        f.Binding.Handle(Equip(Rifle));
        f.Items.Remove(Rifle, 1);
        var model = f.Binding.NewModel();
        Assert.DoesNotContain(model.Items, item => item.Id == Rifle);
        Assert.Null(f.Equipment.Weapon);
        Assert.Null(f.Weapons.WeaponKey);
    }

    [Fact]
    public void TheTabsEquipActionRoundTripsThroughTheModel()
    {
        var f = new Fixture();
        Assert.True(f.Model.Select(Rifle));
        var command = f.Model.Equip();
        Assert.Equal(Equip(Rifle), command);
        f.Binding.Handle(command!);
        Assert.Null(f.Model.Equip());
        Assert.Equal(Unequip(Rifle), f.Model.Unequip());
    }
}
