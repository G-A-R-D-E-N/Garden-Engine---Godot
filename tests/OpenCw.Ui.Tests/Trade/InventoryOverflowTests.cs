using OpenCommonwealth;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Trade;

public sealed class InventoryOverflowTests
{
    const string Item = "000B01:Fallout4.esm";
    const string Other = "000B02:Fallout4.esm";
    const string Chest = "000A01:Fallout4.esm";
    const string Crate = "000A02:Fallout4.esm";

    [Fact]
    public void AddingPastIntMaxSaturatesInsteadOfWrappingNegative()
    {
        var items = new PlayerItems();
        items.Add(Item, ItemState.Plain, int.MaxValue - 1);
        items.Add(Item, ItemState.Plain, 5);

        Assert.Equal(int.MaxValue, items.CountOf(Item));
        Assert.Equal(int.MaxValue, Assert.Single(items.Stacks).Count);
    }

    [Fact]
    public void AggregateCountsAcrossStatesDoNotThrowOrWrap()
    {
        var items = new PlayerItems();
        items.Add(Item, ItemState.Plain, int.MaxValue);
        items.Add(Item, new ItemState(["mod"]), int.MaxValue);

        Assert.Equal(int.MaxValue, items.CountOf(Item));
        Assert.Equal(int.MaxValue, Assert.Single(items.Stacks).Count);
    }

    [Fact]
    public void ATransferThatWouldOverflowTheTargetIsRefusedAndChangesNeitherSide()
    {
        var source = new PlayerItems();
        var target = new PlayerItems();
        source.Add(Item, ItemState.Plain, 10);
        target.Add(Item, ItemState.Plain, int.MaxValue - 5);

        Assert.False(source.MoveTo(target, Item, 10));

        Assert.Equal(10, source.CountOf(Item));
        Assert.Equal(int.MaxValue - 5, target.CountOf(Item));
        Assert.True(source.MoveTo(target, Item, 5));
        Assert.Equal(int.MaxValue, target.CountOf(Item));
    }

    [Fact]
    public void AScriptMoveOfEverythingIsRefusedBeforeTheSourceIsCleared()
    {
        var ledger = new ContainerLedger();
        var access = new LedgerInventoryAccess(ledger, new PlayerItems(), _ => false);
        ledger.Freeze(Chest, [new InventoryStack(Item, 10), new InventoryStack(Other, 2)]);
        ledger.Freeze(Crate, [new InventoryStack(Other, int.MaxValue)]);

        Assert.Equal(0, access.Move(Chest, Crate, null, 0));

        Assert.Equal(12, access.Count(Chest, null));
        Assert.Equal(int.MaxValue, access.Count(Crate, Other));
    }

    [Fact]
    public void AScriptAddThatWouldOverflowIsRefused()
    {
        var ledger = new ContainerLedger();
        var access = new LedgerInventoryAccess(ledger, new PlayerItems(), _ => false);
        ledger.Freeze(Chest, [new InventoryStack(Item, int.MaxValue)]);

        Assert.False(access.TryAdd(Chest, Item, 1));
        Assert.Equal(int.MaxValue, access.Count(Chest, Item));
    }

    [Fact]
    public void ExpandedDuplicateStacksSaturate()
    {
        var stacks = ContainerContents.Expand(
            [new InventoryStack(Item, int.MaxValue), new InventoryStack(Item, 7)], "ref", _ => false, (_, _, _) => []);

        Assert.Equal(int.MaxValue, Assert.Single(stacks).Count);
    }

    [Fact]
    public void ALeveledEntryAboveTheApiLimitIsRolledInChunksSoNoCountIsLost()
    {
        var calls = new List<(int Count, ulong Seed)>();

        var stacks = ContainerContents.PickChunked(70000, 100, (count, seed) =>
        {
            calls.Add((count, seed));
            return [new InventoryStack(Item, count)];
        });

        Assert.Equal(new[] { (32767, 100UL), (32767, 101UL), (4466, 102UL) }, calls);
        Assert.Equal(70000, stacks.Sum(stack => stack.Count));
    }

    [Fact]
    public void AnEntryAtOrBelowTheLimitIsOneCallWithThePlainSeed()
    {
        var calls = new List<(int Count, ulong Seed)>();

        ContainerContents.PickChunked(5, 9, (count, seed) =>
        {
            calls.Add((count, seed));
            return [];
        });

        Assert.Equal(new[] { (5, 9UL) }, calls);
    }
}
