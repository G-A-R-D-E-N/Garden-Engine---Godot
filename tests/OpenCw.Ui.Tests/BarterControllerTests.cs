using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BarterControllerTests
{
    internal sealed class FakeRules : IBarterRules
    {
        public string VendorName { get; set; } = "Vendor Vic";

        public string PlayerListHeader { get; set; } = "Your stuff";

        public string VendorListHeader { get; set; } = "Wares of Vic";

        public int Revision { get; set; }

        public int PlayerCaps { get; set; } = 100;

        public int VendorCaps { get; set; } = 100;

        public List<BarterStack> Player { get; } = new() { new BarterStack("pipe", "Pipe", 2), new BarterStack("quest", "Letter", 1) };

        public List<BarterStack> Vendor { get; } = new() { new BarterStack("gun", "Rifle", 1) };

        public bool CanInvest { get; set; }

        public int InvestAmount { get; set; } = 50;

        public ActionResult CommitResult { get; set; } = new(true);

        public List<IReadOnlyList<BarterLine>> Commits { get; } = new();

        public int Invests { get; private set; }

        public IReadOnlyList<BarterStack> PlayerStacks => Player;

        public IReadOnlyList<BarterStack> VendorStacks => Vendor;

        public int UnitPrice(BarterSide side, string itemId) => side == BarterSide.Player ? 10 : 30;

        public string Refusal(BarterSide side, string itemId) => itemId == "quest" ? "The vendor won't take that." : string.Empty;

        public int? NetOverride { get; set; }

        public BarterQuote Quote(IReadOnlyList<BarterLine> basket)
        {
            if (NetOverride is int forced) return new BarterQuote(forced, true);
            var net = basket.Sum(line => (line.Side == BarterSide.Player ? 10 : -30) * line.Count);
            if (net < 0 && PlayerCaps < -net) return new BarterQuote(net, false, "Not enough caps.");
            if (net > 0 && VendorCaps < net) return new BarterQuote(net, false, "The vendor is short of caps.");
            return new BarterQuote(net, true);
        }

        public ActionResult Commit(IReadOnlyList<BarterLine> basket)
        {
            Commits.Add(basket);
            return CommitResult;
        }

        public ActionResult Invest()
        {
            Invests++;
            return new ActionResult(true);
        }
    }

    static (BarterController Ctrl, FakeRules Rules) Make()
    {
        var rules = new FakeRules();
        return (new BarterController(rules), rules);
    }

    static string[] Rows(BarterController ctrl) => WorkshopTestHelpers.RowTexts(ctrl.List);

    [Fact]
    public void The_player_list_shows_stacks_with_the_unit_price_and_the_header_shows_both_caps()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { "Pipe x 2 - 10 caps", "Letter x 1 - 10 caps" }, Rows(ctrl));
        Assert.Equal("Your caps 100 | Vendor caps 100", ctrl.Header);
        Assert.Equal("Vendor Vic", ctrl.Title);
        Assert.Equal("Sell", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
    }

    [Fact]
    public void Switching_sides_shows_the_vendor_list_and_the_buy_label()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(new[] { "Rifle x 1 - 30 caps" }, Rows(ctrl));
        Assert.Equal("Buy", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);

        ctrl.Handle(MenuInputAction.Left);

        Assert.Equal(new[] { "Pipe x 2 - 10 caps", "Letter x 1 - 10 caps" }, Rows(ctrl));
        Assert.Equal("Sell", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
    }

    [Fact]
    public void Each_side_shows_the_rules_heading_in_the_detail_and_the_summary()
    {
        var ctrl = new BarterController(new FakeRules { PlayerListHeader = "Mine-HDR", VendorListHeader = "Theirs-HDR" });

        Assert.Equal(("Mine-HDR", "Mine-HDR"), (ctrl.DetailLines[0].Text, ctrl.Rows.Summary));

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(("Theirs-HDR", "Theirs-HDR"), (ctrl.DetailLines[0].Text, ctrl.Rows.Summary));
    }

    [Fact]
    public void Blank_rule_headings_are_not_replaced_by_text_of_the_menus_own()
    {
        var ctrl = new BarterController(new FakeRules { PlayerListHeader = "", VendorListHeader = "" });

        Assert.Equal(("", ""), (ctrl.DetailLines[0].Text, ctrl.Rows.Summary));

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(("", ""), (ctrl.DetailLines[0].Text, ctrl.Rows.Summary));
    }

    [Fact]
    public void Staging_reduces_the_available_count_and_stops_at_what_the_stack_holds()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("Pipe x 1 (1 staged) - 10 caps", ctrl.List.RowText(0));

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("Pipe x 0 (2 staged) - 10 caps", ctrl.List.RowText(0));

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(BarterController.NothingLeftText, ctrl.Message);
        Assert.Equal(2, ctrl.Basket.Single().Count);
    }

    [Fact]
    public void Taking_back_restores_the_count_and_an_unstaged_row_says_nothing_is_staged()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.TabPrev);

        Assert.Equal("Pipe x 2 - 10 caps", ctrl.List.RowText(0));
        Assert.Empty(ctrl.Basket);

        ctrl.Handle(MenuInputAction.TabPrev);
        Assert.Equal(BarterController.NothingStagedText, ctrl.Message);
    }

    [Fact]
    public void An_item_the_rules_refuse_is_not_staged_and_the_refusal_is_shown()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("The vendor won't take that.", ctrl.Message);
        Assert.Empty(ctrl.Basket);
    }

    [Fact]
    public void The_quote_comes_from_the_rules_and_the_detail_says_who_pays()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(-20, ctrl.Quote!.PlayerNet);
        var texts = WorkshopTestHelpers.Texts(ctrl);
        Assert.Contains("Sell Pipe x 1", texts);
        Assert.Contains("Buy Rifle x 1", texts);
        Assert.Contains("You pay 20 caps", texts);
    }

    [Fact]
    public void A_quote_that_cannot_commit_shows_its_refusal_and_commit_does_not_call_the_rules()
    {
        var (ctrl, rules) = Make();
        rules.PlayerCaps = 5;
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal("Not enough caps.", ctrl.Message);
        Assert.Empty(rules.Commits);
        Assert.NotEmpty(ctrl.Basket);
    }

    [Fact]
    public void Commit_sends_the_whole_basket_once_clears_it_and_says_traded()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Compare);

        var sent = rules.Commits.Single();
        Assert.Equal(new[] { new BarterLine(BarterSide.Player, "pipe", 2), new BarterLine(BarterSide.Vendor, "gun", 1) }, sent);
        Assert.Empty(ctrl.Basket);
        Assert.Equal(BarterController.TradedText, ctrl.Message);
    }

    [Fact]
    public void A_commit_raises_the_trade_cue_and_a_failed_one_does_not()
    {
        var (ctrl, rules) = Make();
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;
        ctrl.Handle(MenuInputAction.Accept);
        rules.CommitResult = new ActionResult(false, "Deal fell through.");
        ctrl.Handle(MenuInputAction.Compare);
        Assert.DoesNotContain("trade.commit", sounds);

        rules.CommitResult = new ActionResult(true);
        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal(1, sounds.Count(s => s == "trade.commit"));
    }

    [Fact]
    public void A_successful_invest_stays_silent_because_only_the_commit_is_evidenced()
    {
        var (ctrl, rules) = Make();
        rules.CanInvest = true;
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;
        ctrl.Handle(MenuInputAction.Search);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, rules.Invests);
        Assert.DoesNotContain("trade.commit", sounds);
    }

    [Fact]
    public void A_failed_commit_keeps_the_basket_and_shows_the_reason()
    {
        var (ctrl, rules) = Make();
        rules.CommitResult = new ActionResult(false, "Deal fell through.");
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.NotEmpty(ctrl.Basket);
        Assert.Equal("Deal fell through.", ctrl.Message);
    }

    [Fact]
    public void An_empty_basket_cannot_be_committed()
    {
        var (ctrl, rules) = Make();

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Empty(rules.Commits);
        Assert.Equal(BarterController.NothingStagedText, ctrl.Message);
    }

    [Fact]
    public void Reset_clears_the_basket_without_asking()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Empty(ctrl.Basket);
        Assert.Equal("Pipe x 2 - 10 caps", ctrl.List.RowText(0));
    }

    [Fact]
    public void A_stale_inventory_clamps_the_basket_and_blocks_the_next_commit_press_once()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);

        rules.Player[0] = new BarterStack("pipe", "Pipe", 1);
        rules.Revision++;
        ctrl.Tick(0.1);

        Assert.Equal(BarterController.ChangedText, ctrl.Message);
        Assert.Equal(1, ctrl.Basket.Single().Count);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Equal(BarterController.CheckAgainText, ctrl.Message);
        Assert.Empty(rules.Commits);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Single(rules.Commits);
    }

    [Fact]
    public void A_stack_that_vanished_is_dropped_from_the_basket()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        rules.Player.RemoveAt(0);
        rules.Revision++;
        ctrl.Tick(0.1);

        Assert.Empty(ctrl.Basket);
        Assert.Equal(BarterController.ChangedText, ctrl.Message);
    }

    [Fact]
    public void A_revision_change_that_touches_nothing_in_the_basket_is_silent()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        rules.PlayerCaps = 250;
        rules.Revision++;
        ctrl.Tick(0.1);

        Assert.Equal(string.Empty, ctrl.Message);
        Assert.Equal("Your caps 250 | Vendor caps 100", ctrl.Header);
    }

    [Fact]
    public void Back_with_an_empty_basket_closes_at_once()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void Back_with_a_basket_asks_to_discard_and_cancel_keeps_it()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);
        Assert.True(ctrl.IsConfirming);
        Assert.False(ctrl.IsFinished);
        ctrl.Handle(MenuInputAction.Back);
        Assert.False(ctrl.IsConfirming);
        Assert.NotEmpty(ctrl.Basket);

        ctrl.Handle(MenuInputAction.Back);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.IsFinished);
        Assert.Empty(rules.Commits);
    }

    [Fact]
    public void Invest_needs_the_option_and_an_empty_basket_and_asks_before_calling_the_rules()
    {
        var (ctrl, rules) = Make();

        ctrl.Handle(MenuInputAction.Search);
        Assert.Equal(BarterController.NotAvailableText, ctrl.Message);

        rules.CanInvest = true;
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Search);
        Assert.Equal(BarterController.NotAvailableText, ctrl.Message);
        Assert.False(ctrl.IsConfirming);
        ctrl.Handle(MenuInputAction.TabPrev);

        ctrl.Handle(MenuInputAction.Search);
        Assert.True(ctrl.IsConfirming);
        Assert.Contains("Invest 50 caps in this vendor?", WorkshopTestHelpers.Texts(ctrl));
        Assert.Equal(0, rules.Invests);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, rules.Invests);
    }

    [Fact]
    public void A_price_the_rules_cannot_give_reads_as_dashes()
    {
        Assert.Equal("--", BarterController.PriceText(-1));
        Assert.Equal("12 caps", BarterController.PriceText(12));
    }
}
