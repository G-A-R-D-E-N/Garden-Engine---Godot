using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VendorChestsTests
{
    private sealed class Fake(params (string, string)[] linked) : IVendorRecords
    {
        public IReadOnlySet<string> VendorKeywords { get; } = new HashSet<string> { "kw01", "kw05" };
        public string?[] Factions { get; init; } = [];
        public IEnumerable<(string Keyword, string Reference)> LinkedRefs(string npcRefKey) => linked;
        public IEnumerable<string?> FactionChests(string npcBaseKey) => Factions;
    }

    [Fact]
    public void Keyword_linked_chests_come_first_in_link_order_then_faction_chests()
    {
        var chests = new VendorChests(new Fake(("kw05", "chestB"), ("other", "door"), ("kw01", "chestA")) { Factions = ["fac"] });
        Assert.Equal(["chestB", "chestA", "fac"], chests.Resolve("r", "b"));
        Assert.Equal("chestB", chests.ChestKey("r", "b"));
    }

    [Fact]
    public void The_first_chest_is_the_keyword_one_even_when_a_faction_also_names_a_chest()
    {
        var chests = new VendorChests(new Fake(("kw01", "chestA")) { Factions = ["fac"] });
        Assert.Equal("chestA", chests.ChestKey("r", "b"));
    }

    [Fact]
    public void The_faction_chest_is_used_when_no_keyword_link_names_one()
    {
        var chests = new VendorChests(new Fake(("other", "door")) { Factions = [null, "facA", "facB"] });
        Assert.Equal("facA", chests.ChestKey("r", "b"));
        Assert.Equal(["facA", "facB"], chests.Resolve("r", "b"));
    }

    [Fact]
    public void No_keyword_link_and_no_faction_chest_gives_no_chest()
    {
        var chests = new VendorChests(new Fake { Factions = [null, ""] });
        Assert.Null(chests.ChestKey("r", "b"));
    }

    [Fact]
    public void Duplicates_and_empty_references_are_dropped()
    {
        var chests = new VendorChests(new Fake(("kw01", "chestA")) { Factions = [null, "", "chestA", "chestC"] });
        Assert.Equal(["chestA", "chestC"], chests.Resolve("r", "b"));
    }

    [Fact]
    public void Non_vendor_keywords_alone_give_no_chest()
    {
        var chests = new VendorChests(new Fake(("other", "x")));
        Assert.Empty(chests.Resolve("r", "b"));
        Assert.Null(chests.ChestKey("r", "b"));
    }
}
