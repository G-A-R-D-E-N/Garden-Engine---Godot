using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ContainerControllerTests
{
    sealed class FakeRules : IContainerRules
    {
        public ContainerMode Mode { get; set; } = ContainerMode.Loot;

        public string Title { get; set; } = "Crate";

        public string PlayerListHeader { get; set; } = "Pack of Pebble";

        public double CarryLimit { get; set; } = 50;

        public int Revision { get; set; }

        public bool Throw { get; set; }

        public List<ContainerStack> Container { get; } = new()
        {
            new ContainerStack("a", "Alpha", 1),
            new ContainerStack("b", "Bulk", 12),
            new ContainerStack("w", "Whole", 30, 0, 0, true),
        };

        public List<ContainerStack> Player { get; } = new() { new ContainerStack("p", "Pebble", 2, 1.5, 4) };

        public Dictionary<string, string> Refusals { get; } = new();

        public HashSet<string> Watched { get; } = new();

        public List<(ContainerSide From, string Id, int Count)> Moves { get; } = new();

        public IReadOnlyList<ContainerStack> ContainerStacks => Container;

        public IReadOnlyList<ContainerStack> PlayerStacks => Player;

        public string Refusal(ContainerSide from, string itemId, int count) =>
            from == ContainerSide.Container && Refusals.TryGetValue(itemId, out var text) ? text : string.Empty;

        public int Chance(ContainerSide from, string itemId) => from == ContainerSide.Container ? 35 : 80;

        public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Moves.Add((from, itemId, count));
            if (Watched.Contains(itemId)) return new ContainerTransferResult(TransferStatus.Caught);
            var source = from == ContainerSide.Container ? Container : Player;
            var target = from == ContainerSide.Container ? Player : Container;
            var index = source.FindIndex(stack => stack.Id == itemId);
            var stack = source[index];
            if (stack.Count == count) source.RemoveAt(index);
            else source[index] = stack with { Count = stack.Count - count };
            var existing = target.FindIndex(candidate => candidate.Id == itemId);
            if (existing < 0) target.Add(stack with { Count = count });
            else target[existing] = target[existing] with { Count = target[existing].Count + count };
            Revision++;
            return new ContainerTransferResult(TransferStatus.Done);
        }
    }

    static (ContainerController Ctrl, FakeRules Rules) Make(ContainerMode mode = ContainerMode.Loot)
    {
        var rules = new FakeRules { Mode = mode };
        return (new ContainerController(rules), rules);
    }

    static string[] Rows(ContainerController ctrl) => WorkshopTestHelpers.RowTexts(ctrl.List);

    static ContainerSide SideOf(ContainerController ctrl) => ctrl.Rows.Selected!.Side;

    static bool Picking(ContainerController ctrl) => ctrl.Hints.Any(hint => hint.Label == "Cancel");

    static int QuantityOf(ContainerController ctrl) =>
        ctrl.DetailLines.Select(line => System.Text.RegularExpressions.Regex.Match(line.Text, @"^Quantity (\d+) of \d+$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).SingleOrDefault();

    [Fact]
    public void The_container_side_lists_stacks_with_counts_and_a_header_with_weight_and_limit()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { "Alpha", "Bulk x 12", "Whole x 30" }, Rows(ctrl));
        Assert.Equal("Crate", ctrl.Title);
        Assert.Equal("Loot | Weight 3 / 50", ctrl.Header);
        Assert.Equal("Take", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
        Assert.Equal("Take all", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Favourite).Label);
    }

    [Fact]
    public void Switching_sides_shows_the_pack_with_a_store_label_and_no_take_all()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(ContainerSide.Player, SideOf(ctrl));
        Assert.Equal(new[] { "Pebble x 2" }, Rows(ctrl));
        Assert.Equal("Store", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
        Assert.DoesNotContain(ctrl.Hints, hint => hint.Action == MenuInputAction.Favourite);

        ctrl.Handle(MenuInputAction.Left);

        Assert.Equal(ContainerSide.Container, SideOf(ctrl));
    }

    [Theory]
    [InlineData("Mine-HDR")]
    [InlineData("")]
    public void The_pack_side_shows_the_rules_heading_in_the_detail_and_the_summary(string heading)
    {
        var ctrl = new ContainerController(new FakeRules { PlayerListHeader = heading });
        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(ContainerSide.Player, SideOf(ctrl));
        Assert.Equal((heading, heading), (ctrl.DetailLines[0].Text, ctrl.Rows.Summary));
    }

    [Fact]
    public void A_single_item_moves_straight_across()
    {
        var (ctrl, rules) = Make();

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal((ContainerSide.Container, "a", 1), Assert.Single(rules.Moves));
        Assert.Equal(new[] { "Bulk x 12", "Whole x 30" }, Rows(ctrl));
        Assert.Contains(rules.Player, stack => stack.Id == "a");
    }

    [Fact]
    public void A_big_stack_opens_the_quantity_picker_and_confirm_moves_that_many()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(Picking(ctrl));
        Assert.Equal(1, QuantityOf(ctrl));
        Assert.Empty(rules.Moves);

        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.PageUp);
        Assert.Equal(8, QuantityOf(ctrl));
        Assert.Contains(ctrl.DetailLines, line => line.Text == "Quantity 8 of 12");

        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(Picking(ctrl));
        Assert.Equal((ContainerSide.Container, "b", 8), Assert.Single(rules.Moves));
        Assert.Equal(new[] { "Alpha", "Bulk x 4", "Whole x 30" }, Rows(ctrl));
    }

    [Fact]
    public void The_picker_card_follows_the_picked_stack_even_if_the_list_cursor_moves()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Rows.Select(0);

        var texts = ctrl.DetailLines.Select(line => line.Text).ToArray();
        Assert.Contains("Bulk", texts);
        Assert.Contains("Quantity 1 of 12", texts);
        Assert.DoesNotContain("Alpha", texts);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal((ContainerSide.Container, "b", 1), Assert.Single(rules.Moves));
    }

    [Fact]
    public void The_picker_stops_at_one_and_at_what_the_stack_holds()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.PageDown);
        Assert.Equal(1, QuantityOf(ctrl));

        ctrl.Handle(MenuInputAction.PageUp);
        ctrl.Handle(MenuInputAction.PageUp);
        ctrl.Handle(MenuInputAction.PageUp);
        Assert.Equal(12, QuantityOf(ctrl));
    }

    [Fact]
    public void Back_in_the_picker_cancels_without_moving_anything()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);

        Assert.False(Picking(ctrl));
        Assert.False(ctrl.IsFinished);
        Assert.Empty(rules.Moves);
    }

    [Fact]
    public void A_move_whole_stack_item_skips_the_picker_and_moves_everything()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.False(Picking(ctrl));
        Assert.Equal((ContainerSide.Container, "w", 30), Assert.Single(rules.Moves));
    }

    [Fact]
    public void Take_all_moves_everything_and_reports_the_first_refusal()
    {
        var (ctrl, rules) = Make();
        rules.Refusals["b"] = "Too heavy.";

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(new[] { "a", "w" }, rules.Moves.Select(move => move.Id).ToArray());
        Assert.Equal(new[] { "Bulk x 12" }, Rows(ctrl));
        Assert.Equal("Too heavy.", ctrl.Message);
    }

    [Fact]
    public void Take_all_raises_TookAll_once_only_when_something_moved_and_nothing_was_refused()
    {
        var (ctrl, rules) = Make();
        var raised = 0;
        ctrl.TookAll += () => raised++;
        rules.Refusals["b"] = "Too heavy.";

        ctrl.Handle(MenuInputAction.Favourite);
        Assert.Equal(0, raised);

        rules.Refusals.Clear();
        ctrl.Handle(MenuInputAction.Favourite);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Take_all_hands_focus_to_the_pack_when_the_container_empties()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(ContainerSide.Player, SideOf(ctrl));
        Assert.Contains("Pebble x 2", Rows(ctrl));
    }

    [Fact]
    public void Take_all_does_nothing_on_the_pack_side()
    {
        var (ctrl, rules) = Make();
        ctrl.Handle(MenuInputAction.Right);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Empty(rules.Moves);
    }

    [Fact]
    public void Being_caught_ends_the_menu_with_the_caught_outcome()
    {
        var (ctrl, rules) = Make(ContainerMode.Pickpocket);
        rules.Watched.Add("a");
        var results = new List<bool>();
        ctrl.Finished += results.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.IsFinished);
        Assert.Equal(ContainerOutcome.Caught, ctrl.Outcome);
        Assert.Equal(new[] { false }, results);
        Assert.False(ctrl.Handle(MenuInputAction.Down));
    }

    [Fact]
    public void Being_caught_during_take_all_stops_the_rest()
    {
        var (ctrl, rules) = Make(ContainerMode.Steal);
        rules.Watched.Add("b");

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(ContainerOutcome.Caught, ctrl.Outcome);
        Assert.Contains(rules.Container, stack => stack.Id == "w");
        Assert.Equal(new[] { "a", "b" }, rules.Moves.Select(move => move.Id).ToArray());
    }

    [Fact]
    public void Back_closes_the_menu_with_the_closed_outcome()
    {
        var (ctrl, _) = Make();

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.Equal(ContainerOutcome.Closed, ctrl.Outcome);
    }

    [Fact]
    public void A_steal_container_shows_the_owned_header_the_theft_warning_and_the_steal_words()
    {
        var (ctrl, _) = Make(ContainerMode.Steal);

        Assert.Equal("Steal (Owned) | Weight 3 / 50", ctrl.Header);
        Assert.Contains(ctrl.DetailLines, line => line.Text == ContainerController.TheftText && line.Tone == LineTone.Bad);
        Assert.Equal("Steal", ctrl.Verb);
        Assert.Equal("Steal all", ctrl.TakeAllLabel);
        Assert.Equal("Steal all", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Favourite).Label);
    }

    [Fact]
    public void A_loot_container_shows_no_theft_warning()
    {
        var (ctrl, _) = Make();

        Assert.DoesNotContain(ctrl.DetailLines, line => line.Text == ContainerController.TheftText);
    }

    [Fact]
    public void Pickpocket_shows_the_chance_for_the_selected_item_on_each_side()
    {
        var (ctrl, _) = Make(ContainerMode.Pickpocket);

        Assert.Contains(ctrl.DetailLines, line => line.Text == "35% to steal" && line.Tone == LineTone.Bad);

        ctrl.Handle(MenuInputAction.Right);

        Assert.Contains(ctrl.DetailLines, line => line.Text == "80% to place");
    }

    [Fact]
    public void Other_modes_show_no_chance_line()
    {
        var (ctrl, _) = Make();

        Assert.DoesNotContain(ctrl.DetailLines, line => line.Text.Contains("% to "));
    }

    [Fact]
    public void Carrying_more_than_the_limit_shows_over_encumbered()
    {
        var (ctrl, rules) = Make();
        rules.CarryLimit = 2;

        Assert.True(ctrl.IsOverEncumbered);
        Assert.Contains(ctrl.DetailLines, line => line.Text == ContainerController.OverText && line.Tone == LineTone.Bad);
    }

    [Fact]
    public void A_limit_of_zero_means_no_limit()
    {
        var (ctrl, rules) = Make();
        rules.CarryLimit = 0;

        Assert.False(ctrl.IsOverEncumbered);
        Assert.Equal("Loot | Weight 3", ctrl.Header);
    }

    [Fact]
    public void A_rules_exception_becomes_a_refusal_and_leaves_the_rows_alone()
    {
        var (ctrl, rules) = Make();
        rules.Throw = true;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(ContainerController.UnavailableText, ctrl.Message);
        Assert.Equal(new[] { "Alpha", "Bulk x 12", "Whole x 30" }, Rows(ctrl));
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_revision_change_refreshes_the_rows_on_the_next_tick()
    {
        var (ctrl, rules) = Make();
        rules.Container.RemoveAt(0);
        rules.Revision++;

        ctrl.Tick(0.1);

        Assert.Equal(new[] { "Bulk x 12", "Whole x 30" }, Rows(ctrl));
    }

    [Fact]
    public void Ordering_a_stack_that_has_vanished_says_the_inventory_changed()
    {
        var (ctrl, rules) = Make();
        rules.Container.RemoveAt(0);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(ContainerController.ChangedText, ctrl.Message);
        Assert.Empty(rules.Moves);
        Assert.Equal(new[] { "Bulk x 12", "Whole x 30" }, Rows(ctrl));
    }

    [Fact]
    public void The_mode_words_cover_the_reload_and_power_armour_modes()
    {
        var (reload, _) = Make(ContainerMode.Reload);

        Assert.Equal("Unload", reload.Verb);
        Assert.Equal("Unload all", reload.TakeAllLabel);
        Assert.Equal("Power armor", ContainerController.ModeWord(ContainerMode.PowerArmor));
    }

    [Theory]
    [InlineData(ContainerMode.Loot, "Loot")]
    [InlineData(ContainerMode.Steal, "Steal")]
    [InlineData(ContainerMode.Pickpocket, "Pickpocket")]
    [InlineData(ContainerMode.Teammate, "Teammate")]
    [InlineData(ContainerMode.PowerArmor, "Power armor")]
    [InlineData(ContainerMode.Reload, "Reload")]
    [InlineData(ContainerMode.Workbench, "Workbench")]
    public void ModeWord_names_every_container_mode(ContainerMode mode, string expected)
    {
        Assert.Equal(expected, ContainerController.ModeWord(mode));
    }
}
