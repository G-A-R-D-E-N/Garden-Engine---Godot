using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Swf;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ContainerArtTests
{
    private const string M = ContainerArt.M;

    private sealed class Rules : IContainerRules
    {
        public ContainerMode Mode => ContainerMode.Loot;

        public string Title => "Crate";

        public string PlayerListHeader => "Your stuff";

        public double CarryLimit => 50;

        public int Revision => 0;

        public IReadOnlyList<ContainerStack> ContainerStacks { get; } =
            [new("a", "Alpha", 1), new("b", "Bulk", 12, 0.5, 3)];

        public IReadOnlyList<ContainerStack> PlayerStacks { get; } = [new("p", "Pebble", 2, 1.5, 4)];

        public string Refusal(ContainerSide from, string itemId, int count) => string.Empty;

        public int Chance(ContainerSide from, string itemId) => 0;

        public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count) => new(TransferStatus.Done);
    }

    private static ContainerController New() => new(new Rules());

    [Fact]
    public void Both_lists_bind_their_rows_and_only_the_active_side_is_highlighted()
    {
        var c = New();
        var b = ContainerArt.Build(c);

        Assert.Equal([new SwfListRow("Alpha", true), new SwfListRow("Bulk x 12", false)], b.Lists[ContainerArt.ContainerAnchor]);
        Assert.Equal([new SwfListRow("Pebble x 2", false)], b.Lists[ContainerArt.PlayerAnchor]);

        c.Handle(MenuInputAction.Right);
        b = ContainerArt.Build(c);
        Assert.Equal([new SwfListRow("Pebble x 2", true)], b.Lists[ContainerArt.PlayerAnchor]);
        Assert.DoesNotContain(true, b.Lists[ContainerArt.ContainerAnchor].Select(r => r.Selected));
    }

    [Fact]
    public void Headers_weight_and_hidden_clips_follow_the_container_state()
    {
        var b = ContainerArt.Build(New());

        Assert.Equal("Your stuff", b.Text[M + "PlayerInventory_mc/PlayerListHeader/textField"]);
        Assert.Equal("CRATE", b.Text[M + "ContainerInventory_mc/ContainerListHeader/textField"]);
        Assert.Equal("3/50", b.Text[M + "PlayerInventory_mc/PlayerWeight_tf"]);
        Assert.Contains(M + "QuantityMenu_mc", b.Hide);
        Assert.Contains(M + "PickpocketInfo_mc", b.Hide);
        Assert.Contains(M + "PlayerInventory_mc/PlayerCaps_tf", b.Hide);
        Assert.Equal(new HashSet<string> { ContainerArt.ContainerAnchor, ContainerArt.PlayerAnchor }, b.Headers);
    }

    [Fact]
    public void Picking_a_quantity_shows_the_widget_with_the_count_and_a_thumb_offset()
    {
        var c = New();
        c.Handle(MenuInputAction.Down);
        c.Handle(MenuInputAction.Accept);
        c.Handle(MenuInputAction.Right);
        c.Handle(MenuInputAction.Right);
        var b = ContainerArt.Build(c);
        const string Q = M + "QuantityMenu_mc";

        Assert.DoesNotContain(Q, b.Hide);
        Assert.Equal("3", b.Text[Q + "/Value_tf"]);
        Assert.Equal("QUANTITY", b.Text[Q + "/Label_tf"]);
        var thumb = b.Place[Q + "/QuantityScrollbar_mc/Thumb_mc"];
        Assert.Equal(ContainerArt.ThumbDelta(3, 12), thumb.Tx, 6);
        Assert.Empty(b.Sprites);
        Assert.Contains(c.DetailLines, l => l.Text.StartsWith("Quantity 3", StringComparison.Ordinal));
    }

    [Fact]
    public void The_card_lines_are_the_item_name_alone_and_fall_back_to_the_full_detail_without_card_art()
    {
        var c = New();

        Assert.Equal([new MenuTextLine("Alpha", LineTone.Heading)], ContainerArt.CardLines(c));
        var plain = ContainerArt.CardLines(c, false);
        Assert.Equal(3, plain.Count);
        Assert.Equal(new MenuTextLine("Alpha", LineTone.Heading), plain[0]);
        Assert.DoesNotContain(c.DetailLines[0], plain);
        Assert.Empty(ContainerArt.Build(c, null, false).Sprites);
        Assert.NotEmpty(ContainerArt.Build(c).Sprites);
    }

    [Fact]
    public void The_thumb_follows_the_scrollbar_rule_from_zero_to_the_full_count()
    {
        var scale = ContainerArt.ScrollbarScale;
        Assert.Equal((ContainerArt.TrackX - ContainerArt.ThumbDesignX) * scale, ContainerArt.ThumbDelta(0, 8), 6);
        Assert.Equal((ContainerArt.TrackX + ContainerArt.TrackWidth - ContainerArt.ThumbWidth - ContainerArt.ThumbDesignX) * scale, ContainerArt.ThumbDelta(8, 8), 6);
        Assert.Equal((ContainerArt.ThumbDelta(0, 8) + ContainerArt.ThumbDelta(8, 8)) / 2, ContainerArt.ThumbDelta(4, 8), 6);
        Assert.Equal(ContainerArt.ThumbDelta(8, 8), ContainerArt.ThumbDelta(99, 8), 6);
    }

    [Fact]
    public void The_card_entries_carry_value_and_weight_of_the_shown_stack_and_hide_while_picking()
    {
        var c = New();
        c.Handle(MenuInputAction.Down);

        Assert.Equal([new CardEntry("VAL", "3"), new CardEntry("WG", "0.5")], c.CardEntries);
        Assert.Equal(6, ContainerArt.Build(c).Sprites.Count);
        c.Handle(MenuInputAction.Accept);
        Assert.Empty(ContainerArt.Build(c).Sprites);
    }

    [Fact]
    public void Only_the_active_header_keeps_its_arrows_and_both_lists_get_a_bracket()
    {
        var b = ContainerArt.Build(New());

        Assert.DoesNotContain(ContainerArt.ContainerAnchor + "/RightArrow", b.Hide);
        Assert.Contains(ContainerArt.PlayerAnchor + "/LeftArrow", b.Hide);
        Assert.Contains(ContainerArt.PlayerAnchor + "/RightArrow", b.Hide);
        Assert.Equal(new[] { ContainerArt.ContainerAnchor, ContainerArt.PlayerAnchor }, b.Brackets.Select(x => x.Header).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_window_over_nine_rows_scrolls_with_the_selection_and_reports_its_first_row()
    {
        var rows = Enumerable.Range(0, 20).Select(i => new SwfListRow("r" + i, i == 15)).ToList();
        var window = SwfBindings.Window(rows);

        Assert.Equal(7, window.First);
        Assert.Equal("r7", window.Rows[0].Text);
        Assert.True(window.MoreAbove);
        Assert.True(window.MoreBelow);
    }

    [Fact]
    public void Hovering_a_row_inside_the_window_keeps_the_scroll_position()
    {
        var scroll = new SwfScroll();
        List<SwfListRow> Rows(int selected) => Enumerable.Range(0, 20).Select(i => new SwfListRow("r" + i, i == selected)).ToList();

        Assert.Equal(7, SwfScroll.Window(scroll, "a", Rows(15)).First);
        Assert.Equal(7, SwfScroll.Window(scroll, "a", Rows(8)).First);
        Assert.Equal(7, scroll.First("a"));
        Assert.Equal(3, SwfScroll.Window(scroll, "a", Rows(3)).First);
        Assert.Equal(0, SwfBindings.Window(Rows(8)).First);
    }

    [Fact]
    public void The_stateless_window_is_unchanged_for_a_short_list_and_no_selection()
    {
        var rows = Enumerable.Range(0, 4).Select(i => new SwfListRow("r" + i, false)).ToList();

        Assert.Equal(0, SwfBindings.Window(rows, 9, 5).First);
    }

    private static void Collect(SwfNode node, HashSet<string> into)
    {
        into.Add(node.Path);
        foreach (var child in node.Children)
            Collect(child, into);
    }

    [Fact]
    public void Every_bound_path_exists_in_the_real_container_movie()
    {
        if (Fo4Data.Read(ContainerArt.Movie) is not { } movieBytes)
            return;
        using var stage = SwfStage.Read(movieBytes);
        var paths = new HashSet<string>();
        Collect(stage.Root, paths);
        var c = New();
        c.Handle(MenuInputAction.Down);
        c.Handle(MenuInputAction.Accept);
        foreach (var b in new[] { ContainerArt.Build(New()), ContainerArt.Build(c) })
        {
            var bound = b.Hide.Concat(b.Show).Concat(b.Text.Keys).Concat(b.Place.Keys).Concat(b.Lists.Keys)
                .Concat(b.Headers.SelectMany(h => new[] { h, h + "/textField", h + "/RightArrow" }));
            foreach (var bind in bound)
                Assert.Contains(bind, paths);
        }
    }

    private static SwfNode? Find(SwfNode node, string path) =>
        node.Path == path ? node : node.Children.Select(c => Find(c, path)).FirstOrDefault(n => n is not null);

    private static IEnumerable<SwfNode> Leaves(SwfNode node) =>
        node.Children.Count == 0 ? [node] : node.Children.SelectMany(Leaves);

    private static SwfStage? Open(SwfReadOptions options) =>
        Fo4Data.Read(ContainerArt.Movie) is { } movieBytes ? SwfStage.Read(movieBytes, options) : null;

    [Fact]
    public void The_settled_read_makes_the_bracket_art_visible_and_the_plain_read_does_not()
    {
        const string Panel = M + "PlayerInventory_mc/PlayerBracketBackground_mc";
        using (var settled = Open(ContainerArt.Read))
        {
            if (settled is null)
                return;
            Assert.All(Leaves(Find(settled.Root, Panel)!), n => Assert.False(n.Effects.HasTint && n.Effects.Tint.A <= 0));
            var thumb = Find(settled.Root, M + "QuantityMenu_mc/QuantityScrollbar_mc/Thumb_mc")!;
            Assert.All(Leaves(thumb), n => Assert.False(n.Effects.HasTint && n.Effects.Tint.A <= 0));
        }
        using var plain = Open(SwfReadOptions.None);
        Assert.Contains(Leaves(Find(plain!.Root, Panel)!), n => n.Effects.HasTint && n.Effects.Tint.A <= 0);
    }

    [Fact]
    public void The_bracket_edges_and_scrollbar_numbers_match_the_real_movie()
    {
        using var stage = Open(ContainerArt.Read);
        if (stage is null)
            return;
        double Right(string panel) => Find(stage.Root, panel)!.Local.Tx + Leaves(Find(stage.Root, panel)!).First().Bounds!.Value.W;
        Assert.Equal(ContainerArt.PlayerRight, Right(M + "PlayerInventory_mc/PlayerBracketBackground_mc"), 1);
        Assert.Equal(ContainerArt.ContainerRight, Right(M + "ContainerInventory_mc/ContainerBracketBackground_mc"), 1);
        Assert.Equal(ContainerArt.HintTop, Find(stage.Root, M + "ButtonHintBar_mc")!.Local.Ty, 1);
        var card = Find(stage.Root, M + "ItemCard_mc")!.Local;
        Assert.Equal((1.0, 1.0), (card.A, card.D));
        Assert.Equal((ItemCardArt.OriginX, ItemCardArt.OriginY), (card.Tx, card.Ty));
        foreach (var panel in new[] { M + "PlayerInventory_mc/PlayerBracketBackground_mc", M + "ContainerInventory_mc/ContainerBracketBackground_mc" })
        {
            var node = Find(stage.Root, panel)!;
            Assert.Equal(ContainerArt.Bottom, node.Local.Ty + Leaves(node).First().Bounds!.Value.H, 0.05);
        }
        var bar = M + "QuantityMenu_mc/QuantityScrollbar_mc";
        Assert.Equal(ContainerArt.ScrollbarScale, Find(stage.Root, bar)!.Local.A, 2);
        Assert.Equal(ContainerArt.TrackX, Find(stage.Root, bar + "/Track_mc")!.Local.Tx, 2);
        Assert.Equal(ContainerArt.ThumbDesignX, Find(stage.Root, bar + "/Thumb_mc")!.Local.Tx, 2);
    }
}
