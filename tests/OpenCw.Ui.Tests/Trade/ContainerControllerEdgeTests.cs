using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ContainerControllerEdgeTests
{
    sealed class StubRules : IContainerRules
    {
        public ContainerMode Mode { get; set; } = ContainerMode.Loot;

        public string Title => "Crate";

        public string PlayerListHeader => "Pack";

        public double CarryLimit { get; set; }

        public int Revision { get; set; }

        public List<ContainerStack> Container { get; } = new();

        public List<ContainerStack> Player { get; } = new();

        public Dictionary<string, string?> Refusals { get; } = new();

        public bool RefusalThrows { get; set; }

        public Func<int> ChanceOf { get; set; } = () => 0;

        public Func<string, ContainerTransferResult?> TransferOf { get; set; } = _ => new ContainerTransferResult(TransferStatus.Done);

        public List<(ContainerSide From, string Id, int Count)> Moves { get; } = new();

        public IReadOnlyList<ContainerStack> ContainerStacks => Container;

        public IReadOnlyList<ContainerStack> PlayerStacks => Player;

        public string Refusal(ContainerSide from, string itemId, int count)
        {
            if (RefusalThrows) throw new InvalidOperationException("nope");
            return Refusals.GetValueOrDefault(itemId)!;
        }

        public int Chance(ContainerSide from, string itemId) => ChanceOf();

        public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count)
        {
            Moves.Add((from, itemId, count));
            return TransferOf(itemId)!;
        }
    }

    static (ContainerController Ctrl, StubRules Rules) Make(ContainerMode mode = ContainerMode.Loot, params ContainerStack[] container)
    {
        var rules = new StubRules { Mode = mode };
        rules.Container.AddRange(container);
        return (new ContainerController(rules), rules);
    }

    static (string, LineTone)[] Lines(ContainerController ctrl) => ctrl.DetailLines.Select(line => (line.Text, line.Tone)).ToArray();

    [Theory]
    [InlineData(ContainerMode.Loot, "Loot")]
    [InlineData(ContainerMode.Steal, "Steal")]
    [InlineData(ContainerMode.Pickpocket, "Pickpocket")]
    [InlineData(ContainerMode.Teammate, "Teammate")]
    [InlineData(ContainerMode.PowerArmor, "Power armor")]
    [InlineData(ContainerMode.Reload, "Reload")]
    [InlineData(ContainerMode.Workbench, "Workbench")]
    [InlineData((ContainerMode)99, "Loot")]
    public void Every_mode_has_its_word_and_an_unknown_mode_reads_as_loot(ContainerMode mode, string expected)
    {
        Assert.Equal(expected, ContainerController.ModeWord(mode));
    }

    [Theory]
    [InlineData(ContainerMode.Loot, ContainerSide.Container, "Take")]
    [InlineData(ContainerMode.Loot, ContainerSide.Player, "Store")]
    [InlineData(ContainerMode.Teammate, ContainerSide.Container, "Take")]
    [InlineData(ContainerMode.Steal, ContainerSide.Container, "Steal")]
    [InlineData(ContainerMode.Steal, ContainerSide.Player, "Store")]
    [InlineData(ContainerMode.Pickpocket, ContainerSide.Container, "Steal")]
    [InlineData(ContainerMode.Pickpocket, ContainerSide.Player, "Place")]
    [InlineData(ContainerMode.Reload, ContainerSide.Container, "Unload")]
    [InlineData(ContainerMode.Reload, ContainerSide.Player, "Load")]
    public void The_verb_follows_the_mode_and_the_side(ContainerMode mode, ContainerSide side, string expected)
    {
        var (ctrl, _) = Make(mode);
        if (side == ContainerSide.Player) ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(expected, ctrl.Verb);
    }

    [Theory]
    [InlineData(ContainerMode.Loot, "Take all")]
    [InlineData(ContainerMode.Workbench, "Take all")]
    [InlineData(ContainerMode.Reload, "Unload all")]
    [InlineData(ContainerMode.Steal, "Steal all")]
    [InlineData(ContainerMode.Pickpocket, "Steal all")]
    public void The_take_all_label_follows_the_mode(ContainerMode mode, string expected)
    {
        Assert.Equal(expected, Make(mode).Ctrl.TakeAllLabel);
    }

    [Fact]
    public void A_stack_card_shows_count_weight_value_and_value_per_weight()
    {
        var (ctrl, _) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 2, 1.5, 4));

        Assert.Equal(
            new[]
            {
                ("Loot", LineTone.Heading),
                ("Axe", LineTone.Heading),
                ("Count 2, weight 1.5, value 4", LineTone.Normal),
                ("Value per weight 2.67", LineTone.Normal),
            },
            Lines(ctrl));
    }

    [Fact]
    public void A_weightless_stack_uses_its_value_as_the_value_per_weight()
    {
        var (ctrl, _) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 2, 0, 7));

        Assert.Equal(("Count 2, weight 0, value 7", LineTone.Normal), Lines(ctrl)[2]);
        Assert.Equal(("Value per weight 7", LineTone.Normal), Lines(ctrl)[3]);
    }

    [Fact]
    public void The_picker_card_shows_the_quantity_and_the_total_weight_and_value()
    {
        var (ctrl, _) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 8, 1.25, 3));
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(
            new[]
            {
                ("Loot", LineTone.Heading),
                ("Axe", LineTone.Heading),
                ("Count 8, weight 1.25, value 3", LineTone.Normal),
                ("Value per weight 2.4", LineTone.Normal),
                ("Quantity 3 of 8", LineTone.Heading),
                ("Total weight 3.75, value 9", LineTone.Normal),
            },
            Lines(ctrl));
    }

    static bool IsPicking(ContainerController ctrl) => ctrl.Hints.Any(h => h.Action == MenuInputAction.Back && h.Label == "Cancel") && ctrl.Hints.Count == 2;

    [Fact]
    public void A_stack_of_exactly_the_limit_moves_one_and_one_more_opens_the_picker()
    {
        var (five, fiveRules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", ContainerController.WholeStackLimit));
        var (six, sixRules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", ContainerController.WholeStackLimit + 1));

        five.Handle(MenuInputAction.Accept);
        six.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { (ContainerSide.Container, "x", 1) }, fiveRules.Moves);
        Assert.False(IsPicking(five));
        Assert.Empty(sixRules.Moves);
        Assert.True(IsPicking(six));
    }

    [Fact]
    public void The_picker_steps_by_one_and_by_five_and_clamps_at_both_ends()
    {
        var steps = new[]
        {
            MenuInputAction.Up, MenuInputAction.Right, MenuInputAction.PageUp, MenuInputAction.Down, MenuInputAction.Left,
            MenuInputAction.PageDown, MenuInputAction.PageDown, MenuInputAction.PageUp, MenuInputAction.PageUp,
            MenuInputAction.PageUp, MenuInputAction.PageUp,
        };
        var seen = new List<int>();
        for (var n = 1; n <= steps.Length; n++)
        {
            var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 20));
            ctrl.Handle(MenuInputAction.Accept);
            foreach (var action in steps.Take(n)) ctrl.Handle(action);
            ctrl.Handle(MenuInputAction.Accept);
            seen.Add(rules.Moves.Single().Item3);
        }

        Assert.Equal(new[] { 2, 3, 8, 7, 6, 1, 1, 6, 11, 16, 20 }, seen);
    }

    [Fact]
    public void The_picker_hints_are_the_verb_and_cancel_only()
    {
        var (ctrl, _) = Make(ContainerMode.Pickpocket, new ContainerStack("x", "Axe", 9));
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Steal"), new MenuHint(MenuInputAction.Back, "Cancel") }, ctrl.Hints);
    }

    [Fact]
    public void Accepting_the_picker_moves_the_chosen_quantity()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 20));
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.PageUp);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { (ContainerSide.Container, "x", 6) }, rules.Moves);
        Assert.False(IsPicking(ctrl));
    }

    [Fact]
    public void A_picked_stack_that_vanishes_is_reported_on_the_next_press()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 9));
        ctrl.Handle(MenuInputAction.Accept);
        rules.Container.Clear();

        Assert.True(ctrl.Handle(MenuInputAction.Up));

        Assert.False(IsPicking(ctrl));
        Assert.Equal(ContainerController.ChangedText, ctrl.Message);
        Assert.Empty(rules.Moves);
    }

    [Fact]
    public void A_picked_stack_that_vanishes_is_reported_on_the_next_tick()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 9));
        ctrl.Handle(MenuInputAction.Accept);
        rules.Container.Clear();
        rules.Revision++;

        ctrl.Tick(0.1);

        Assert.False(IsPicking(ctrl));
        Assert.Equal(ContainerController.ChangedText, ctrl.Message);
    }

    [Fact]
    public void An_emptied_container_hands_focus_to_the_pack_on_the_next_tick()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("a", "Alpha", 1));
        rules.Player.Add(new ContainerStack("p", "Pebble", 2));
        rules.Container.Clear();
        rules.Revision++;

        ctrl.Tick(0.1);

        Assert.Equal(new[] { "Pebble x 2" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void Rows_show_a_short_count_from_ten_thousand_and_hide_blank_ids_and_empty_stacks()
    {
        var (ctrl, _) = Make(
            ContainerMode.Loot,
            new ContainerStack("a", "Alpha", 1),
            new ContainerStack("b", "Bulk", 9999),
            new ContainerStack("c", "Crate", 10000),
            new ContainerStack("d", "Dust", 12345),
            new ContainerStack(" ", "Blank", 3),
            new ContainerStack("e", "Empty", 0),
            new ContainerStack("f", "Negative", -2));

        Assert.Equal(new[] { "Alpha", "Bulk x 9999", "Crate x 10k", "Dust x 12.3k" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void Nothing_on_either_side_says_empty_and_offers_only_close()
    {
        var (ctrl, rules) = Make();

        Assert.Equal(new[] { ("Loot", LineTone.Heading), (ContainerController.EmptyText, LineTone.Muted) }, Lines(ctrl));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Back, "Close") }, ctrl.Hints);
        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Empty(rules.Moves);
    }

    [Fact]
    public void Carrying_exactly_the_limit_is_not_over_encumbered_and_one_more_unit_is()
    {
        var (ctrl, rules) = Make();
        rules.CarryLimit = 3;
        rules.Player.Add(new ContainerStack("p", "Pebble", 2, 1.5));
        Assert.False(ctrl.IsOverEncumbered);
        Assert.DoesNotContain(Lines(ctrl), line => line.Item1 == ContainerController.OverText);

        rules.Player[0] = rules.Player[0] with { Count = 3 };

        Assert.True(ctrl.IsOverEncumbered);
        Assert.Equal((ContainerController.OverText, LineTone.Bad), Lines(ctrl).Last());
    }

    [Fact]
    public void A_negative_count_adds_no_weight()
    {
        var (ctrl, rules) = Make();
        rules.Player.Add(new ContainerStack("p", "Pebble", -4, 2));
        rules.Player.Add(new ContainerStack("q", "Quartz", 1, 0.5));

        Assert.Equal(0.5, ctrl.PlayerWeight);
    }

    [Theory]
    [InlineData(ContainerMode.Steal, "Steal (Owned) | Weight 0 / 2.5")]
    [InlineData(ContainerMode.Pickpocket, "Pickpocket | Weight 0 / 2.5")]
    [InlineData(ContainerMode.Teammate, "Teammate | Weight 0 / 2.5")]
    public void The_header_marks_stolen_goods_and_formats_a_fractional_limit(ContainerMode mode, string expected)
    {
        var (ctrl, rules) = Make(mode);
        rules.CarryLimit = 2.5;

        Assert.Equal(expected, ctrl.Header);
    }

    [Theory]
    [InlineData(150, "100% to steal")]
    [InlineData(-5, "0% to steal")]
    [InlineData(0, "0% to steal")]
    [InlineData(37, "37% to steal")]
    public void A_pickpocket_chance_is_clamped_to_zero_through_one_hundred(int chance, string expected)
    {
        var (ctrl, rules) = Make(ContainerMode.Pickpocket, new ContainerStack("x", "Axe", 1));
        rules.ChanceOf = () => chance;

        Assert.Equal((expected, LineTone.Bad), Lines(ctrl)[4]);
    }

    [Fact]
    public void A_pickpocket_chance_on_the_pack_side_reads_as_placing()
    {
        var (ctrl, rules) = Make(ContainerMode.Pickpocket);
        rules.Player.Add(new ContainerStack("p", "Pebble", 1));
        rules.ChanceOf = () => 60;
        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(("60% to place", LineTone.Bad), Lines(ctrl).Single(line => line.Item1.EndsWith("to place", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_chance_that_throws_hides_the_line_and_records_the_error()
    {
        var (ctrl, rules) = Make(ContainerMode.Pickpocket, new ContainerStack("x", "Axe", 1));
        rules.ChanceOf = () => throw new InvalidOperationException("no odds");

        Assert.Equal(4, ctrl.DetailLines.Count);
    }

    [Fact]
    public void The_chance_line_is_only_for_pickpocketing()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 1));
        rules.ChanceOf = () => 50;

        Assert.DoesNotContain(Lines(ctrl), line => line.Item1.Contains('%'));
    }

    [Fact]
    public void A_null_transfer_result_is_unavailable_and_leaves_the_stack_listed()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 1));
        rules.TransferOf = _ => null;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(ContainerController.UnavailableText, ctrl.Message);
        Assert.Equal(new[] { "Axe" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void A_refused_transfer_with_no_reason_says_it_failed_and_one_with_a_reason_says_why()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 1));
        rules.TransferOf = _ => new ContainerTransferResult(TransferStatus.Refused);

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(ContainerController.FailedText, ctrl.Message);

        rules.TransferOf = _ => new ContainerTransferResult(TransferStatus.Refused, "Jammed.");
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("Jammed.", ctrl.Message);
    }

    [Fact]
    public void A_null_refusal_means_no_refusal_and_a_throwing_one_blocks_the_move()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("x", "Axe", 1));
        rules.Refusals["x"] = null;

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { (ContainerSide.Container, "x", 1) }, rules.Moves);

        rules.RefusalThrows = true;
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Single(rules.Moves);
        Assert.Equal(ContainerController.UnavailableText, ctrl.Message);
    }

    [Fact]
    public void Take_all_skips_refused_stacks_moves_the_rest_and_reports_the_first_reason()
    {
        var (ctrl, rules) = Make(
            ContainerMode.Loot,
            new ContainerStack("a", "Alpha", 2),
            new ContainerStack("b", "Bulk", 3),
            new ContainerStack("c", "Crate", 1));
        rules.Refusals["a"] = "Locked.";
        rules.TransferOf = id => id == "b" ? new ContainerTransferResult(TransferStatus.Refused, "Jammed.") : new ContainerTransferResult(TransferStatus.Done);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(new[] { (ContainerSide.Container, "b", 3), (ContainerSide.Container, "c", 1) }, rules.Moves);
        Assert.Equal("Locked.", ctrl.Message);
    }

    [Fact]
    public void Take_all_with_a_refused_transfer_and_no_reason_says_it_failed()
    {
        var (ctrl, rules) = Make(ContainerMode.Loot, new ContainerStack("a", "Alpha", 2));
        rules.TransferOf = _ => new ContainerTransferResult(TransferStatus.Refused);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(ContainerController.FailedText, ctrl.Message);
    }

    [Fact]
    public void Input_after_closing_is_not_handled()
    {
        var (ctrl, _) = Make(ContainerMode.Loot, new ContainerStack("a", "Alpha", 1));
        Assert.Equal(ContainerOutcome.Open, ctrl.Outcome);
        ctrl.Handle(MenuInputAction.Back);

        Assert.False(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal(ContainerOutcome.Closed, ctrl.Outcome);
    }

    [Fact]
    public void The_controller_needs_its_rules()
    {
        Assert.Throws<ArgumentNullException>(() => new ContainerController(null!));
    }
}
