using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InteractionSaveRoundTripTests
{
    const string Stim = "000B01:Fallout4.esm";
    const string Leather = "000C01:Fallout4.esm";
    const string Gun = "000F01:Fallout4.esm";
    const string Ammo = "000F02:Fallout4.esm";
    const string Core = "000F03:Fallout4.esm";
    const string Chest = "000A01:Fallout4.esm";
    const string Door = "000A02:Fallout4.esm";
    const string Pickup = "000A03:Fallout4.esm";
    const string Frame = "000A04:Fallout4.esm";

    static readonly WeaponInfo Pistol = new(9, Ammo, 6, 20, 0, 3000, 1, 0, 0.5, 2, 0);

    static (ContainerLedger Ledger, PlayerItems Items) Reload(ContainerLedger ledger, PlayerItems items)
    {
        var sections = ContainerStateSave.Sections(ledger, items);
        var loadedLedger = new ContainerLedger();
        var loadedItems = new PlayerItems();
        ContainerStateSave.Apply(sections, loadedLedger, loadedItems);
        return (loadedLedger, loadedItems);
    }

    static PowerArmorSession Armor(PlayerItems store) => new(t => t.BaseKey == "frame", k => k == Core, store, 0.01);

    [Fact]
    public void LootedContainerAndUnlockSurviveAndKeepTheirOrder()
    {
        var ledger = new ContainerLedger();
        ledger.Freeze(Chest, [new InventoryStack(Leather, 3), new InventoryStack(Stim, 1)]);
        ledger.MarkUnlocked(Chest);
        var items = new PlayerItems();
        items.Add(Ammo, 12);

        var (loaded, loadedItems) = Reload(ledger, items);

        Assert.Equal([(Leather, 3), (Stim, 1)], loaded.Find(Chest)!.Stacks.Select(s => (s.Id, s.Count)));
        Assert.True(loaded.IsUnlocked(Chest));
        Assert.Equal(12, loadedItems.CountOf(Ammo));
    }

    [Fact]
    public void OpenDoorsAndTakenWorldItemsSurvive()
    {
        var ledger = new ContainerLedger();
        var items = new PlayerItems();
        var store = items;
        var pickup = new ItemPickup(store, _ => true, _ => { }, ledger);
        var target = new ActivationTarget(Pickup, Leather, "MiscItem", new HashSet<string>(), 4);
        pickup.Activate(target);
        ledger.SetOpened(Door, true);

        var (loaded, loadedItems) = Reload(ledger, items);

        Assert.True(loaded.IsTaken(Pickup));
        Assert.True(loaded.IsOpened(Door));
        Assert.Equal(4, loadedItems.CountOf(Leather));
    }

    [Fact]
    public void ClosingADoorRemovesItFromTheSave()
    {
        var ledger = new ContainerLedger();
        ledger.SetOpened(Door, true);
        ledger.SetOpened(Door, false);
        Assert.Empty(ContainerStateSave.Sections(ledger, new PlayerItems()));
    }

    [Fact]
    public void ACraftedItemIsInThePlayerStockAfterReload()
    {
        var reader = new FakeRecordReader();
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Leather, "MISC", name: "Leather");
        reader.Add("000D01:Fallout4.esm", "KYWD", "WorkshopRecipeFilterChemsHealing");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, "000A01:Fallout4.esm", ["000D01:Fallout4.esm"], (Leather, 2));
        var items = new PlayerItems();
        items.Add(Leather, 5);
        var bench = new RecordBenchRecipes(reader, items, ["000A01:Fallout4.esm"], "Chem Station");
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);

        var (_, loadedItems) = Reload(new ContainerLedger(), items);

        Assert.Equal(1, loadedItems.CountOf(Stim));
        Assert.Equal(3, loadedItems.CountOf(Leather));
    }

    [Fact]
    public void EquippedWeaponKeepsClipAndDrawnStateAndReserve()
    {
        var items = new PlayerItems();
        items.Add(Ammo, 20);
        items.Add(Gun, 1);
        var weapons = new WeaponController(items);
        weapons.Equip(Gun, Pistol, clip: 4);
        weapons.Draw();
        var ledger = new ContainerLedger { Weapon = weapons.Capture() };

        var (loaded, loadedItems) = Reload(ledger, items);
        var restored = new WeaponController(loadedItems);
        restored.Restore(loaded.Weapon, key => key == Gun ? Pistol : null);

        Assert.Equal(Gun, restored.WeaponKey);
        Assert.Equal(4, restored.Clip);
        Assert.True(restored.IsDrawn);
        Assert.Equal(20, restored.Reserve);
    }

    [Fact]
    public void AModdedWeaponKeepsItsInstanceState()
    {
        var state = new ItemState(["000AAA:Fallout4.esm"], 0.5, 2);
        var weapons = new WeaponController(new PlayerItems());
        weapons.Equip(Gun, Pistol, 3, state);
        var (loaded, _) = Reload(new ContainerLedger { Weapon = weapons.Capture() }, new PlayerItems());
        Assert.Equal(state, loaded.Weapon!.Item);
    }

    [Fact]
    public void ASheathedWeaponStaysSheathed()
    {
        var weapons = new WeaponController(new PlayerItems());
        weapons.Equip(Gun, Pistol, clip: 6);
        var (loaded, _) = Reload(new ContainerLedger { Weapon = weapons.Capture() }, new PlayerItems());
        var held = new PlayerItems();
        held.Add(Gun, 1);
        var restored = new WeaponController(held);
        restored.Restore(loaded.Weapon, _ => Pistol);
        Assert.Equal(WeaponState.Sheathed, restored.State);
    }

    [Fact]
    public void AWeaponWhoseRecordIsGoneRestoresAsNothingEquipped()
    {
        var restored = new WeaponController(new PlayerItems());
        restored.Equip("old", Pistol);
        restored.Restore(new WeaponSave(Gun, 3, true, ItemState.Plain), _ => null);
        Assert.Null(restored.WeaponKey);
        Assert.Equal(WeaponState.None, restored.State);
    }

    [Fact]
    public void PowerArmorOccupancyAndInstalledCoreSurvive()
    {
        var items = new PlayerItems();
        var store = items;
        items.Add(Core, 1);
        var session = Armor(store);
        var target = new ActivationTarget(Frame, "frame", "Furniture", new HashSet<string> { "furniture" });
        session.Activate(target);
        var frame = session.Occupied!;
        Assert.Equal(Core, frame.CoreKey);
        frame.CoreCharge = 0.75;
        var ledger = new ContainerLedger { PowerArmor = session.Capture() };

        var (loaded, loadedItems) = Reload(ledger, items);
        var restored = Armor(loadedItems);
        var entered = 0;
        restored.Entered += _ => entered++;
        restored.Restore(loaded.PowerArmor);

        Assert.True(restored.InArmor);
        Assert.Equal(1, entered);
        Assert.Equal(Frame, restored.Occupied!.RefKey);
        Assert.Equal(Core, restored.Occupied.CoreKey);
        Assert.Equal(0.75, restored.Occupied.CoreCharge);
        Assert.Equal(0, loadedItems.CountOf(Core));
    }

    [Fact]
    public void AnExitedFrameKeepsItsCoreWithoutOccupancy()
    {
        var store = new PlayerItems();
        store.Add(Core, 1);
        var session = Armor(store);
        session.Activate(new ActivationTarget(Frame, "frame", "Furniture", new HashSet<string>()));
        session.Occupied!.CoreCharge = 0.5;
        session.Exit();

        var (loaded, _) = Reload(new ContainerLedger { PowerArmor = session.Capture() }, new PlayerItems());
        var restored = Armor(new PlayerItems());
        restored.Restore(loaded.PowerArmor);

        Assert.False(restored.InArmor);
        Assert.Equal(Core, restored.Frame(Frame).CoreKey);
        Assert.Equal(0.5, restored.Frame(Frame).CoreCharge);
    }

    [Fact]
    public void LoadingASaveWithoutArmorExitsTheArmorTheSessionIsIn()
    {
        var session = Armor(new PlayerItems());
        session.Activate(new ActivationTarget(Frame, "frame", "Furniture", new HashSet<string>()));
        var exited = 0;
        session.Exited += _ => exited++;
        session.Restore(null);
        Assert.False(session.InArmor);
        Assert.Equal(1, exited);
    }

    [Fact]
    public void ASaveFromBeforeTheseKeysStillLoads()
    {
        var json = "{\"version\":1,\"player\":[{\"id\":\"" + Stim + "\",\"count\":2}],\"containers\":[]}";
        var ledger = new ContainerLedger();
        ledger.SetOpened(Door, true);
        var items = new PlayerItems();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(json) }, ledger, items);
        Assert.Equal(2, items.CountOf(Stim));
        Assert.False(ledger.IsOpened(Door));
        Assert.Null(ledger.Weapon);
        Assert.Null(ledger.PowerArmor);
    }

    [Theory]
    [InlineData("\"weapon\":{\"key\":\"w\",\"clip\":-1}")]
    [InlineData("\"weapon\":{\"clip\":1}")]
    [InlineData("\"opened\":[\"\"]")]
    [InlineData("\"taken\":{}")]
    [InlineData("\"powerArmor\":{\"occupied\":\"x\",\"frames\":[]}")]
    [InlineData("\"powerArmor\":{\"frames\":[{\"ref\":\"f\",\"charge\":\"high\"}]}")]
    [InlineData("\"worn\":{}")]
    [InlineData("\"worn\":[{\"id\":\"x\",\"count\":1}]")]
    [InlineData("\"worn\":[{\"id\":\"x\",\"count\":1,\"mask\":-1}]")]
    [InlineData("\"worn\":[{\"id\":\"x\",\"count\":1,\"mask\":\"a\"}]")]
    public void AMalformedInteractionRowLeavesTheLiveStateUntouched(string extra)
    {
        var json = "{\"version\":1,\"player\":[],\"containers\":[]," + extra + "}";
        var ledger = new ContainerLedger();
        ledger.SetOpened(Door, true);
        var items = new PlayerItems();
        items.Add(Stim, 1);
        Assert.Throws<FormatException>(() =>
            ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(json) }, ledger, items));
        Assert.True(ledger.IsOpened(Door));
        Assert.Equal(1, items.CountOf(Stim));
    }

    [Theory]
    [InlineData("[0.5,1,1,1]")]
    [InlineData("[0.5,1,1,1,1,1,1]")]
    [InlineData("[]")]
    [InlineData("[0.5,\"x\",1,1,1,1]")]
    [InlineData("[0.5,null,1,1,1,1]")]
    [InlineData("[0.5,{},1,1,1,1]")]
    [InlineData("\"all\"")]
    public void AWrongLengthOrMalformedPartsArrayLoadsTheFrameWithFullPieces(string parts)
    {
        var json = "{\"version\":1,\"player\":[],\"containers\":[],\"powerArmor\":{\"occupied\":\"f\",\"frames\":[{\"ref\":\"f\",\"core\":\"" + Core + "\",\"charge\":0.4,\"parts\":" + parts + "}]}}";
        var ledger = new ContainerLedger();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(json) }, ledger, new PlayerItems());
        var restored = Armor(new PlayerItems());
        restored.Restore(ledger.PowerArmor);
        Assert.Equal("f", restored.Occupied!.RefKey);
        Assert.Equal(0.4, restored.Occupied.CoreCharge);
        Assert.Equal(6, restored.Occupied.Parts.Count);
        Assert.All(restored.Occupied.Parts, p => Assert.Equal(1, p.Condition));
    }

    [Fact]
    public void AWellFormedPartsArrayRestoresItsConditionsAndClampsOutOfRangeValues()
    {
        var json = "{\"version\":1,\"player\":[],\"containers\":[],\"powerArmor\":{\"frames\":[{\"ref\":\"f\",\"charge\":1,\"parts\":[0.5,-1,7,0,1,-5]}]}}";
        var ledger = new ContainerLedger();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(json) }, ledger, new PlayerItems());
        var restored = Armor(new PlayerItems());
        restored.Restore(ledger.PowerArmor);
        var frame = restored.Frame("f");
        Assert.Equal(0.5, frame.ConditionOf((PowerArmorPartId)0));
        Assert.Null(frame.ConditionOf((PowerArmorPartId)1));
        Assert.Equal(1, frame.ConditionOf((PowerArmorPartId)2));
        Assert.Equal(0, frame.ConditionOf((PowerArmorPartId)3));
        Assert.Null(frame.ConditionOf((PowerArmorPartId)5));
    }

    [Fact]
    public void PickedUpArmorStaysWornWithItsStateAndSlotsAfterASaveAndLoad()
    {
        var state = new ItemState(["000AAA:Fallout4.esm"], 0.5, 1);
        var items = new PlayerItems();
        var pickup = new ItemPickup(items, _ => true, _ => { });
        pickup.Activate(new ActivationTarget("000A05:Fallout4.esm", Leather, "Armor", new HashSet<string>(), 1));
        items.Transform(Leather, ItemState.Plain, state);
        var equipment = new PlayerEquipment();
        equipment.Wear(Leather, state, 0b101);

        var (loaded, loadedItems) = Reload(new ContainerLedger { Armor = equipment.Armor.ToArray() }, items);
        var restored = new PlayerEquipment();
        restored.Restore(loaded.Armor, loadedItems);

        Assert.Equal([new EquippedArmor(Leather, state, 0b101)], restored.Armor);
        Assert.True(restored.IsEquipped(Leather, state));
        Assert.Equal(1, loadedItems.CountOf(Leather, state));
    }

    [Fact]
    public void RestoringWornArmorDropsPiecesThePlayerNoLongerHolds()
    {
        var held = new PlayerItems();
        held.Add(Leather, 1);
        var restored = new PlayerEquipment();
        restored.Restore([new EquippedArmor(Leather, ItemState.Plain, 1), new EquippedArmor(Stim, ItemState.Plain, 2)], held);
        Assert.Equal([new EquippedArmor(Leather, ItemState.Plain, 1)], restored.Armor);
    }

    [Fact]
    public void LoadingASaveWithoutWornArmorUndressesThePlayer()
    {
        var held = new PlayerItems();
        held.Add(Leather, 1);
        var equipment = new PlayerEquipment();
        equipment.Wear(Leather, ItemState.Plain, 1);
        var ledger = new ContainerLedger { Armor = equipment.Armor.ToArray() };
        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), ledger, new PlayerItems());
        held.Add(Leather, 1);
        equipment.Restore(ledger.Armor, held);
        Assert.Empty(ledger.Armor);
        Assert.Empty(equipment.Armor);
    }

    [Fact]
    public void WornArmorAloneKeepsTheLedgerNonEmptyAndOverlappingSlotsDisplace()
    {
        var ledger = new ContainerLedger { Armor = [new EquippedArmor(Leather, ItemState.Plain, 1)] };
        Assert.False(ledger.IsEmpty);
        Assert.NotEmpty(ContainerStateSave.Sections(ledger, new PlayerItems()));
        var held = new PlayerItems();
        held.Add(Leather, 1);
        held.Add(Stim, 1);
        var equipment = new PlayerEquipment();
        equipment.Restore([new EquippedArmor(Leather, ItemState.Plain, 0b011), new EquippedArmor(Stim, ItemState.Plain, 0b010)], held);
        Assert.Equal([new EquippedArmor(Stim, ItemState.Plain, 0b010)], equipment.Armor);
    }

    [Fact]
    public void LoadingASaveWithNoSectionClearsEverything()
    {
        var ledger = new ContainerLedger { Weapon = new WeaponSave(Gun, 1, false, ItemState.Plain) };
        ledger.SetOpened(Door, true);
        ledger.MarkTaken(Pickup);
        var items = new PlayerItems();
        items.Add(Stim, 1);
        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), ledger, items);
        Assert.True(ledger.IsEmpty);
        Assert.Empty(items.Stacks);
    }
}
