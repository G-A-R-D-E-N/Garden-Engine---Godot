using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Trade;

public sealed class LedgerInventoryAccessTests
{
    const string Player = "000014:Fallout4.esm";
    const string Chest = "000A01:Fallout4.esm";
    const string Crate = "000A02:Fallout4.esm";
    const string Ammo = "000B01:Fallout4.esm";
    const string Caps = "00000F:Fallout4.esm";

    static (ContainerLedger Ledger, PlayerItems Items, LedgerInventoryAccess Access) Setup()
    {
        var ledger = new ContainerLedger();
        var items = new PlayerItems();
        return (ledger, items, new LedgerInventoryAccess(ledger, items, key => key == Player));
    }

    [Fact]
    public void AddThenCountReadsTheContainerRowAndAScriptedAddSurvivesASaveAndLoad()
    {
        var (ledger, items, access) = Setup();
        ledger.Freeze(Chest, [new InventoryStack(Caps, 2)]);

        Assert.True(access.TryAdd(Chest, Ammo, 3));
        Assert.True(access.TryAdd(Chest, Ammo, 1));

        Assert.Equal(4, access.Count(Chest, Ammo));
        Assert.Equal(6, access.Count(Chest, null));
        var reloaded = new ContainerLedger();
        ContainerStateSave.Apply(ContainerStateSave.Sections(ledger, items), reloaded, new PlayerItems());
        Assert.Equal(4, reloaded.Find(Chest)!.CountOf(Ammo));
        Assert.Equal(2, reloaded.Find(Chest)!.CountOf(Caps));
    }

    [Fact]
    public void ThePlayerReferenceRoutesToPlayerItemsAndLeavesTheLedgerUntouched()
    {
        var (ledger, items, access) = Setup();

        Assert.True(access.TryAdd(Player, Ammo, 5));
        Assert.Equal(5, items.CountOf(Ammo));
        Assert.Equal(5, access.Count(Player, Ammo));
        Assert.Equal(3, access.Move(Player, null, Ammo, 3));

        Assert.Equal(2, items.CountOf(Ammo));
        Assert.Null(ledger.Find(Player));
        Assert.True(ledger.IsEmpty);
    }

    [Fact]
    public void ARemoveTakesThePartialCountAndAnOverCountTakesOnlyWhatIsHeldPlainCopiesFirst()
    {
        var (ledger, _, access) = Setup();
        var worn = new ItemState(["mod_a"], 0.5);
        var held = ledger.Freeze(Chest, [new InventoryStack(Ammo, 2)]);
        held.Add(Ammo, worn, 1);

        Assert.Equal(2, access.Move(Chest, null, Ammo, 2));
        Assert.Equal(1, held.CountOf(Ammo, worn));
        Assert.Equal(1, access.Move(Chest, null, Ammo, 10));
        Assert.Equal(0, access.Count(Chest, Ammo));
        Assert.Equal(0, access.Move(Chest, null, Ammo, 1));
    }

    [Fact]
    public void AMoveCarriesInstanceStateToTheOtherContainerOrThePlayerAndRemoveAllEmptiesTheSource()
    {
        var (ledger, items, access) = Setup();
        var worn = new ItemState(["mod_a"], 0.5);
        var chest = ledger.Freeze(Chest, [new InventoryStack(Caps, 9)]);
        chest.Add(Ammo, worn, 2);
        var crate = ledger.Freeze(Crate, new List<InventoryStack>());

        Assert.Equal(2, access.Move(Chest, Crate, Ammo, 5));
        Assert.Equal(2, crate.CountOf(Ammo, worn));
        Assert.Equal(9, access.Count(Chest, null));

        Assert.Equal(9, access.Move(Chest, Player, null, 0));
        Assert.Equal(9, items.CountOf(Caps));
        Assert.Empty(chest.Entries);
    }

    [Fact]
    public void AReferenceWithNoLedgerRowIsNeverGivenAnEmptyRowThatWouldHideItsRecordContents()
    {
        var (ledger, items, access) = Setup();
        ledger.Freeze(Chest, [new InventoryStack(Ammo, 5)]);

        Assert.False(access.TryAdd(Crate, Ammo, 1));
        Assert.Equal(0, access.Count(Crate, Ammo));
        Assert.Equal(0, access.Move(Crate, null, Ammo, 1));
        Assert.Equal(0, access.Move(Chest, Crate, Ammo, 1));
        Assert.Equal(0, access.Move(Chest, Chest, Ammo, 1));

        Assert.Null(ledger.Find(Crate));
        Assert.Equal(5, access.Count(Chest, Ammo));
        Assert.Empty(items.Entries);
        Assert.False(access.TryAdd(Chest, Ammo, 0));
    }

    [Fact]
    public void ARemoveAllToTheSameReferenceIsRefusedAndKeepsEveryItem()
    {
        var (ledger, _, access) = Setup();
        ledger.Freeze(Chest, [new InventoryStack(Ammo, 5), new InventoryStack(Caps, 2)]);

        Assert.Equal(0, access.Move(Chest, Chest, null, 0));
        Assert.Equal(7, access.Count(Chest, null));
        Assert.Equal(new[] { Caps, Ammo }, ledger.Find(Chest)!.Entries.Select(entry => entry.Id).Order().ToArray());
    }

    [Fact]
    public void AScriptAddToANeverOpenedContainerLandsOnTopOfItsRecordContentsAndTheSeededRowKeepsTheRecordStacks()
    {
        var ledger = new ContainerLedger();
        var seeded = 0;
        var access = new LedgerInventoryAccess(ledger, new PlayerItems(), key => key == Player,
            key => { seeded++; return key == Crate ? new InventoryStack[] { new(Caps, 2) } : null; });

        Assert.True(access.TryAdd(Crate, Ammo, 3));
        Assert.Equal(2, access.Count(Crate, Caps));
        Assert.Equal(3, access.Count(Crate, Ammo));
        Assert.Equal(1, seeded);
        Assert.Equal(2, ledger.Find(Crate)!.CountOf(Caps));

        Assert.False(access.TryAdd(Chest, Ammo, 1));
        Assert.Null(ledger.Find(Chest));
    }

    [Fact]
    public void AnUnreadableInventoryIsUnavailableAndAnEmptyOneIsNot()
    {
        var access = new LedgerInventoryAccess(new ContainerLedger(), new PlayerItems(), key => key == Player,
            key => key == Crate ? new InventoryStack[] { } : null);

        Assert.False(access.HasInventory(Chest));
        Assert.True(access.HasInventory(Crate));
        Assert.Equal(0, access.Count(Crate, null));
        Assert.True(access.HasInventory(Player));
    }
}
