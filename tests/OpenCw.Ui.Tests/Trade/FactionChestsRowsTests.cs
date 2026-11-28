using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class FactionChestsRowsTests
{
    const string Npc = "000800:Fallout4.esm";
    const string Vendors = "000A01:Fallout4.esm";
    const string Settlers = "000A02:Fallout4.esm";
    const string Raiders = "000A03:Fallout4.esm";
    const string ChestA = "000C01:Fallout4.esm";
    const string ChestB = "000C02:Fallout4.esm";

    static FakeRecordReader World(params string[] factions)
    {
        var reader = new FakeRecordReader();
        reader.Add(Npc, "NPC_").Lists["NPC_\\Faction"] = factions;
        reader.Add(Vendors, "FACT").Scalars["FACT\\MerchantContainer"] = ChestA;
        reader.Add(Settlers, "FACT");
        reader.Add(Raiders, "FACT").Scalars["FACT\\MerchantContainer"] = ChestB;
        return reader;
    }

    [Fact]
    public void EachFactionWithAMerchantContainerGivesItsChestInMembershipOrder()
    {
        Assert.Equal(new[] { ChestB, ChestA }, RadicleVendorLookup.Chests(World(Raiders, Settlers, Vendors), Npc));
    }

    [Fact]
    public void ADuplicateMembershipKeepsBothChestsAndAZeroChestIsSkipped()
    {
        var reader = World(Vendors, Vendors, Raiders);
        reader.Records[Raiders].Scalars["FACT\\MerchantContainer"] = "000000:Fallout4.esm";

        Assert.Equal(new[] { ChestA, ChestA }, RadicleVendorLookup.Chests(reader, Npc));
    }

    [Fact]
    public void AnUnknownFactionRecordIsSkipped()
    {
        Assert.Equal(new[] { ChestA }, RadicleVendorLookup.Chests(World("000FFF:Fallout4.esm", Vendors), Npc));
    }

    [Fact]
    public void AnEmptyFactionLinkMakesTheWholeListUnreadable()
    {
        Assert.Empty(RadicleVendorLookup.Chests(World(Vendors, ""), Npc));
    }

    [Fact]
    public void ANpcWithNoFactionsOrNoRecordHasNoChests()
    {
        Assert.Empty(RadicleVendorLookup.Chests(World(), Npc));
        Assert.Empty(RadicleVendorLookup.Chests(new FakeRecordReader(), Npc));
    }
}
