using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordTradeRulesEdgeTests
{
    const string Tea = "000002:a.esm";
    const string Pipe = "000001:a.esm";
    const string Cup = "000003:a.esm";
    const string Gold = "000006:a.esm";

    sealed class StubLedger : IBarterLedger
    {
        public double Player { get; set; } = 0.5;

        public double Vendor { get; set; } = 1.5;

        public int PlayerCaps { get; set; } = 1000;

        public int VendorCaps { get; set; } = 1000;

        public Dictionary<string, string> Refusals { get; } = new();

        public List<InventoryStack> Stacks { get; } = new();

        public List<(IReadOnlyList<BarterLine> Basket, int Net)> Commits { get; } = new();

        public string VendorName => "Vic";

        public int Revision => 0;

        public IReadOnlyList<InventoryStack> PlayerStacks => Stacks;

        public IReadOnlyList<InventoryStack> VendorStacks => Stacks;

        public bool CanInvest => false;

        public int InvestAmount => 0;

        public double PriceFactor(BarterSide side) => side == BarterSide.Player ? Player : Vendor;

        public string Refusal(BarterSide side, string itemId) => Refusals.GetValueOrDefault(itemId, string.Empty);

        public ActionResult Commit(IReadOnlyList<BarterLine> basket, int playerNet)
        {
            Commits.Add((basket, playerNet));
            return new ActionResult(true);
        }

        public ActionResult Invest() => new(true);
    }

    static FakeRecordReader Items()
    {
        var reader = new FakeRecordReader();
        reader.Add(Pipe, "WEAP", "Pipe", "Pipe Pistol").Scalars["WEAP\\Value"] = "50";
        reader.Add(Tea, "ALCH", "Tea", "Tea").Scalars["ALCH\\Value"] = "4";
        reader.Add(Cup, "MISC", "Cup", "Cup");
        reader.Add(Gold, "MISC", "Gold", "Gold").Scalars["MISC\\Value"] = "2147483647";
        return reader;
    }

    static RecordBarterRules Rules(StubLedger ledger, FakeRecordReader? reader = null) => new(ledger, new RecordItems(reader ?? Items()));

    static BarterLine Sell(string id, int count) => new(BarterSide.Player, id, count);

    static BarterLine Buy(string id, int count) => new(BarterSide.Vendor, id, count);

    [Theory]
    [InlineData("5", 0.5, 3)]
    [InlineData("7", 0.5, 4)]
    [InlineData("3", 1.5, 5)]
    [InlineData("1", 0.5, 1)]
    [InlineData("10", 1.0, 10)]
    [InlineData("50", 2.0, 100)]
    public void A_unit_price_rounds_halves_up_and_never_drops_below_one_cap(string value, double factor, int expected)
    {
        var reader = Items();
        reader.Records[Tea].Scalars["ALCH\\Value"] = value;
        var rules = Rules(new StubLedger { Player = factor }, reader);

        Assert.Equal(expected, rules.UnitPrice(BarterSide.Player, Tea));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("lots")]
    public void A_record_value_that_is_not_a_positive_whole_number_has_no_price_and_is_refused(string value)
    {
        var reader = Items();
        reader.Records[Tea].Scalars["ALCH\\Value"] = value;
        var rules = Rules(new StubLedger(), reader);

        Assert.Equal(0, rules.UnitPrice(BarterSide.Player, Tea));
        Assert.Equal(RecordBarterRules.NoPriceText, rules.Refusal(BarterSide.Player, Tea));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_infinite_price_factor_means_no_price(double factor)
    {
        Assert.Equal(0, Rules(new StubLedger { Vendor = factor }).UnitPrice(BarterSide.Vendor, Pipe));
    }

    [Fact]
    public void A_huge_factor_clamps_the_price_to_the_maximum()
    {
        Assert.Equal(RecordBarterRules.MaxUnitPrice, Rules(new StubLedger { Player = 1e12 }).UnitPrice(BarterSide.Player, Tea));
    }

    [Fact]
    public void The_ledger_refusal_wins_over_the_missing_price_text()
    {
        var ledger = new StubLedger();
        ledger.Refusals[Cup] = "Quest item.";

        Assert.Equal("Quest item.", Rules(ledger).Refusal(BarterSide.Player, Cup));
    }

    [Fact]
    public void A_mixed_basket_nets_what_the_player_earns_against_what_the_player_pays()
    {
        var rules = Rules(new StubLedger());

        Assert.Equal(new BarterQuote(-67, true), rules.Quote([Sell(Tea, 4), Buy(Pipe, 1)]));
    }

    [Fact]
    public void A_basket_that_cancels_out_is_a_free_trade()
    {
        var rules = Rules(new StubLedger { PlayerCaps = 0, VendorCaps = 0 });

        Assert.Equal(new BarterQuote(0, true), rules.Quote([Sell(Pipe, 3), Buy(Pipe, 1)]));
    }

    [Fact]
    public void An_empty_basket_quotes_zero_and_can_commit()
    {
        Assert.Equal(new BarterQuote(0, true), Rules(new StubLedger()).Quote([]));
    }

    [Fact]
    public void The_player_may_spend_exactly_all_their_caps_but_not_one_more()
    {
        Assert.Equal(new BarterQuote(-67, true), Rules(new StubLedger { PlayerCaps = 67 }).Quote([Sell(Tea, 4), Buy(Pipe, 1)]));
        Assert.Equal(new BarterQuote(-67, false, "Not enough caps."),
            Rules(new StubLedger { PlayerCaps = 66 }).Quote([Sell(Tea, 4), Buy(Pipe, 1)]));
    }

    [Fact]
    public void The_vendor_may_pay_out_exactly_all_their_caps_but_not_one_more()
    {
        Assert.Equal(new BarterQuote(8, true), Rules(new StubLedger { VendorCaps = 8 }).Quote([Sell(Tea, 4)]));
        Assert.Equal(new BarterQuote(8, false, "The vendor is short of caps."), Rules(new StubLedger { VendorCaps = 7 }).Quote([Sell(Tea, 4)]));
    }

    [Fact]
    public void One_line_without_a_count_spoils_an_otherwise_good_basket()
    {
        Assert.Equal(new BarterQuote(0, false, RecordBarterRules.BadCountText), Rules(new StubLedger()).Quote([Sell(Tea, 4), Buy(Pipe, 0)]));
    }

    [Fact]
    public void A_total_just_inside_the_int_range_is_quoted_and_one_line_more_is_too_large()
    {
        var rules = Rules(new StubLedger { Player = 1, VendorCaps = int.MaxValue });

        Assert.Equal(new BarterQuote(2_140_000_000, true), rules.Quote([Sell(Gold, 214)]));
        Assert.Equal(new BarterQuote(0, false, RecordBarterRules.TooLargeText), rules.Quote([Sell(Gold, 215)]));
    }

    [Fact]
    public void Commit_reports_the_first_refused_line_in_basket_order()
    {
        var ledger = new StubLedger();
        ledger.Refusals[Tea] = "Quest item.";
        var rules = Rules(ledger);

        Assert.Equal(new ActionResult(false, "Quest item."), rules.Commit([Sell(Tea, 1), Sell(Cup, 1)]));
        Assert.Equal(new ActionResult(false, RecordBarterRules.NoPriceText), rules.Commit([Sell(Cup, 1), Sell(Tea, 1)]));
        Assert.Empty(ledger.Commits);
    }

    [Fact]
    public void Commit_hands_the_ledger_the_basket_and_the_quoted_net()
    {
        var ledger = new StubLedger();
        var basket = new[] { Sell(Tea, 4), Buy(Pipe, 1) };

        var result = Rules(ledger).Commit(basket);

        Assert.True(result.Ok);
        Assert.Equal(basket, ledger.Commits.Single().Basket);
        Assert.Equal(-67, ledger.Commits.Single().Net);
    }

    [Fact]
    public void A_stack_with_no_record_is_named_by_its_id_in_the_barter_lists()
    {
        var ledger = new StubLedger();
        ledger.Stacks.Add(new InventoryStack("000099:a.esm", 3));

        Assert.Equal(new BarterStack("000099:a.esm", "000099:a.esm", 3), Rules(ledger).PlayerStacks.Single());
    }

    [Fact]
    public void An_item_with_a_record_but_no_value_or_weight_lists_as_zero_in_a_container()
    {
        var inventory = new StubInventory();
        inventory.Player.Add(new InventoryStack(Cup, 2));
        var rules = new RecordContainerRules(inventory, new RecordItems(Items()));

        Assert.Equal(new ContainerStack(Cup, "Cup", 2), rules.PlayerStacks.Single());
        Assert.Equal(0, rules.PlayerStacks.Single().Value);
        Assert.Equal(0.0, rules.PlayerStacks.Single().Weight);
    }

    [Fact]
    public void Rules_need_their_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordBarterRules(null!, new RecordItems(Items())));
        Assert.Throws<ArgumentNullException>(() => new RecordBarterRules(new StubLedger(), null!));
        Assert.Throws<ArgumentNullException>(() => new RecordContainerRules(null!, new RecordItems(Items())));
        Assert.Throws<ArgumentNullException>(() => new RecordContainerRules(new StubInventory(), null!));
    }

    sealed class StubInventory : IContainerInventory
    {
        public List<InventoryStack> Player { get; } = new();

        public ContainerMode Mode => ContainerMode.Loot;

        public string Title => "Crate";

        public double CarryLimit => 0;

        public int Revision => 0;

        public IReadOnlyList<InventoryStack> ContainerStacks => Array.Empty<InventoryStack>();

        public IReadOnlyList<InventoryStack> PlayerStacks => Player;

        public string Refusal(ContainerSide from, string itemId, int count) => string.Empty;

        public int Chance(ContainerSide from, string itemId) => 0;

        public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count) => new(TransferStatus.Done);
    }
}
