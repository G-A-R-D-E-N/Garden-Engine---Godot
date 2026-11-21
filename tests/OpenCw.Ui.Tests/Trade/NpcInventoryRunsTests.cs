using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NpcInventoryRunsTests
{
    const string Npc = "000800:Fallout4.esm";

    static InventoryStack Row(uint id, int count) => new(RecordKey.Format("Fallout4.esm", id), count);

    static FakeRecordReader Reader(string[] items, string[] counts)
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Npc, "NPC_");
        record.Lists["NPC_\\ItemObject"] = items;
        record.Lists["NPC_\\ItemCount"] = counts;
        return reader;
    }

    [Fact]
    public void RowsBecomeStacksInStoredOrderWithFormattedKeys()
    {
        var reader = Reader(["001234:Fallout4.esm", "000005:Fallout4.esm"], ["3", "1"]);

        Assert.Equal(new[] { new InventoryStack("001234:Fallout4.esm", 3), new InventoryStack("000005:Fallout4.esm", 1) }, RecordRows.NpcItems(reader, Npc));
    }

    [Fact]
    public void NonPositiveCountsAreReadAndThenSkippedByTheRuns()
    {
        var reader = Reader(["000001:Fallout4.esm", "000002:Fallout4.esm", "000003:Fallout4.esm"], ["0", "-4", "2"]);

        var stacks = RecordRows.NpcItems(reader, Npc)!;

        Assert.Equal(new[] { Row(1, 0), Row(2, -4), Row(3, 2) }, stacks);
        Assert.Equal(new[] { Row(3, 2) }, RadicleNpcInventoryRecords.Runs(stacks));
    }

    [Fact]
    public void RowsPastTheCapAreRefusedNotCut()
    {
        var many = Enumerable.Range(1, RadicleContainerRecords.MaxItemRuns + 10).Select(i => Row((uint)i, 1));
        var exact = Enumerable.Range(1, RadicleContainerRecords.MaxItemRuns).Select(i => Row((uint)i, 1));

        Assert.Throws<InvalidOperationException>(() => RadicleNpcInventoryRecords.Runs(many));
        Assert.Equal(RadicleContainerRecords.MaxItemRuns, RadicleNpcInventoryRecords.Runs(exact).Count);
    }

    [Fact]
    public void AnNpcWithNoItemsIsAReadableEmptyList()
    {
        Assert.Empty(RecordRows.NpcItems(Reader([], []), Npc)!);
    }

    [Fact]
    public void ANullItemAnUnparsableCountOrUnevenRowsMakeTheInventoryUnreadable()
    {
        Assert.Null(RecordRows.NpcItems(Reader(["000001:Fallout4.esm", ""], ["1", "1"]), Npc));
        Assert.Null(RecordRows.NpcItems(Reader(["000001:Fallout4.esm", "000000:Fallout4.esm"], ["1", "1"]), Npc));
        Assert.Null(RecordRows.NpcItems(Reader(["000001:Fallout4.esm"], [""]), Npc));
        Assert.Null(RecordRows.NpcItems(Reader(["000001:Fallout4.esm"], ["many"]), Npc));
        Assert.Null(RecordRows.NpcItems(Reader(["000001:Fallout4.esm", "000002:Fallout4.esm"], ["1"]), Npc));
    }

    [Fact]
    public void AMissingRecordOrRowIsUnreadable()
    {
        Assert.Null(RecordRows.NpcItems(new FakeRecordReader(), Npc));
        var reader = new FakeRecordReader();
        reader.Add(Npc, "NPC_").Lists["NPC_\\ItemObject"] = ["000001:Fallout4.esm"];
        Assert.Null(RecordRows.NpcItems(reader, Npc));
    }
}
