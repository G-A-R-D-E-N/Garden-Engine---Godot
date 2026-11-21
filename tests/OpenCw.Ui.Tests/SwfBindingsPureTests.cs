using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SwfBindingsPureTests
{
    private static List<SwfListRow> Rows(int count, int selected) =>
        Enumerable.Range(0, count).Select(i => new SwfListRow("r" + i, i == selected)).ToList();

    private static string[] Texts(SwfListWindow w) => w.Rows.Select(r => r.Text).ToArray();

    [Theory]
    [InlineData(19, 90, 221, 119)]
    [InlineData(19, 0, 221, 29)]
    [InlineData(19, 221, 221, 250)]
    [InlineData(19, 500, 221, 250)]
    [InlineData(19, -5, 221, 29)]
    [InlineData(19, 50, 0, 29)]
    [InlineData(19, 50, -3, 29)]
    [InlineData(0, 12.5, 100, 22.5)]
    public void HeaderArrowXIsTheTextEdgeClampedToTheFieldPlusTheGap(double textX, double textWidth, double fieldWidth, double expected) =>
        Assert.Equal(expected, SwfBindings.HeaderArrowX(textX, textWidth, fieldWidth));

    [Theory]
    [InlineData(7, 0, "7")]
    [InlineData(7, 1, "7")]
    [InlineData(7, 3, "007")]
    [InlineData(0, 2, "00")]
    [InlineData(1234, 3, "1234")]
    [InlineData(-5, 3, "-005")]
    [InlineData(5, -1, "5")]
    public void NumberPadsToTheRequestedDigits(int n, int digits, string expected) =>
        Assert.Equal(expected, SwfBindings.Number(n, digits));

    [Fact]
    public void NumberWithoutDigitsIsUnpadded() =>
        Assert.Equal("42", SwfBindings.Number(42));

    [Theory]
    [InlineData("$", "")]
    [InlineData("$___", "")]
    [InlineData("$A__B \t C ", "A B C")]
    [InlineData("A_B", "A_B")]
    [InlineData(" $X", " $X")]
    [InlineData("", "")]
    [InlineData("$$X", "$X")]
    public void CleanOnlyTouchesDollarPrefixedStrings(string input, string expected) =>
        Assert.Equal(expected, SwfBindings.Clean(input));

    [Fact]
    public void WindowWithNoSelectionStartsAtTheTop()
    {
        var w = SwfBindings.Window(Rows(20, -1));

        Assert.Equal(new[] { "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8" }, Texts(w));
        Assert.False(w.MoreAbove);
        Assert.True(w.MoreBelow);
    }

    [Fact]
    public void WindowKeepsTheSelectionOnTheLastVisibleRowOnceItScrolls()
    {
        var atEdge = SwfBindings.Window(Rows(20, 8));
        Assert.Equal("r0", atEdge.Rows[0].Text);
        Assert.False(atEdge.MoreAbove);

        var pastEdge = SwfBindings.Window(Rows(20, 9));
        Assert.Equal(new[] { "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9" }, Texts(pastEdge));
        Assert.True(pastEdge.MoreAbove);
        Assert.True(pastEdge.MoreBelow);

        var middle = SwfBindings.Window(Rows(20, 12));
        Assert.Equal("r4", middle.Rows[0].Text);
        Assert.Equal("r12", middle.Rows[^1].Text);
        Assert.True(middle.Rows[^1].Selected);
    }

    [Fact]
    public void WindowOfFewerRowsThanVisibleShowsAllOfThem()
    {
        var w = SwfBindings.Window(Rows(3, 2));

        Assert.Equal(new[] { "r0", "r1", "r2" }, Texts(w));
        Assert.False(w.MoreAbove);
        Assert.False(w.MoreBelow);
    }

    [Fact]
    public void WindowOfNoRowsIsEmpty()
    {
        var w = SwfBindings.Window(new List<SwfListRow>());

        Assert.Empty(w.Rows);
        Assert.False(w.MoreAbove);
        Assert.False(w.MoreBelow);
    }

    [Fact]
    public void WindowHonoursACustomVisibleCount()
    {
        var mid = SwfBindings.Window(Rows(10, 5), 3);
        Assert.Equal(new[] { "r3", "r4", "r5" }, Texts(mid));
        Assert.True(mid.MoreAbove);
        Assert.True(mid.MoreBelow);

        var last = SwfBindings.Window(Rows(10, 9), 3);
        Assert.Equal(new[] { "r7", "r8", "r9" }, Texts(last));
        Assert.True(last.MoreAbove);
        Assert.False(last.MoreBelow);
    }

    [Fact]
    public void WindowFollowsTheFirstSelectedRowWhenSeveralAreMarked()
    {
        var rows = Rows(10, 1);
        rows[8] = rows[8] with { Selected = true };

        var w = SwfBindings.Window(rows, 3);

        Assert.Equal(new[] { "r0", "r1", "r2" }, Texts(w));
        Assert.False(w.MoreAbove);
        Assert.True(w.MoreBelow);
    }

    [Fact]
    public void WindowDoesNotChangeTheSourceRows()
    {
        var rows = Rows(20, 15);

        SwfBindings.Window(rows);

        Assert.Equal(20, rows.Count);
        Assert.Equal("r0", rows[0].Text);
    }

    [Fact]
    public void BindingsStartEmptyWithTheDocumentedConstants()
    {
        var b = new SwfBindings();

        Assert.Empty(b.Hide);
        Assert.Empty(b.Headers);
        Assert.Empty(b.Text);
        Assert.Empty(b.FillX);
        Assert.Empty(b.Lists);
        Assert.Equal(9, SwfBindings.ListRows);
        Assert.Equal(10, SwfBindings.HeaderArrowGap);
        Assert.Equal("/textField", SwfBindings.HeaderText);
        Assert.Equal("/RightArrow", SwfBindings.HeaderRightArrow);
    }

    [Fact]
    public void NodeKeepsItsConstructorValuesAndStartsVisibleWithNoEffects()
    {
        var local = new SwfMatrix(1, 0, 0, 1, 5, 6);
        var node = new SwfNode("a/b", "b", 3, 42, true, null, local);

        Assert.Equal("a/b", node.Path);
        Assert.Equal("b", node.Name);
        Assert.Equal(3, node.Depth);
        Assert.Equal(42, node.CharacterId);
        Assert.True(node.IsSprite);
        Assert.False(node.IsMask);
        Assert.Null(node.ClipDepth);
        Assert.Equal(local, node.Local);
        Assert.True(node.Visible);
        Assert.Null(node.Bounds);
        Assert.Null(node.Text);
        Assert.Empty(node.Children);
        Assert.Same(SwfEffects.None, node.Effects);
    }

    private static SwfNode At(int depth, ushort? clipDepth = null) =>
        new("p" + depth, "", depth, 1, false, clipDepth, new SwfMatrix(1, 0, 0, 1, 0, 0));

    [Fact]
    public void AMaskClipsOnlyTheSiblingsAboveItUpToAndIncludingItsClipDepth()
    {
        var mask = At(2, 4);
        var siblings = new[] { At(1), mask, At(3), At(4), At(5) };

        Assert.Equal(new[] { 3, 4 }, SwfNode.MaskedBy(mask, siblings).Select(n => n.Depth));
        Assert.True(mask.IsMask);
        Assert.Empty(SwfNode.MaskedBy(At(3), siblings));
    }

    private static SwfLayoutTextRecord TextOf(string text, bool html) =>
        new(null, 20, null, null, null, null, null, null, false, false, false, html, false, "", text, null);

    [Fact]
    public void PlainTextOfNonHtmlKeepsTagsAndDecodesEntities() =>
        Assert.Equal("<b>x</b> &lt;", SwfStage.PlainText(TextOf("<b>x</b> &amp;lt;", false)));

    [Fact]
    public void PlainTextOfHtmlStripsTagsBeforeDecodingEntities()
    {
        Assert.Equal("<b> \"q\" a&b", SwfStage.PlainText(TextOf("<i>&lt;b&gt;</i> &quot;q&quot; a&amp;b", true)));
        Assert.Equal("", SwfStage.PlainText(TextOf("", true)));
    }
}
