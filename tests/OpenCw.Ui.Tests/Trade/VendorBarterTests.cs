using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VendorBarterTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const string Tea = "000101:a.esm";
    const string Chest = "000900:a.esm";

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => refKey == "bad" ? null : new ActorSetup("Vic", true, [new("ignored:a.esm", 9)]);
    }

    sealed class Chests : IContainerRecords
    {
        public int Reads { get; private set; }

        public ContainerSetup? Read(string refKey, string baseKey)
        {
            Reads++;
            return refKey == Chest ? new ContainerSetup("Chest", false, false, [new(Gun, 2), new(Caps, 100)]) : null;
        }
    }

    sealed class Items : IItemRecords
    {
        public ItemRecord? Find(string id) => id switch
        {
            Gun => new ItemRecord(id, "WEAP", "Gun", 30, 1),
            Tea => new ItemRecord(id, "ALCH", "Tea", 5, 0.1),
            _ => null,
        };
    }

    static ActorInventory Open(Chests chests, ContainerLedger ledger, PlayerItems player, string npc = "npc1", string chest = Chest)
    {
        var inventory = ActorInventory.OpenVendor(npc, "base", chest, "chestbase", new Actors(), chests, ledger, player, Caps, BarterPricing.Factor, out var refusal);
        Assert.Equal(string.Empty, refusal);
        return inventory!;
    }

    static PlayerItems Player(params InventoryStack[] stacks)
    {
        var player = new PlayerItems();
        player.Replace(stacks);
        return player;
    }

    [Fact]
    public void StockAndCapsComeFromTheChestNotTheActor()
    {
        var ledger = new ContainerLedger();
        var inventory = Open(new Chests(), ledger, Player());

        Assert.Equal("Vic", inventory.VendorName);
        Assert.Equal(new[] { new InventoryStack(Gun, 2) }, ((IBarterLedger)inventory).VendorStacks);
        Assert.Equal(100, inventory.VendorCaps);
        Assert.Equal(new[] { Chest }, ledger.References);
    }

    [Fact]
    public void PricesAreValueOneToOneBothWays()
    {
        var inventory = Open(new Chests(), new ContainerLedger(), Player());
        var rules = new RecordBarterRules(inventory, new Items());

        Assert.Equal(30, rules.UnitPrice(BarterSide.Vendor, Gun));
        Assert.Equal(5, rules.UnitPrice(BarterSide.Player, Tea));
    }

    [Fact]
    public void BuyAndSellMoveItemsAndCapsAndSurviveSaveRoundTrip()
    {
        var chests = new Chests();
        var ledger = new ContainerLedger();
        var player = Player(new InventoryStack(Caps, 80), new InventoryStack(Tea, 4));
        var rules = new RecordBarterRules(Open(chests, ledger, player), new Items());

        var result = rules.Commit([new(BarterSide.Vendor, Gun, 2), new(BarterSide.Player, Tea, 4)]);

        Assert.True(result.Ok);
        Assert.Equal(2, player.CountOf(Gun));
        Assert.Equal(0, player.CountOf(Tea));
        Assert.Equal(40, player.CountOf(Caps));
        Assert.Equal(140, ledger.Find(Chest)!.CountOf(Caps));

        var sections = ContainerStateSave.Sections(ledger, player);
        var restoredLedger = new ContainerLedger();
        var restoredPlayer = new PlayerItems();
        ContainerStateSave.Apply(sections, restoredLedger, restoredPlayer);
        var reopened = Open(chests, restoredLedger, restoredPlayer);

        Assert.Equal(1, chests.Reads);
        Assert.Equal(140, reopened.VendorCaps);
        Assert.Equal(new[] { new InventoryStack(Tea, 4) }, ((IBarterLedger)reopened).VendorStacks);
        Assert.Equal(40, reopened.PlayerCaps);
    }

    [Fact]
    public void RefusesWhenPlayerLacksCapsOrVendorIsShort()
    {
        var ledger = new ContainerLedger();
        var player = Player(new InventoryStack(Caps, 10), new InventoryStack(Tea, 100));
        var rules = new RecordBarterRules(Open(new Chests(), ledger, player), new Items());

        Assert.False(rules.Commit([new(BarterSide.Vendor, Gun, 1)]).Ok);
        Assert.False(rules.Commit([new(BarterSide.Player, Tea, 100)]).Ok);
        Assert.Equal(100, ledger.Find(Chest)!.CountOf(Caps));
        Assert.Equal(100, player.CountOf(Tea));
    }

    [Fact]
    public void TwoVendorsSharingAChestShareOneStock()
    {
        var chests = new Chests();
        var ledger = new ContainerLedger();
        var player = Player(new InventoryStack(Caps, 100));
        Assert.True(new RecordBarterRules(Open(chests, ledger, player, "npcA"), new Items()).Commit([new(BarterSide.Vendor, Gun, 2)]).Ok);

        var other = Open(chests, ledger, player, "npcB");

        Assert.Empty(((IBarterLedger)other).VendorStacks);
        Assert.Equal(1, chests.Reads);
    }

    [Fact]
    public void MissingChestOrActorRefusesWithoutFreezing()
    {
        var ledger = new ContainerLedger();
        var chestless = ActorInventory.OpenVendor("npc1", "b", "nochest", "cb", new Actors(), new Chests(), ledger, Player(), Caps, BarterPricing.Factor, out var r1);
        var actorless = ActorInventory.OpenVendor("bad", "b", Chest, "cb", new Actors(), new Chests(), ledger, Player(), Caps, BarterPricing.Factor, out var r2);

        Assert.Null(chestless);
        Assert.Null(actorless);
        Assert.Equal(ActorInventory.NoStockText, r1);
        Assert.Equal(ActorInventory.MissingText, r2);
        Assert.Empty(ledger.References);
    }
}

public class VendorLookupEvidenceTests
{
    [Fact]
    public void CapsItemIsCaps001InFallout4Esm()
    {
        Assert.Equal("00000F:Fallout4.esm", BarterPricing.CapsItemId);
    }

    [Fact]
    public void VendorKeywordsAreTheSixVendorContainerKeywordsFormList()
    {
        Assert.Equal(6, RadicleVendorLookup.Keywords.Count);
        Assert.Contains("10FCD1:Fallout4.esm", RadicleVendorLookup.Keywords);
        Assert.Contains("2269A0:Fallout4.esm", RadicleVendorLookup.Keywords);
        Assert.DoesNotContain("00000F:Fallout4.esm", RadicleVendorLookup.Keywords);
    }

    [Fact]
    public void AKeywordLinkedChestResolvesThroughVendorChests()
    {
        var chests = new VendorChests(new Linked(("10FCD1:Fallout4.esm", "ABCDEF:Fallout4.esm"), ("000001:Fallout4.esm", "000002:Fallout4.esm")));

        Assert.Equal("ABCDEF:Fallout4.esm", chests.ChestKey("npc", "base"));
    }

    sealed class Linked(params (string, string)[] links) : IVendorRecords
    {
        public IReadOnlySet<string> VendorKeywords => RadicleVendorLookup.Keywords;

        public IEnumerable<(string Keyword, string Reference)> LinkedRefs(string npcRefKey) => links;

        public IEnumerable<string?> FactionChests(string npcBaseKey) => [];
    }
}
