using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NpcFactionRowsTests
{
    const string Npc = "000800:Fallout4.esm";
    const string Vendors = "000A01:Fallout4.esm";
    const string Raiders = "000A03:Fallout4.esm";

    static FakeRecordReader World(string[] factions, string[] ranks)
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Npc, "NPC_");
        record.Lists["NPC_\\Faction"] = factions;
        record.Lists["NPC_\\FactionRank"] = ranks;
        return reader;
    }

    [Fact]
    public void EachMembershipKeepsItsSignedRankInStoredOrder()
    {
        Assert.Equal(new[] { (Raiders, -2), (Vendors, 3) }, RecordRows.NpcFactions(World([Raiders, Vendors], ["-2", "3"]), Npc));
    }

    [Fact]
    public void ANpcWithNoFactionsIsAReadableEmptyList()
    {
        Assert.Empty(RecordRows.NpcFactions(World([], []), Npc)!);
    }

    [Fact]
    public void AnEmptyFactionLinkOrAZeroFactionMakesTheWholeListUnreadable()
    {
        Assert.Null(RecordRows.NpcFactions(World([Vendors, ""], ["1", "2"]), Npc));
        Assert.Null(RecordRows.NpcFactions(World([Vendors, "000000:Fallout4.esm"], ["1", "2"]), Npc));
    }

    [Fact]
    public void AnUnparsableRankMakesTheWholeListUnreadable()
    {
        Assert.Null(RecordRows.NpcFactions(World([Vendors, Raiders], ["1", "x"]), Npc));
    }

    [Fact]
    public void UnevenFactionAndRankListsAreUnreadable()
    {
        Assert.Null(RecordRows.NpcFactions(World([Vendors, Raiders], ["1"]), Npc));
    }

    [Fact]
    public void AnAbsentNpcIsUnreadable()
    {
        Assert.Null(RecordRows.NpcFactions(new FakeRecordReader(), Npc));
    }
}
