using System.Linq;
using OpenCommonwealth.Services.Ui.Layout;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SwfLayoutConversionTests
{
    private static readonly SwfTransformRecord Identity = new(1, 0, 0, 1, 0, 0);

    private static readonly MenuSwfRef Source = new("list_menu", "archive.ba2", "Interface/Menu.swf", new Dictionary<string, string>());

    private static SwfLayoutRecord Movie(IReadOnlyList<SwfLayoutFontRecord> fonts, SwfLayoutTextRecord? text) =>
        new(new SwfBoundsRecord(0, 0, 100, 100), 30, true, false, false, fonts,
        [
            new SwfLayoutStateRecord("", 0, 0,
            [
                new SwfLayoutRowRecord("field", "", 1, 7, SwfCharacterKind.EditText, true, true, new SwfBoundsRecord(0, 0, 10, 10),
                    Identity, null, 0, 0, 0, false, false, false, false, text),
            ]),
        ]);

    private static SwfLayoutTextRecord TextWithClass(string? fontClass) =>
        new(4, 18, 0xFF112233, 0, null, null, null, null, false, false, false, false, false, "var", "hello", fontClass);

    [Fact]
    public void TheFontClassOfAnEditTextFlowsIntoItsTextStyle()
    {
        var record = Movie([], TextWithClass("$MainFont"));
        var style = SwfLayoutConversion.ToMenuLayouts(Source, record).Single().Rows.Single().Text;
        Assert.Equal("$MainFont", style!.FontClass);
        Assert.Equal(4, style.FontId);
    }

    [Fact]
    public void AnEditTextWithNoFontClassHasANullOne()
    {
        var record = Movie([], TextWithClass(null));
        Assert.Null(SwfLayoutConversion.ToMenuLayouts(Source, record).Single().Rows.Single().Text!.FontClass);
    }

    [Fact]
    public void EveryFieldOfTheMoviesFontTableFlowsIntoTheLayout()
    {
        var full = new SwfLayoutFontRecord(9, 3, "Futura", true, false, 1, 2, true, 800, 200, 0, 1024, [65, 66], "Futura Display", "(c)", "$MainFont");
        var bare = new SwfLayoutFontRecord(12, 1, "Plain", null, null, null, null, false, null, null, null, null, null, null, null, null);
        var layout = SwfLayoutConversion.ToMenuLayouts(Source, Movie([full, bare], TextWithClass(null))).Single();
        Assert.Equal(2, layout.Fonts.Count);
        var first = layout.Fonts[0];
        Assert.Equal(9, first.FontId);
        Assert.Equal("Futura", first.Name);
        Assert.True(first.Bold);
        Assert.False(first.Italic);
        Assert.Equal("Futura Display", first.FontName);
        Assert.Equal("$MainFont", first.FontClass);
        Assert.Equal(2u, first.GlyphCount);
        Assert.Equal(new ushort[] { 65, 66 }, first.Codes);
        var second = layout.Fonts[1];
        Assert.Equal(12, second.FontId);
        Assert.Equal("Plain", second.Name);
        Assert.Null(second.Bold);
        Assert.Null(second.Italic);
        Assert.Null(second.FontName);
        Assert.Null(second.FontClass);
        Assert.Null(second.GlyphCount);
        Assert.Null(second.Codes);
    }

    [Fact]
    public void AMovieWithoutFontsGivesAnEmptyFontTable()
    {
        Assert.Empty(SwfLayoutConversion.ToMenuLayouts(Source, Movie([], TextWithClass(null))).Single().Fonts);
    }
}
