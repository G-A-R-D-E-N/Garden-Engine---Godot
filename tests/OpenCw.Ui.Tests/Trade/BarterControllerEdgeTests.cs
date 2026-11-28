using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BarterControllerEdgeTests
{
    sealed class StubRules : IBarterRules
    {
        public int Revision { get; set; }

        public List<BarterStack> Player { get; } = new() { new BarterStack("pipe", "Pipe", 2) };

        public List<BarterStack> Vendor { get; } = new() { new BarterStack("gun", "Rifle", 1) };

        public Func<BarterSide, string, int> Price { get; set; } = (side, _) => side == BarterSide.Player ? 10 : 30;

        public Func<IReadOnlyList<BarterLine>, BarterQuote> QuoteOf { get; set; } = _ => new BarterQuote(0, true);

        public Func<IReadOnlyList<BarterLine>, ActionResult> CommitOf { get; set; } = _ => new ActionResult(true);

        public Func<ActionResult> InvestOf { get; set; } = () => new ActionResult(true);

        public bool CanInvest { get; set; } = true;

        public int Commits { get; private set; }

        public string VendorName => "Vic";

        public string PlayerListHeader => "Mine";

        public string VendorListHeader => "Theirs";

        public int PlayerCaps => 100;

        public int VendorCaps => 100;

        public IReadOnlyList<BarterStack> PlayerStacks => Player;

        public IReadOnlyList<BarterStack> VendorStacks => Vendor;

        public int InvestAmount => 50;

        public int UnitPrice(BarterSide side, string itemId) => Price(side, itemId);

        public string Refusal(BarterSide side, string itemId) => string.Empty;

        public BarterQuote Quote(IReadOnlyList<BarterLine> basket) => QuoteOf(basket);

        public ActionResult Commit(IReadOnlyList<BarterLine> basket)
        {
            Commits++;
            return CommitOf(basket);
        }

        public ActionResult Invest() => InvestOf();
    }

    static (BarterController Ctrl, StubRules Rules) Make()
    {
        var rules = new StubRules();
        return (new BarterController(rules), rules);
    }

    [Fact]
    public void A_commit_press_after_an_unticked_revision_change_prunes_the_basket_and_asks_again_once()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);
        rules.Player[0] = new BarterStack("pipe", "Pipe", 1);
        rules.Revision++;

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal(BarterController.ChangedText, ctrl.Message);
        Assert.Equal(new BarterLine(BarterSide.Player, "pipe", 1), ctrl.Basket.Single());
        Assert.Equal(0, rules.Commits);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Equal(BarterController.CheckAgainText, ctrl.Message);
        Assert.Equal(0, rules.Commits);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Equal(1, rules.Commits);
    }

    [Fact]
    public void Taking_back_from_two_staged_leaves_one_staged()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.TabPrev);

        Assert.Equal("Pipe x 1 (1 staged) - 10 caps", ctrl.List.RowText(0));
        Assert.Equal(1, ctrl.Basket.Single().Count);
    }

    [Fact]
    public void Reset_with_nothing_staged_says_so()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(BarterController.NothingStagedText, ctrl.Message);
    }

    [Fact]
    public void The_detail_before_anything_is_staged_is_the_side_heading_and_the_unit_price()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { new MenuTextLine("Mine", LineTone.Heading), new MenuTextLine("Pipe: 10 caps each") }, ctrl.DetailLines);
    }

    [Fact]
    public void A_row_without_a_price_reads_dashes_in_the_list_and_the_detail()
    {
        var (ctrl, rules) = Make();
        rules.Price = (_, _) => -1;
        rules.Revision++;
        ctrl.Tick(0.1);

        Assert.Equal("Pipe x 2 - --", ctrl.List.RowText(0));
        Assert.Equal("Pipe: -- each", ctrl.DetailLines[1].Text);
    }

    [Fact]
    public void Selling_only_says_you_receive_and_buying_only_says_you_pay()
    {
        var (ctrl, rules) = Make();
        rules.QuoteOf = basket => new BarterQuote(10, true);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new MenuTextLine("You receive 10 caps"), ctrl.DetailLines[^1]);

        rules.QuoteOf = basket => new BarterQuote(-30, true);
        ctrl.Handle(MenuInputAction.TabPrev);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new MenuTextLine("You pay 30 caps"), ctrl.DetailLines[^1]);
    }

    [Fact]
    public void A_quote_that_is_zero_reads_as_receiving_nothing()
    {
        var (ctrl, rules) = Make();
        rules.QuoteOf = _ => new BarterQuote(0, true);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new MenuTextLine("You receive 0 caps"), ctrl.DetailLines[^1]);
    }

    [Fact]
    public void The_most_negative_net_is_shown_without_overflowing()
    {
        var (ctrl, rules) = Make();
        rules.QuoteOf = _ => new BarterQuote(int.MinValue, false, "No.");
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new MenuTextLine("You pay 2147483648 caps"), ctrl.DetailLines[^2]);
        Assert.Equal(new MenuTextLine("No.", LineTone.Bad), ctrl.DetailLines[^1]);
    }

    [Fact]
    public void A_refused_quote_without_text_says_not_available_in_the_detail_and_on_commit()
    {
        var (ctrl, rules) = Make();
        rules.QuoteOf = _ => new BarterQuote(5, false);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new MenuTextLine(BarterController.NotAvailableText, LineTone.Bad), ctrl.DetailLines[^1]);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal(BarterController.NotAvailableText, ctrl.Message);
        Assert.Equal(0, rules.Commits);
    }

    [Fact]
    public void A_failed_commit_without_a_reason_says_the_standard_failure()
    {
        var (ctrl, rules) = Make();
        rules.CommitOf = _ => new ActionResult(false);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal(BarterController.FailedText, ctrl.Message);
        Assert.NotEmpty(ctrl.Basket);
    }

    [Fact]
    public void The_basket_lists_player_lines_before_vendor_lines_and_ids_in_ordinal_order()
    {
        var (ctrl, rules) = Make();
        rules.Player.Clear();
        rules.Player.AddRange(new[] { new BarterStack("b", "Bee", 1), new BarterStack("B", "Cap Bee", 1), new BarterStack("a", "Ay", 1) });
        rules.Revision++;
        ctrl.Tick(0.1);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Left);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(
            new[]
            {
                new BarterLine(BarterSide.Player, "B", 1),
                new BarterLine(BarterSide.Player, "a", 1),
                new BarterLine(BarterSide.Player, "b", 1),
                new BarterLine(BarterSide.Vendor, "gun", 1),
            },
            ctrl.Basket);
    }

    [Fact]
    public void Stacks_without_an_id_or_a_positive_count_are_not_listed()
    {
        var (ctrl, rules) = Make();
        rules.Player.Clear();
        rules.Player.AddRange(new[]
        {
            new BarterStack("", "Blank", 1), new BarterStack("  ", "Space", 1), new BarterStack("zero", "Zero", 0),
            new BarterStack("neg", "Neg", -2), new BarterStack("ok", "Ok", 3),
        });
        rules.Revision++;
        ctrl.Tick(0.1);

        Assert.Equal(new[] { "Ok x 3 - 10 caps" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void Asking_to_discard_shows_the_question_and_confirm_and_cancel_hints()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { new MenuTextLine("Drop the staged items and leave?", LineTone.Heading) }, ctrl.DetailLines);
        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") },
            ctrl.Hints);
    }

    [Fact]
    public void Browsing_hints_name_every_trade_action()
    {
        var (ctrl, _) = Make();

        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Sell"),
                new MenuHint(MenuInputAction.TabPrev, "Take back"),
                new MenuHint(MenuInputAction.Compare, "Trade"),
                new MenuHint(MenuInputAction.Favourite, "Reset"),
                new MenuHint(MenuInputAction.Search, "Invest"),
                new MenuHint(MenuInputAction.Back, "Exit"),
            },
            ctrl.Hints);
    }

    [Fact]
    public void A_successful_invest_says_invested_and_the_revision_it_caused_stays_silent()
    {
        var (ctrl, rules) = Make();
        rules.InvestOf = () =>
        {
            rules.Revision++;
            return new ActionResult(true);
        };
        ctrl.Handle(MenuInputAction.Search);

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Tick(0.1);

        Assert.False(ctrl.IsConfirming);
        Assert.Equal("Invested.", ctrl.Message);
    }

    [Theory]
    [InlineData("Out of caps.", "Out of caps.")]
    [InlineData("", "That didn't work.")]
    public void A_failed_invest_shows_the_reason_or_the_standard_failure(string reason, string expected)
    {
        var (ctrl, rules) = Make();
        rules.InvestOf = () => new ActionResult(false, reason);
        ctrl.Handle(MenuInputAction.Search);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(expected, ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Input_arriving_while_the_rules_commit_is_swallowed()
    {
        var (ctrl, rules) = Make();
        var inner = new List<bool>();
        rules.CommitOf = _ =>
        {
            inner.Add(ctrl.Handle(MenuInputAction.Compare));
            inner.Add(ctrl.Handle(MenuInputAction.Back));
            return new ActionResult(true);
        };
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal(new[] { true, true }, inner);
        Assert.Equal(1, rules.Commits);
        Assert.False(ctrl.IsFinished);
        Assert.Empty(ctrl.Basket);
    }

    [Fact]
    public void A_finished_menu_ignores_further_input()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.Handle(MenuInputAction.Down));
    }

    [Fact]
    public void Staging_on_the_vendor_side_shows_the_staged_count_and_the_buy_price()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Right);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Rifle x 0 (1 staged) - 30 caps", ctrl.List.RowText(0));
        Assert.Equal(new BarterLine(BarterSide.Vendor, "gun", 1), ctrl.Basket.Single());
    }

    [Fact]
    public void A_stack_name_missing_from_the_stacks_falls_back_to_the_item_id_in_the_basket_detail()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Accept);
        rules.Player.Clear();
        rules.Player.Add(new BarterStack("other", "Other", 1));

        Assert.Contains("Sell pipe x 1", WorkshopTestHelpers.Texts(ctrl));
    }
}
