using OpenCommonwealth.Services.Ui.Layout;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class TextPresentationTests
{
    private static readonly LayoutRect Box = new(0, 0, 100, 20);

    private static TextStyle Style(
        int? fontId = null,
        double size = 20,
        uint rgba = 0xFFFFFFFF,
        LayoutTextAlign align = LayoutTextAlign.Left,
        bool multiline = false,
        bool wordWrap = false,
        string? fontClass = null) =>
        new(fontId, size, rgba, align, multiline, wordWrap, false, "", fontClass);

    private static LayoutRow TextRow(TextStyle? style) =>
        new("title", LayoutRowKind.Text, 0, true, Box, style);

    private static LayoutFont Font(int id, string? fontName = null, bool? bold = null, bool? italic = null, string? fontClass = null) =>
        new(id, $"font{id}", bold, italic, fontName, fontClass, null, null);

    private static MenuLayout Layout(params LayoutFont[] fonts) =>
        new("message_box", "default", 1280, 720, fonts, []);

    private static TextPresentation Resolve(MenuLayout layout, TextStyle style, double scale = 1)
    {
        var resolved = TextPresentations.Resolve(layout, TextRow(style), scale);
        Assert.NotNull(resolved);
        return resolved;
    }

    [Fact]
    public void ARowWithoutATextStyleHasNoPresentation()
    {
        Assert.Null(TextPresentations.Resolve(Layout(), TextRow(null), 1));
    }

    [Fact]
    public void SizeScalesWithTheStageScale()
    {
        Assert.Equal(30.0, Resolve(Layout(), Style(size: 20), 1.5).Size, 6);
        Assert.Equal(10.0, Resolve(Layout(), Style(size: 20), 0.5).Size, 6);
    }

    [Fact]
    public void SizeNeverDropsBelowOne()
    {
        Assert.Equal(1.0, Resolve(Layout(), Style(size: 0), 1.5).Size, 6);
        Assert.Equal(1.0, Resolve(Layout(), Style(size: 20), 0).Size, 6);
        Assert.Equal(1.0, Resolve(Layout(), Style(size: 4), 0.1).Size, 6);
    }

    [Fact]
    public void ANonFiniteScaleFallsBackToSizeOne()
    {
        Assert.Equal(1.0, Resolve(Layout(), Style(size: 20), double.NaN).Size, 6);
        Assert.Equal(1.0, Resolve(Layout(), Style(size: 20), double.PositiveInfinity).Size, 6);
    }

    [Fact]
    public void TheFontIsLookedUpById()
    {
        var layout = Layout(Font(1, "Other", true, true), Font(7, "Futura", true, false));
        var text = Resolve(layout, Style(fontId: 7));
        Assert.Equal("Futura", text.FontName);
        Assert.True(text.Bold);
        Assert.False(text.Italic);
    }

    [Fact]
    public void TheFirstFontWithTheIdWins()
    {
        var layout = Layout(Font(7, "First"), Font(7, "Second"));
        Assert.Equal("First", Resolve(layout, Style(fontId: 7)).FontName);
    }

    [Fact]
    public void ItalicComesFromTheMatchedFont()
    {
        var layout = Layout(Font(7, "Futura", false, true));
        var text = Resolve(layout, Style(fontId: 7));
        Assert.True(text.Italic);
        Assert.False(text.Bold);
    }

    [Fact]
    public void TheFontIdBeatsTheFontClass()
    {
        var layout = Layout(Font(1, "ByClass", fontClass: "$MAIN"), Font(7, "ById"));
        Assert.Equal("ById", Resolve(layout, Style(fontId: 7, fontClass: "$MAIN")).FontName);
    }

    [Fact]
    public void TheFontFallsBackToTheFontClassWhenTheIdIsUnknown()
    {
        var layout = Layout(Font(1, "Other"), Font(2, "Classy", true, null, "$MAIN"));
        var text = Resolve(layout, Style(fontId: 99, fontClass: "$MAIN"));
        Assert.Equal("Classy", text.FontName);
        Assert.True(text.Bold);
    }

    [Fact]
    public void TheFontClassIsUsedWhenTheStyleHasNoFontId()
    {
        var layout = Layout(Font(2, "Classy", fontClass: "$MAIN"));
        Assert.Equal("Classy", Resolve(layout, Style(fontClass: "$MAIN")).FontName);
    }

    [Fact]
    public void AnEmptyFontClassMatchesNothing()
    {
        var layout = Layout(Font(2, "Blank", fontClass: ""));
        Assert.Null(Resolve(layout, Style(fontId: 99, fontClass: "")).FontName);
    }

    [Fact]
    public void AnUnknownFontKeepsTheDefaults()
    {
        var layout = Layout(Font(1, "Other", true, true, "$OTHER"));
        var text = Resolve(layout, Style(fontId: 99, fontClass: "$MAIN"));
        Assert.Null(text.FontName);
        Assert.False(text.Bold);
        Assert.False(text.Italic);
    }

    [Fact]
    public void AFontWithoutBoldOrItalicFlagsIsNeitherBoldNorItalic()
    {
        var text = Resolve(Layout(Font(7, "Futura")), Style(fontId: 7));
        Assert.False(text.Bold);
        Assert.False(text.Italic);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void WrapNeedsBothMultilineAndWordWrap(bool multiline, bool wordWrap, bool expected)
    {
        Assert.Equal(expected, Resolve(Layout(), Style(multiline: multiline, wordWrap: wordWrap)).Wrap);
    }

    [Theory]
    [InlineData(LayoutTextAlign.Left)]
    [InlineData(LayoutTextAlign.Center)]
    [InlineData(LayoutTextAlign.Right)]
    [InlineData(LayoutTextAlign.Justify)]
    public void AlignPassesThrough(LayoutTextAlign align)
    {
        Assert.Equal(align, Resolve(Layout(), Style(align: align)).Align);
    }

    [Fact]
    public void RgbaPassesThroughUnchanged()
    {
        Assert.Equal(0x12345678u, Resolve(Layout(), Style(rgba: 0x12345678)).Rgba);
    }
}
