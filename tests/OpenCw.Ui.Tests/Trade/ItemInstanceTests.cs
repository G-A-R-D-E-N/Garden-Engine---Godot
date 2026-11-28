using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ItemInstanceTests
{
    const string Rifle = "000A01:Fallout4.esm";
    const string Vest = "000A02:Fallout4.esm";
    const string Ammo = "000A03:Fallout4.esm";
    const string Scope = "000B01:Fallout4.esm";
    const string Barrel = "000B02:Fallout4.esm";
    const string Pad = "000B03:Fallout4.esm";
    const string Steel = "000C01:Fallout4.esm";
    const string Bench = "000D01:Fallout4.esm";
    const string WeaponType = "1346454871";
    const string ArmorType = "1330467393";

    static ItemState Mods(params string[] mods) => new(mods);

    static string T(string id, params string[] mods) => $"item:{id}#{string.Join(',', mods)}#1#0";

    [Fact]
    public void StateEqualityIgnoresModOrderAndCase()
    {
        Assert.True(Mods(Scope, Barrel).Equals(Mods(Barrel.ToLowerInvariant(), Scope.ToLowerInvariant())));
        Assert.Equal(Mods(Scope, Barrel).GetHashCode(), Mods(Barrel, Scope).GetHashCode());
        Assert.False(Mods(Scope).Equals(Mods(Scope).WithCondition(0.5)));
        Assert.False(Mods(Scope).Equals(Mods(Scope).WithRank(1)));
        Assert.True(ItemState.Plain.IsPlain);
        Assert.False(Mods(Scope).IsPlain);
        Assert.Equal([Scope, Barrel], Mods(Barrel, Scope).Mods);
        Assert.Throws<ArgumentException>(() => Mods(Scope, Scope));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemState.Plain.WithCondition(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemState.Plain.WithRank(-1));
        Assert.Equal(Mods(Scope), Mods(Scope, Barrel).Without(Barrel.ToLowerInvariant()));
    }

    [Fact]
    public void PlainIdsStayOneCountAndInstancesStackOnlyWhenEqual()
    {
        var items = new PlayerItems();
        items.Add(Ammo, 10);
        items.Add(Ammo, 5);
        items.Add(Rifle, 1);
        items.Add(Rifle, Mods(Scope), 1);
        items.Add(Rifle, Mods(Scope), 1);
        items.Add(Rifle, Mods(Scope).WithCondition(0.5), 1);

        Assert.Equal(15, items.CountOf(Ammo));
        Assert.Equal([new ItemEntry(Ammo, ItemState.Plain, 15), new ItemEntry(Rifle, ItemState.Plain, 1),
            new ItemEntry(Rifle, Mods(Scope), 2), new ItemEntry(Rifle, Mods(Scope).WithCondition(0.5), 1)], items.Entries);
        Assert.Equal(4, items.CountOf(Rifle));
        Assert.Equal(2, items.CountOf(Rifle, Mods(Scope)));
        Assert.Equal([new InventoryStack(Ammo, 15), new InventoryStack(Rifle, 4)], items.Stacks);
    }

    [Fact]
    public void RemoveByIdTakesPlainFirstAndIsAllOrNothing()
    {
        var items = new PlayerItems();
        items.Add(Rifle, Mods(Scope), 2);
        items.Add(Rifle, 1);

        Assert.False(items.Remove(Rifle, 4));
        Assert.Equal(3, items.CountOf(Rifle));
        Assert.True(items.Remove(Rifle, 2));
        Assert.Equal(0, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope)));
        Assert.False(items.Remove(Rifle, 0));
        Assert.False(items.Remove(Rifle, ItemState.Plain, 1));
        Assert.True(items.Remove(Rifle, Mods(Scope), 1));
        Assert.Empty(items.Entries);
    }

    [Fact]
    public void TransformMovesOneCopyAndLeavesTheRestAlone()
    {
        var items = new PlayerItems();
        items.Add(Rifle, 3);
        Assert.True(items.Transform(Rifle, ItemState.Plain, Mods(Barrel)));
        Assert.Equal(2, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Barrel)));
        Assert.False(items.Transform(Rifle, Mods(Scope), Mods(Barrel)));
        Assert.Equal(3, items.CountOf(Rifle));
    }

    [Fact]
    public void SaveRoundTripKeepsInstancesAndPlainRowsKeepTheirOldShape()
    {
        var player = new PlayerItems();
        player.Add(Ammo, 7);
        player.Add(Rifle, Mods(Scope, Barrel).WithCondition(0.25).WithRank(2), 1);
        var ledger = new ContainerLedger();
        ledger.Freeze("000F01:Fallout4.esm", [new ItemEntry(Vest, Mods(Pad), 1), new ItemEntry(Ammo, ItemState.Plain, 3)]);

        var sections = ContainerStateSave.Sections(ledger, player);
        var json = Encoding.UTF8.GetString(sections[ContainerStateSave.SectionName]);
        Assert.Contains("{\n      \"id\": \"000A03:Fallout4.esm\",\n      \"count\": 7\n    }", json);
        Assert.Contains("\"mods\": [\n        \"000B01:Fallout4.esm\",\n        \"000B02:Fallout4.esm\"\n      ]", json);
        Assert.Contains("\"condition\": 0.25", json);
        Assert.Contains("\"rank\": 2", json);

        var loadedPlayer = new PlayerItems();
        var loadedLedger = new ContainerLedger();
        ContainerStateSave.Apply(sections, loadedLedger, loadedPlayer);
        Assert.Equal(player.Entries, loadedPlayer.Entries);
        Assert.Equal(ledger.Find("000F01:Fallout4.esm")!.Entries, loadedLedger.Find("000F01:Fallout4.esm")!.Entries);
    }

    [Fact]
    public void OldPlainSaveLoadsAndBadInstanceRowsChangeNothing()
    {
        var player = new PlayerItems();
        player.Add("KEEP", 2);
        var ledger = new ContainerLedger();
        static Dictionary<string, byte[]> Section(string row) => new()
        {
            [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[" + row + "],\"containers\":[]}"),
        };

        ContainerStateSave.Apply(Section("{\"id\":\"A\",\"count\":4}"), ledger, player);
        Assert.Equal([new ItemEntry("A", ItemState.Plain, 4)], player.Entries);

        foreach (var bad in new[]
        {
            "{\"id\":\"A\",\"count\":1,\"mods\":[\"\"]}",
            "{\"id\":\"A\",\"count\":1,\"mods\":[\"M\",\"m\"]}",
            "{\"id\":\"A\",\"count\":1,\"condition\":1.5}",
            "{\"id\":\"A\",\"count\":1,\"condition\":-0.1}",
            "{\"id\":\"A\",\"count\":1,\"rank\":-1}",
        })
        {
            Assert.Throws<FormatException>(() => ContainerStateSave.Apply(Section(bad), ledger, player));
            Assert.Equal([new ItemEntry("A", ItemState.Plain, 4)], player.Entries);
        }
    }

    static (FakeRecordReader Reader, PlayerItems Items, RecordBenchRecipes Bench) MakeBench(PlayerItems? items = null)
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Combat Rifle");
        reader.Add(Vest, "ARMO", name: "Leather Vest");
        reader.Add(Ammo, "AMMO", name: "5.56");
        reader.Add(Steel, "MISC", name: "Steel");
        reader.Add(Scope, "OMOD", name: "Scope").Scalars["OMOD\\FormType"] = WeaponType;
        reader.Add(Barrel, "OMOD", name: "Long Barrel").Scalars["OMOD\\FormType"] = WeaponType;
        reader.Add(Pad, "OMOD", name: "Padded").Scalars["OMOD\\FormType"] = ArmorType;
        reader.AddRecipe("000E01:Fallout4.esm", Scope, Bench, [], (Steel, 2));
        reader.AddRecipe("000E02:Fallout4.esm", Barrel, Bench, [], (Steel, 1));
        reader.AddRecipe("000E03:Fallout4.esm", Pad, Bench, [], (Steel, 1));
        items ??= new PlayerItems();
        return (reader, items, new RecordBenchRecipes(reader, items, [Bench], "Weapon Workbench"));
    }

    [Fact]
    public void ModRecipesAreOfferedPerItemAndOnlyForMatchingFormTypes()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Steel, 5);
        items.Add(Rifle, 1);
        items.Add(Vest, 1);
        items.Add(Rifle, Mods(Barrel), 2);

        Assert.Equal(["bench", T(Rifle), T(Vest), T(Rifle, Barrel)], bench.Targets.Select(t => t.Id));
        Assert.Equal(["Weapon Workbench", "Combat Rifle", "Leather Vest", "Combat Rifle (+1) x2"], bench.Targets.Select(t => t.Name));
        Assert.Empty(bench.SlotsOf("bench"));
        Assert.Equal(["Scope", "Long Barrel"], bench.OptionsOf(T(Rifle), "Other").Select(o => o.Name).OrderByDescending(n => n));
        Assert.Equal(["Padded"], bench.OptionsOf(T(Vest), "Other").Select(o => o.Name));
        Assert.Equal(["Scope"], bench.OptionsOf(T(Rifle, Barrel), "Other").Select(o => o.Name));
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(T(Vest), "Other", "000E01:Fallout4.esm"));
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(T(Rifle), "Other", "000E01:Fallout4.esm"));
    }

    [Fact]
    public void CraftingAModAttachesItToOneCopyAndSpendsTheComponents()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Steel, 5);
        items.Add(Rifle, 2);

        Assert.True(bench.Craft(T(Rifle), "Other", "000E01:Fallout4.esm").Ok);
        Assert.Equal(3, items.CountOf(Steel));
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope)));
        Assert.Equal(0, items.CountOf(Scope));

        var modded = bench.Targets.Single(t => t.Name == "Combat Rifle (+1)").Id;
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(modded, "Other", "000E01:Fallout4.esm"));
        Assert.Equal(RecordBenchRecipes.NotApplicableText, bench.Craft(modded, "Other", "000E01:Fallout4.esm").Reason);
        Assert.True(bench.Craft(modded, "Other", "000E02:Fallout4.esm").Ok);
        Assert.Equal(2, items.CountOf(Steel));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope, Barrel)));
    }

    [Fact]
    public void WrongTypeMissingItemAndShortComponentsRefuseAndChangeNothing()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Steel, 1);
        items.Add(Vest, 1);
        items.Add(Rifle, 1);

        Assert.Equal(RecordBenchRecipes.NotApplicableText, bench.Craft(T(Vest), "Other", "000E01:Fallout4.esm").Reason);
        Assert.Equal(RecordBenchRecipes.NotApplicableText, bench.Craft(T(Rifle), "Other", "000E03:Fallout4.esm").Reason);
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft(T(Steel), "Other", "000E01:Fallout4.esm").Reason);
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft("bench", "Other", "000E01:Fallout4.esm").Reason);
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft("item:nope", "Other", "000E01:Fallout4.esm").Reason);
        Assert.Equal(RecordBenchRecipes.LackingText, bench.Craft(T(Rifle), "Other", "000E01:Fallout4.esm").Reason);
        Assert.Equal(1, items.CountOf(Steel));
        Assert.Equal([new ItemEntry(Steel, ItemState.Plain, 1), new ItemEntry(Vest, ItemState.Plain, 1), new ItemEntry(Rifle, ItemState.Plain, 1)], items.Entries);
    }

    [Fact]
    public void AWorldPickupIsTheSameStoreTheBenchReads()
    {
        var items = new PlayerItems();
        var (_, _, bench) = MakeBench(items);
        var pickup = new ItemPickup(items, _ => true, _ => { });
        pickup.Activate(new ActivationTarget("000F10:Fallout4.esm", Steel, "MiscItem", new HashSet<string>(), Count: 4));
        pickup.Activate(new ActivationTarget("000F11:Fallout4.esm", Rifle, "Weapon", new HashSet<string>()));

        Assert.Equal(4, bench.AvailableCount(Steel));
        Assert.Equal(T(Rifle), bench.Targets[1].Id);
        Assert.True(bench.Craft(T(Rifle), "Other", "000E01:Fallout4.esm").Ok);
        Assert.Equal(2, items.CountOf(Steel));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope)));
    }

    [Fact]
    public void MaterialsBeforeTheWeaponDoNotShiftTheTarget()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Steel, 2);
        items.Add(Rifle, 1);
        items.Add(Rifle, Mods(Barrel), 1);

        Assert.True(bench.Craft(T(Rifle), "Other", "000E01:Fallout4.esm").Ok);
        Assert.Equal(0, items.CountOf(Steel));
        Assert.Equal(0, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Barrel)));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope)));
    }

    [Fact]
    public void DetachingAModRemovesItFromOneCopyAndCostsNothing()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Steel, 1);
        items.Add(Rifle, Mods(Scope, Barrel), 2);
        var target = T(Rifle, Scope, Barrel);

        Assert.Equal(RecordBenchRecipes.DetachSlot, bench.SlotsOf(target).Last().Id);
        Assert.Equal(["detach:" + Scope, "detach:" + Barrel], bench.OptionsOf(target, RecordBenchRecipes.DetachSlot).Select(o => o.Id));
        Assert.Equal(["Scope", "Long Barrel"], bench.OptionsOf(target, RecordBenchRecipes.DetachSlot).Select(o => o.Name));
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(target, RecordBenchRecipes.DetachSlot, "detach:" + Scope));
        Assert.Empty(bench.Requirements(target, RecordBenchRecipes.DetachSlot, "detach:" + Scope));
        var stamp = bench.Stamp;

        Assert.True(bench.Craft(target, RecordBenchRecipes.DetachSlot, "detach:" + Scope).Ok);
        Assert.NotEqual(stamp, bench.Stamp);
        Assert.Equal(1, items.CountOf(Rifle, Mods(Scope, Barrel)));
        Assert.Equal(1, items.CountOf(Rifle, Mods(Barrel)));
        Assert.Equal(1, items.CountOf(Steel));
        Assert.Equal(0, items.CountOf(Scope));
    }

    [Fact]
    public void DetachRefusesAModThatIsNotAttachedOrAnItemThatIsGone()
    {
        var (_, items, bench) = MakeBench();
        items.Add(Rifle, Mods(Barrel), 1);

        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(T(Rifle, Barrel), RecordBenchRecipes.DetachSlot, "detach:" + Scope));
        Assert.Equal(RecordBenchRecipes.NotApplicableText, bench.Craft(T(Rifle, Barrel), RecordBenchRecipes.DetachSlot, "detach:" + Scope).Reason);
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft(T(Rifle), RecordBenchRecipes.DetachSlot, "detach:" + Barrel).Reason);
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft("bench", RecordBenchRecipes.DetachSlot, "detach:" + Barrel).Reason);
        Assert.Equal([new ItemEntry(Rifle, Mods(Barrel), 1)], items.Entries);
    }

    [Fact]
    public void AnItemWithModsIsATargetEvenWhenTheBenchHasNoModRecipes()
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Combat Rifle");
        var items = new PlayerItems();
        items.Add(Rifle, Mods(Barrel), 1);
        items.Add(Rifle, 1);
        var bench = new RecordBenchRecipes(reader, items, [Bench], "Bench");

        Assert.Equal(["bench", T(Rifle, Barrel)], bench.Targets.Select(t => t.Id));
        Assert.True(bench.Craft(T(Rifle, Barrel), RecordBenchRecipes.DetachSlot, "detach:" + Barrel).Ok);
        Assert.Equal([new ItemEntry(Rifle, ItemState.Plain, 2)], items.Entries);
    }

    [Fact]
    public void TransfersKeepTheInstanceState()
    {
        var from = new PlayerItems();
        var to = new PlayerItems();
        from.Add(Rifle, 1);
        from.Add(Rifle, Mods(Scope), 2);
        Assert.True(from.MoveTo(to, Rifle, 2));
        Assert.Equal([new ItemEntry(Rifle, ItemState.Plain, 1), new ItemEntry(Rifle, Mods(Scope), 1)], to.Entries);
        Assert.Equal([new ItemEntry(Rifle, Mods(Scope), 1)], from.Entries);
        Assert.False(from.MoveTo(to, Rifle, 2));
        Assert.Equal(3, to.CountOf(Rifle) + from.CountOf(Rifle));
    }

    [Fact]
    public void AThrownWeaponSpendsItsOwnInstance()
    {
        var items = new PlayerItems();
        items.Add(Rifle, 1);
        items.Add(Rifle, Mods(Scope), 1);
        var controller = new WeaponController(items);
        controller.Equip(Rifle, new WeaponInfo(10, null, 0, 10, 100, 0, 1, 0, 0, 0, 0.5), item: Mods(Scope));
        controller.Draw();
        Assert.Equal(FireResult.Fired, controller.Fire());
        Assert.Equal([new ItemEntry(Rifle, ItemState.Plain, 1)], items.Entries);
    }

    [Fact]
    public void ANonListModsValueIsRejected()
    {
        var bad = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[{\"id\":\"A\",\"count\":1,\"mods\":\"x\"}],\"containers\":[]}") };
        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(bad, new ContainerLedger(), new PlayerItems()));
    }

    [Fact]
    public void TheEquippedWeaponCarriesItsInstanceState()
    {
        var items = new PlayerItems();
        var controller = new WeaponController(items);
        var info = new WeaponInfo(1, null, 0, 10, 100, 0, 1, 0, 0, 0, 0.5);
        Assert.True(controller.Equip(Rifle, info));
        Assert.Equal(ItemState.Plain, controller.Item);
        Assert.True(controller.Equip(Rifle, info, item: Mods(Scope)));
        Assert.Equal([Scope], controller.Item.Mods);
        controller.Unequip();
        Assert.Equal(ItemState.Plain, controller.Item);
    }
}
