using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SceneIntegrationTests
{
    const string Leather = "000C01:Fallout4.esm";
    const string Pistol = "000A02:Fallout4.esm";
    const string Ammo = "000F01:Fallout4.esm";
    const string Stim = "000B01:Fallout4.esm";
    const string LongBarrel = "000B02:Fallout4.esm";
    const string Bench = "000A01:Fallout4.esm";
    const string Recipe = "000E02:Fallout4.esm";
    const string Crate = "000500:A.esp";
    const string CrateBase = "000501:A.esp";

    static readonly IReadOnlySet<string> Containers = new HashSet<string> { "containers" };
    static readonly IReadOnlySet<string> NoGroups = new HashSet<string>();

    sealed class Crates : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) =>
            new("Crate", false, false, [new InventoryStack(Stim, 2), new InventoryStack(Leather, 2)]);
    }

    static ActivationTarget Loose(string key, string baseKey, string type, int count, string? owner = null) =>
        new(key, baseKey, type, NoGroups, count, owner);

    static (int Ammo, int Leather, int Stim) Held(PlayerItems items) => (items.CountOf(Ammo), items.CountOf(Leather), items.CountOf(Stim));

    static (int Ammo, int Leather, int Stim) Shown(PlacedContainerInventory inventory) =>
        (inventory.ContainerStacks.Where(s => s.Id == Ammo).Sum(s => s.Count), inventory.ContainerStacks.Where(s => s.Id == Leather).Sum(s => s.Count),
            inventory.ContainerStacks.Where(s => s.Id == Stim).Sum(s => s.Count));

    [Fact]
    public void Pickup_container_bench_weapon_save_and_load_keep_exact_state_at_every_step()
    {
        var items = new PlayerItems();
        var ledger = new ContainerLedger();
        var removed = new List<string>();
        var taken = new List<ItemTaken>();
        var pickup = new ItemPickup(items, ItemPickup.SameOwner(null), target => removed.Add(target.RefKey));
        pickup.Taken += taken.Add;
        PlacedContainerInventory? open = null;
        var dispatcher = new ActivationDispatcher(_ => true);
        dispatcher.Register(new DelegateActivationHandler(t => t.Groups.Contains("containers"), t =>
        {
            open = PlacedContainerInventory.Open(t.RefKey, t.BaseKey, new Crates(), ledger, items, out var refusal);
            return new(open is null ? ActivationStatus.Blocked : ActivationStatus.Handled, refusal);
        }));
        dispatcher.Register(pickup);

        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Loose("r1", Leather, "MiscItem", 3)).Status);
        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Loose("r2", Pistol, "Weapon", 0)).Status);
        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Loose("r3", Ammo, "Ammunition", 12)).Status);
        Assert.Equal("stole 1", dispatcher.Dispatch(Loose("r4", Leather, "MiscItem", 1, owner: "000900:A.esp")).Detail);
        Assert.Equal(4, dispatcher.ScriptEventsDelivered);
        Assert.Equal(["r1", "r2", "r3", "r4"], removed);
        Assert.Equal([false, false, false, true], taken.Select(t => t.Stolen));
        Assert.Equal((12, 4, 0), Held(items));
        Assert.Equal(1, items.CountOf(Pistol));

        var crate = new ActivationTarget(Crate, CrateBase, "Container", Containers);
        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(crate).Status);
        Assert.Equal((0, 2, 2), Shown(open!));
        Assert.Equal(TransferStatus.Done, open!.Transfer(ContainerSide.Player, Ammo, 2).Status);
        Assert.Equal(TransferStatus.Done, open.Transfer(ContainerSide.Container, Leather, 2).Status);
        Assert.Equal(TransferStatus.Done, open.Transfer(ContainerSide.Container, Stim, 1).Status);
        Assert.Equal(TransferStatus.Refused, open.Transfer(ContainerSide.Container, Stim, 5).Status);
        Assert.Equal((10, 6, 1), Held(items));
        Assert.Equal((2, 0, 1), Held(ledger.Find(Crate)!));
        Assert.Equal(3, open.Revision);

        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(crate).Status);
        Assert.Equal((2, 0, 1), Shown(open!));

        var reader = new FakeRecordReader();
        reader.Add(Pistol, "WEAP", name: "Pistol");
        reader.Add(Leather, "MISC", name: "Leather");
        reader.Add(LongBarrel, "OMOD", name: "Long Barrel").Scalars["OMOD\\FormType"] = "1346454871";
        reader.AddRecipe(Recipe, LongBarrel, Bench, [], (Leather, 1));
        var bench = new RecordBenchRecipes(reader, items, [Bench], "Weapon Workbench");
        var target = bench.Targets.Single(t => t.Id.StartsWith(RecordBenchRecipes.ItemTargetPrefix, System.StringComparison.Ordinal));
        var slot = bench.SlotsOf(target.Id).Single().Id;
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(target.Id, slot, Recipe));
        Assert.True(bench.Craft(target.Id, slot, Recipe).Ok);
        var modded = new ItemState([LongBarrel]);
        Assert.Equal(5, items.CountOf(Leather));
        Assert.Equal((1, 0), (items.CountOf(Pistol, modded), items.CountOf(Pistol, ItemState.Plain)));
        var after = bench.Targets.Single(t => t.Id.StartsWith(RecordBenchRecipes.ItemTargetPrefix, System.StringComparison.Ordinal));
        Assert.Equal([RecordBenchRecipes.DetachSlot], bench.SlotsOf(after.Id).Select(slot => slot.Id));

        var info = new WeaponInfo(9, Ammo, 6, 20, 0, 3000, 1, 0, 0.5, 0, 0);
        var weapons = new WeaponController(items);
        Assert.True(weapons.Equip(Pistol, info, 0, modded));
        weapons.Draw();
        Assert.Equal(WeaponState.Ready, weapons.State);
        Assert.True(weapons.Reload());
        Assert.Equal((6, 4), (weapons.Clip, items.CountOf(Ammo)));
        Assert.Equal(FireResult.Fired, weapons.Fire());
        Assert.Equal((5, 4), (weapons.Clip, weapons.Reserve));

        ledger.Weapon = weapons.Capture();
        var sections = ContainerStateSave.Sections(ledger, items);
        var loadedItems = new PlayerItems();
        var loadedLedger = new ContainerLedger();
        ContainerStateSave.Apply(sections, loadedLedger, loadedItems);
        Assert.Equal(items.Entries.OrderBy(e => e.Id).Select(e => (e.Id, e.Count, e.State)), loadedItems.Entries.OrderBy(e => e.Id).Select(e => (e.Id, e.Count, e.State)));
        Assert.Equal(1, loadedItems.CountOf(Pistol, modded));
        Assert.Equal((2, 0, 1), Held(loadedLedger.Find(Crate)!));

        var restored = new WeaponController(loadedItems);
        restored.Restore(loadedLedger.Weapon, key => key == Pistol ? info : null);
        Assert.Equal((Pistol, modded, 5, 4, true), (restored.WeaponKey, restored.Item, restored.Clip, restored.Reserve, restored.IsDrawn));
    }
}
