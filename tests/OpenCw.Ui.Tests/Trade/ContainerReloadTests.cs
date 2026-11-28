using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Trade;

public sealed class ContainerReloadTests
{
    const string Ref = "000A01:Fallout4.esm";
    const string Item = "000B01:Fallout4.esm";

    sealed class Records : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) => new("Chest", false, false, [new InventoryStack(Item, 2)]);
    }

    [Fact]
    public void AnOpenContainerFollowsALoadThatReplacesTheLedger()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        player.Add("P", 3);
        var open = PlacedContainerInventory.Open(Ref, "base", new Records(), ledger, player, out _)!;
        var sections = ContainerStateSave.Sections(ledger, player);

        ContainerStateSave.Apply(sections, ledger, player);
        open.Transfer(ContainerSide.Player, "P", 3);

        Assert.Equal(3, ledger.Find(Ref)!.CountOf("P"));
        Assert.Equal(0, player.CountOf("P"));
    }

    [Fact]
    public void AContainerAbsentFromTheSaveIsEmptiedAndDropped()
    {
        var ledger = new ContainerLedger();
        var held = ledger.Freeze(Ref, [new InventoryStack(Item, 2)]);
        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), ledger, new PlayerItems());
        Assert.Null(ledger.Find(Ref));
        Assert.Empty(held.Entries);
    }

    [Fact]
    public void ADuplicatedContainerRowKeepsTheFirst()
    {
        var json = "{\"version\":1,\"player\":[],\"containers\":[{\"ref\":\"" + Ref + "\",\"stacks\":[{\"id\":\"a\",\"count\":1}]},{\"ref\":\"" + Ref + "\",\"stacks\":[{\"id\":\"b\",\"count\":1}]}]}";
        var ledger = new ContainerLedger();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes(json) }, ledger, new PlayerItems());
        Assert.Equal(1, ledger.Find(Ref)!.CountOf("a"));
        Assert.Equal(0, ledger.Find(Ref)!.CountOf("b"));
    }
}
