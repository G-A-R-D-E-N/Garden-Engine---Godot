using System.Linq;
using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordTradeTests
{
    static FakeRecordReader Items()
    {
        var reader = new FakeRecordReader();
        var gun = reader.Add("000001:a.esm", "WEAP", "Pipe", "Pipe Pistol");
        gun.Scalars["WEAP\\Value"] = "50";
        gun.Scalars["WEAP\\Weight"] = "3.5";
        reader.Add("000002:a.esm", "ALCH", "Tea", "Tea").Scalars["ALCH\\Value"] = "4";
        reader.Add("000003:a.esm", "MISC", "Cup", "Cup");
        reader.Add("000004:a.esm", "NPC_", "Npc");
        return reader;
    }

    sealed class FakeInventory : IContainerInventory
    {
        public ContainerMode Mode => ContainerMode.Loot;

        public string Title => "Footlocker";

        public double CarryLimit => 90;

        public int Revision => 4;

        public List<InventoryStack> Container { get; } = [new("000001:a.esm", 2), new("000009:a.esm", 1)];

        public List<InventoryStack> Player { get; } = [new("000002:a.esm", 5)];

        public IReadOnlyList<InventoryStack> ContainerStacks => Container;

        public IReadOnlyList<InventoryStack> PlayerStacks => Player;

        public string Refusal(ContainerSide from, string itemId, int count) => itemId == "000009:a.esm" ? "Locked." : string.Empty;

        public int Chance(ContainerSide from, string itemId) => 42;

        public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count) =>
            new(TransferStatus.Done, from + itemId + count);
    }

    sealed class FakeLedger : IBarterLedger
    {
        public string VendorName => "Vic";

        public int Revision => 2;

        public int PlayerCaps { get; set; } = 100;

        public int VendorCaps { get; set; } = 100;

        public List<InventoryStack> Player { get; } = [new("000002:a.esm", 3)];

        public List<InventoryStack> Vendor { get; } = [new("000001:a.esm", 1)];

        public IReadOnlyList<InventoryStack> PlayerStacks => Player;

        public IReadOnlyList<InventoryStack> VendorStacks => Vendor;

        public bool CanInvest => true;

        public int InvestAmount => 20;

        public double Factor { get; set; } = 0.5;

        public string Blocked { get; set; } = string.Empty;

        public (IReadOnlyList<BarterLine> Basket, int Net)? Committed { get; private set; }

        public double PriceFactor(BarterSide side) => side == BarterSide.Player ? Factor : 1.5;

        public string Refusal(BarterSide side, string itemId) => itemId == Blocked ? "Quest item." : string.Empty;

        public ActionResult Commit(IReadOnlyList<BarterLine> basket, int playerNet)
        {
            Committed = (basket, playerNet);
            return new ActionResult(true);
        }

        public ActionResult Invest() => new(true, "invested");
    }

    [Fact]
    public void ContainerStacksTakeNameWeightAndValueFromRecords()
    {
        var rules = new RecordContainerRules(new FakeInventory(), new RecordItems(Items()));

        Assert.Equal(new ContainerStack("000001:a.esm", "Pipe Pistol", 2, 3.5, 50), rules.ContainerStacks[0]);
        Assert.Equal(new ContainerStack("000009:a.esm", "000009:a.esm", 1), rules.ContainerStacks[1]);
        Assert.Equal(new ContainerStack("000002:a.esm", "Tea", 5, 0, 4), rules.PlayerStacks[0]);
    }

    [Fact]
    public void ContainerRulesForwardTheLiveState()
    {
        var rules = new RecordContainerRules(new FakeInventory(), new RecordItems(Items()));

        Assert.Equal((ContainerMode.Loot, "Footlocker", 90.0, 4), (rules.Mode, rules.Title, rules.CarryLimit, rules.Revision));
        Assert.Equal("Locked.", rules.Refusal(ContainerSide.Container, "000009:a.esm", 1));
        Assert.Equal(42, rules.Chance(ContainerSide.Player, "000002:a.esm"));
        Assert.Equal("Container000001:a.esm2", rules.Transfer(ContainerSide.Container, "000001:a.esm", 2).Reason);
    }

    [Fact]
    public void BarterVendorListHeaderComesFromTheSuppliedSource()
    {
        var rules = new RecordBarterRules(new FakeLedger(), new RecordItems(Items()), null, () => "Theirs");

        Assert.Equal("Theirs", rules.VendorListHeader);
        Assert.Equal(RecordBarterRules.MissingHeader, rules.PlayerListHeader);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BarterVendorListHeaderIsBlankWhenNoSourceHasIt(string? supplied)
    {
        Assert.Equal(RecordBarterRules.MissingHeader, new RecordBarterRules(new FakeLedger(), new RecordItems(Items())).VendorListHeader);
        Assert.Equal(RecordBarterRules.MissingHeader, new RecordBarterRules(new FakeLedger(), new RecordItems(Items()), null, () => supplied).VendorListHeader);
    }

    [Fact]
    public void ContainerPlayerListHeaderComesFromTheSuppliedSource()
    {
        var rules = new RecordContainerRules(new FakeInventory(), new RecordItems(Items()), () => "Mine");

        Assert.Equal("Mine", rules.PlayerListHeader);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ContainerPlayerListHeaderIsBlankWhenNoSourceHasIt(string? supplied)
    {
        Assert.Equal(RecordContainerRules.MissingHeader, new RecordContainerRules(new FakeInventory(), new RecordItems(Items())).PlayerListHeader);
        Assert.Equal(RecordContainerRules.MissingHeader, new RecordContainerRules(new FakeInventory(), new RecordItems(Items()), () => supplied).PlayerListHeader);
    }

    [Fact]
    public void BarterPlayerListHeaderComesFromTheSuppliedSource()
    {
        var rules = new RecordBarterRules(new FakeLedger(), new RecordItems(Items()), () => "Mine");

        Assert.Equal("Mine", rules.PlayerListHeader);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BarterPlayerListHeaderIsBlankWhenNoSourceHasIt(string? supplied)
    {
        Assert.Equal(RecordBarterRules.MissingHeader, new RecordBarterRules(new FakeLedger(), new RecordItems(Items())).PlayerListHeader);
        Assert.Equal(RecordBarterRules.MissingHeader, new RecordBarterRules(new FakeLedger(), new RecordItems(Items()), () => supplied).PlayerListHeader);
    }

    [Fact]
    public void TheRecordSourcesGiveBarterAndContainerListsTheirHeadingsInsteadOfBlanks()
    {
        var sources = new UiDataSources(Items());
        var container = sources.Container(new FakeInventory());
        var barter = sources.Barter(new FakeLedger());

        Assert.Equal(TradeHeadings.YourItems, container.PlayerListHeader);
        Assert.Equal(TradeHeadings.YourItems, barter.PlayerListHeader);
        Assert.Equal(TradeHeadings.VendorItems, barter.VendorListHeader);
        Assert.Equal("Mine", sources.Container(new FakeInventory(), () => "Mine").PlayerListHeader);
        Assert.Equal("Theirs", sources.Barter(new FakeLedger(), null, () => "Theirs").VendorListHeader);
    }

    [Fact]
    public void TradeHeadingsTakeTheGmstTextForAConfiguredIdAndFallBackOtherwise()
    {
        var reader = Items();
        reader.Add("000010:a.esm", "GMST", "sSomeHeading").Scalars[TradeHeadings.StringPath] = " Stash \0";
        reader.Add("000011:a.esm", "GMST", "sBlankHeading").Scalars[TradeHeadings.StringPath] = "  ";
        var ids = new Dictionary<TradeHeading, string>
        {
            [TradeHeading.ContainerPlayer] = "sSomeHeading",
            [TradeHeading.BarterPlayer] = "sBlankHeading",
            [TradeHeading.BarterVendor] = "sMissingHeading",
        };
        var headings = new TradeHeadings(reader, ids);

        Assert.Equal("Stash", headings.Text(TradeHeading.ContainerPlayer));
        Assert.Equal(TradeHeadings.YourItems, headings.Text(TradeHeading.BarterPlayer));
        Assert.Equal(TradeHeadings.VendorItems, headings.Text(TradeHeading.BarterVendor));
        Assert.Equal(TradeHeadings.YourItems, new TradeHeadings(reader).Text(TradeHeading.ContainerPlayer));
        Assert.Equal(TradeHeadings.VendorItems, new TradeHeadings(null, ids).Text(TradeHeading.BarterVendor));
    }

    [Fact]
    public void BarterStacksAreNamedFromRecords()
    {
        var rules = new RecordBarterRules(new FakeLedger(), new RecordItems(Items()));

        Assert.Equal([new BarterStack("000002:a.esm", "Tea", 3)], rules.PlayerStacks);
        Assert.Equal([new BarterStack("000001:a.esm", "Pipe Pistol", 1)], rules.VendorStacks);
        Assert.Equal(("Vic", 2, 100, 100, true, 20), (rules.VendorName, rules.Revision, rules.PlayerCaps, rules.VendorCaps, rules.CanInvest, rules.InvestAmount));
    }

    [Fact]
    public void UnitPriceIsRecordValueTimesTheLedgerFactor()
    {
        var rules = new RecordBarterRules(new FakeLedger(), new RecordItems(Items()));

        Assert.Equal(25, rules.UnitPrice(BarterSide.Player, "000001:a.esm"));
        Assert.Equal(75, rules.UnitPrice(BarterSide.Vendor, "000001:a.esm"));
        Assert.Equal(2, rules.UnitPrice(BarterSide.Player, "000002:a.esm"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void BadFactorMeansNoPrice(double factor)
    {
        var rules = new RecordBarterRules(new FakeLedger { Factor = factor }, new RecordItems(Items()));

        Assert.Equal(0, rules.UnitPrice(BarterSide.Player, "000001:a.esm"));
    }

    [Fact]
    public void TinyValuesStillCostOneCap()
    {
        var rules = new RecordBarterRules(new FakeLedger { Factor = 0.01 }, new RecordItems(Items()));

        Assert.Equal(1, rules.UnitPrice(BarterSide.Player, "000002:a.esm"));
    }

    [Fact]
    public void ItemsWithoutARecordValueAreRefused()
    {
        var rules = new RecordBarterRules(new FakeLedger { Blocked = "000002:a.esm" }, new RecordItems(Items()));

        Assert.Equal(0, rules.UnitPrice(BarterSide.Player, "000003:a.esm"));
        Assert.Equal(RecordBarterRules.NoPriceText, rules.Refusal(BarterSide.Player, "000003:a.esm"));
        Assert.Equal(RecordBarterRules.NoPriceText, rules.Refusal(BarterSide.Player, "000005:a.esm"));
        Assert.Equal("Quest item.", rules.Refusal(BarterSide.Player, "000002:a.esm"));
        Assert.Equal(string.Empty, rules.Refusal(BarterSide.Player, "000001:a.esm"));
    }

    [Fact]
    public void QuoteSumsLinesAndChecksCaps()
    {
        var rules = new RecordBarterRules(new FakeLedger { VendorCaps = 20, PlayerCaps = 50 }, new RecordItems(Items()));

        Assert.Equal(new BarterQuote(8, true), rules.Quote([new BarterLine(BarterSide.Player, "000002:a.esm", 4)]));
        Assert.Equal(new BarterQuote(-75, false, "Not enough caps."), rules.Quote([new BarterLine(BarterSide.Vendor, "000001:a.esm", 1)]));
        Assert.Equal(new BarterQuote(50, false, "The vendor is short of caps."), rules.Quote([new BarterLine(BarterSide.Player, "000001:a.esm", 2)]));
    }

    [Fact]
    public void CommitPassesTheNetToTheLedgerOnlyWhenAllowed()
    {
        var ledger = new FakeLedger();
        var rules = new RecordBarterRules(ledger, new RecordItems(Items()));

        Assert.True(rules.Commit([new BarterLine(BarterSide.Player, "000002:a.esm", 3)]).Ok);
        Assert.Equal(6, ledger.Committed!.Value.Net);

        ledger = new FakeLedger { PlayerCaps = 1 };
        rules = new RecordBarterRules(ledger, new RecordItems(Items()));
        var refused = rules.Commit([new BarterLine(BarterSide.Vendor, "000001:a.esm", 1)]);

        Assert.False(refused.Ok);
        Assert.Equal("Not enough caps.", refused.Reason);
        Assert.Null(ledger.Committed);
    }

    [Fact]
    public void CommitRefusesUnpricedLines()
    {
        var ledger = new FakeLedger();
        var rules = new RecordBarterRules(ledger, new RecordItems(Items()));

        var result = rules.Commit([new BarterLine(BarterSide.Player, "000003:a.esm", 1)]);

        Assert.False(result.Ok);
        Assert.Equal(RecordBarterRules.NoPriceText, result.Reason);
        Assert.Null(ledger.Committed);
    }

    [Fact]
    public void InvestGoesToTheLedger() =>
        Assert.Equal("invested", new RecordBarterRules(new FakeLedger(), new RecordItems(Items())).Invest().Reason);

    [Fact]
    public void MiscItemsAreTradableFromTheirScalarRows()
    {
        var reader = Items();
        var gear = reader.Add("000006:a.esm", "MISC", "Gear", "Gear");
        gear.Scalars["MISC\\Value"] = "30";
        gear.Scalars["MISC\\Weight"] = "0.5";
        var rules = new RecordBarterRules(new FakeLedger { Factor = 1 }, new RecordItems(reader));

        Assert.Equal(30, rules.UnitPrice(BarterSide.Player, "000006:a.esm"));
        Assert.Equal(string.Empty, rules.Refusal(BarterSide.Player, "000006:a.esm"));
    }

    [Fact]
    public void HugePricesAreClampedAndHugeTotalsRefused()
    {
        var reader = Items();
        reader.Add("000006:a.esm", "MISC", "Gold", "Gold").Scalars["MISC\\Value"] = "2147483647";
        var rules = new RecordBarterRules(new FakeLedger { Factor = 1 }, new RecordItems(reader));

        Assert.Equal(RecordBarterRules.MaxUnitPrice, rules.UnitPrice(BarterSide.Player, "000006:a.esm"));
        Assert.Equal(new BarterQuote(0, false, RecordBarterRules.TooLargeText), rules.Quote([new BarterLine(BarterSide.Player, "000006:a.esm", int.MaxValue)]));
        Assert.Equal(new BarterQuote(0, false, RecordBarterRules.TooLargeText),
            rules.Quote([.. Enumerable.Repeat(new BarterLine(BarterSide.Player, "000006:a.esm", int.MaxValue), 1000)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void LinesWithoutAPositiveCountAreRefused(int count)
    {
        var ledger = new FakeLedger();
        var rules = new RecordBarterRules(ledger, new RecordItems(Items()));
        var basket = new[] { new BarterLine(BarterSide.Player, "000002:a.esm", count) };

        Assert.Equal(new BarterQuote(0, false, RecordBarterRules.BadCountText), rules.Quote(basket));
        Assert.Equal(RecordBarterRules.BadCountText, rules.Commit(basket).Reason);
        Assert.Null(ledger.Committed);
    }

    [Fact]
    public void ContainerRulesRejectMissingCollaborators()
    {
        Assert.Equal("inventory", Assert.Throws<ArgumentNullException>(() => new RecordContainerRules(null!, new RecordItems(Items()))).ParamName);
        Assert.Equal("items", Assert.Throws<ArgumentNullException>(() => new RecordContainerRules(new FakeInventory(), null!)).ParamName);
    }
}
