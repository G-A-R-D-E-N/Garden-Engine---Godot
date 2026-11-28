using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Swf;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ItemCardArtTests
{
    [Fact]
    public void Entries_stack_upward_by_the_card_height_with_the_entry_spacing()
    {
        var b = new SwfBindings();

        ItemCardArt.Add(b, [new CardEntry("VAL", "10"), new CardEntry("WG", "0.5")]);

        Assert.Equal(6, b.Sprites.Count);
        Assert.All(b.Sprites, s => Assert.Equal(ItemCardArt.Clip, s.Parent));
        var rows = b.Sprites.Chunk(3).ToArray();
        Assert.Equal(-23.38, rows[0][0].Y, 6);
        Assert.Equal(-44.31, rows[1][0].Y, 6);
        Assert.All(b.Sprites, s => Assert.Equal(0.7, s.Scale));
        Assert.Equal(554.21, ItemCardArt.NotesX, 2);
        Assert.Equal(212.79, ItemCardArt.NotesWidth, 2);
        Assert.Equal(ItemCardArt.Entry, rows[0][0].Template);
        Assert.Equal(("VAL", SwfAlign.Left), (rows[0][1].Label!.Text, rows[0][1].Label!.Align));
        Assert.Equal(("0.5", SwfAlign.Right), (rows[1][2].Label!.Text, rows[1][2].Label!.Align));
        Assert.Equal([new SwfTemplate(ItemCardArt.Entry, ItemCardArt.Symbol, null, 0)], ItemCardArt.Templates());
    }

    [Fact]
    public void No_entries_add_no_sprites()
    {
        var b = new SwfBindings();

        ItemCardArt.Add(b, []);

        Assert.Empty(b.Sprites);
    }

    [Fact]
    public void The_barter_card_carries_the_selected_row_price_and_draws_three_sprites()
    {
        var rules = new BarterControllerTests.FakeRules();
        var ctrl = new BarterController(rules);

        Assert.Equal([new CardEntry("VAL", "10")], ctrl.CardEntries);
        Assert.Equal(3, BarterArt.Build(ctrl, rules).Sprites.Count);
        ctrl.Handle(MenuInputAction.Right);
        Assert.Equal("VAL", Assert.Single(ctrl.CardEntries).Label);
    }
}
