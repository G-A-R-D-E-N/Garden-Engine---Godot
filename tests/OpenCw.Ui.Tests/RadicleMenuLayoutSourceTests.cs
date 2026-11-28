using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Ui.Layout;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RadicleMenuLayoutSourceTests
{
    private static readonly SwfTransformRecord Identity = new(1, 0, 0, 1, 0, 0);
    private static readonly IReadOnlyDictionary<string, string> NoAliases = new Dictionary<string, string>();

    private static MenuSwfRef Ref(string menu = "list_menu", IReadOnlyDictionary<string, string>? rows = null) =>
        new(menu, "archive.ba2", "Interface/Menu.swf", rows ?? NoAliases);

    private static SwfLayoutRowRecord Row(
        string path,
        SwfCharacterKind kind = SwfCharacterKind.Shape,
        double x = 0,
        double y = 0,
        double width = 10,
        double height = 10,
        bool hasRect = true,
        bool visible = true,
        ushort depth = 3,
        string parent = "",
        SwfLayoutTextRecord? text = null) =>
        new(path, parent, depth, 7, kind, visible, hasRect, new SwfBoundsRecord(x, y, width, height), Identity, null, 0, 0, 0, false, false, false, false, text);

    private static SwfLayoutTextRecord Text(
        ushort? font = 4,
        double? height = 18,
        uint? color = 0x11223344,
        byte? align = 2,
        bool html = false,
        string text = "hello",
        string variable = "var",
        string? fontClass = null) =>
        new(font, height, color, align, null, null, null, null, true, true, false, html, false, variable, text, fontClass);

    private static SwfLayoutRecord Movie(double stageX, double stageY, double width, double height, params SwfLayoutStateRecord[] states) =>
        MovieWithFonts([], stageX, stageY, width, height, states);

    private static SwfLayoutRecord MovieWithFonts(IReadOnlyList<SwfLayoutFontRecord> fonts, double stageX, double stageY, double width, double height, params SwfLayoutStateRecord[] states) =>
        new(new SwfBoundsRecord(stageX, stageY, width, height), 30, true, false, false, fonts, states);

    private static SwfLayoutStateRecord State(string label, uint frame, params SwfLayoutRowRecord[] rows) =>
        new(label, 0, frame, rows);

    private static SwfLayoutStateRecord SpriteState(ushort timeline, string label, uint frame, params SwfLayoutRowRecord[] rows) =>
        new(label, timeline, frame, rows);

    [Fact]
    public void StatesOfSpriteTimelinesAreIgnored()
    {
        var record = Movie(0, 0, 100, 100,
            State("", 0, Row("root_row")),
            SpriteState(5, "_up", 0, Row("sprite_row", x: 3)),
            SpriteState(5, "_over", 1, Row("sprite_row", x: 4)));
        var layouts = SwfLayoutConversion.ToMenuLayouts(Ref(), record);
        var layout = Assert.Single(layouts);
        Assert.Equal(DefaultLayouts.State, layout.State);
        Assert.Equal(new[] { "root_row" }, layout.Rows.Select(row => row.Path));
    }

    [Fact]
    public void AMovieWhoseOnlyDrawableStatesAreSpritesProducesNoLayouts()
    {
        var record = Movie(0, 0, 100, 100, State("", 0), SpriteState(5, "idle", 0, Row("b")));
        Assert.Empty(SwfLayoutConversion.ToMenuLayouts(Ref(), record));
    }

    [Fact]
    public void RowsKeepTheirPathAndVisibility()
    {
        var record = Movie(0, 0, 1280, 720, State("", 0, Row("Menu/Title", visible: false)));
        var row = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single();
        Assert.Equal("Menu/Title", row.Path);
        Assert.False(row.Visible);
    }

    [Fact]
    public void RowDepthIsTheDrawOrderWithinTheStateNotThePlacementDepth()
    {
        var record = Movie(0, 0, 100, 100, State("", 0,
            Row("front", depth: 9), Row("gone", hasRect: false), Row("front", depth: 4), Row("back", depth: 2)));
        var rows = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows;
        Assert.Equal(new[] { "front", "back" }, rows.Select(row => row.Path));
        Assert.Equal(new[] { 0, 1 }, rows.Select(row => row.Depth));
    }

    [Fact]
    public void TheLayoutTakesItsStageSizeAndMenuNameFromTheMovieAndTheMapping()
    {
        var record = Movie(0, 0, 1920, 1080, State("", 0, Row("a")));
        var layout = SwfLayoutConversion.ToMenuLayouts(Ref("detail_pane"), record).Single();
        Assert.Equal("detail_pane", layout.Menu);
        Assert.Equal(1920.0, layout.StageWidth);
        Assert.Equal(1080.0, layout.StageHeight);
    }

    [Fact]
    public void RectanglesAreRelativeToTheStageOrigin()
    {
        var record = Movie(-100, -50, 800, 600, State("", 0, Row("a", x: -90, y: -40, width: 30, height: 20)));
        var row = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single();
        Assert.Equal(new LayoutRect(10, 10, 30, 20), row.Rect);
    }

    [Theory]
    [InlineData(SwfCharacterKind.Shape, LayoutRowKind.Shape)]
    [InlineData(SwfCharacterKind.Morph, LayoutRowKind.Shape)]
    [InlineData(SwfCharacterKind.Bitmap, LayoutRowKind.Bitmap)]
    [InlineData(SwfCharacterKind.Text, LayoutRowKind.Text)]
    [InlineData(SwfCharacterKind.EditText, LayoutRowKind.Text)]
    [InlineData(SwfCharacterKind.Sprite, LayoutRowKind.Clip)]
    [InlineData(SwfCharacterKind.Button, LayoutRowKind.Clip)]
    public void CharacterKindsMapToRowKinds(SwfCharacterKind kind, LayoutRowKind expected)
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("a", kind)));
        Assert.Equal(expected, SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Kind);
    }

    [Theory]
    [InlineData(SwfCharacterKind.Font)]
    [InlineData(SwfCharacterKind.Sound)]
    [InlineData(SwfCharacterKind.Video)]
    [InlineData(SwfCharacterKind.Imported)]
    public void CharacterKindsWithNothingToDrawAreDropped(SwfCharacterKind kind)
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("keep"), Row("drop", kind)));
        Assert.Equal(new[] { "keep" }, SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Select(row => row.Path));
    }

    [Fact]
    public void RowsWithoutARectangleAreDropped()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("keep"), Row("nowhere", hasRect: false)));
        Assert.Equal(new[] { "keep" }, SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Select(row => row.Path));
    }

    [Fact]
    public void ADuplicatedPathKeepsOnlyTheFirstRow()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("dup", x: 1), Row("dup", x: 2)));
        var rows = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows;
        Assert.Single(rows);
        Assert.Equal(1.0, rows[0].Rect.X);
    }

    [Fact]
    public void HtmlLineBreaksAndParagraphEndsBecomeNewlines()
    {
        var html = Text(html: true, text: "<p>one<br/>two<BR>three<br />four</p><p>five</p>");
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: html)));
        Assert.Equal("one\ntwo\nthree\nfour\nfive", SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text!.InitialText);
    }

    [Fact]
    public void ATextFieldCarriesItsStyle()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: Text(fontClass: "$MainFont"))));
        var style = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text;
        Assert.NotNull(style);
        Assert.Equal(4, style.FontId);
        Assert.Equal("$MainFont", style.FontClass);
        Assert.Equal(18.0, style.FontSize);
        Assert.Equal(LayoutTextAlign.Center, style.Align);
        Assert.True(style.Multiline);
        Assert.True(style.WordWrap);
        Assert.False(style.AutoSize);
        Assert.Equal("hello", style.InitialText);
    }

    [Fact]
    public void ColourIsRepackedFromArgbToRgba()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: Text(color: 0x80112233))));
        var style = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text;
        Assert.Equal(0x11223380u, style!.Rgba);
    }

    [Fact]
    public void AMissingColourFontAndHeightFallBackToWhiteNoFontAndTheDefaultSize()
    {
        var bare = Text(font: null, height: null, color: null, align: null);
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: bare)));
        var style = SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text;
        Assert.Null(style!.FontId);
        Assert.Null(style.FontClass);
        Assert.Equal(SwfLayoutConversion.FallbackFontSize, style.FontSize);
        Assert.Equal(0xFFFFFFFFu, style.Rgba);
        Assert.Equal(LayoutTextAlign.Left, style.Align);
    }

    [Theory]
    [InlineData(0, LayoutTextAlign.Left)]
    [InlineData(1, LayoutTextAlign.Right)]
    [InlineData(2, LayoutTextAlign.Center)]
    [InlineData(3, LayoutTextAlign.Justify)]
    public void SwfAlignCodesMapToLayoutAlignment(int code, LayoutTextAlign expected)
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: Text(align: (byte)code))));
        Assert.Equal(expected, SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text!.Align);
    }

    [Fact]
    public void HtmlInitialTextLosesItsMarkupAndEntities()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: Text(html: true, text: "<p>Fish &amp; <b>Chips</b></p>"))));
        Assert.Equal("Fish & Chips", SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text!.InitialText);
    }

    [Fact]
    public void PlainInitialTextIsKeptVerbatim()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("t", SwfCharacterKind.EditText, text: Text(text: "a <b> & c"))));
        Assert.Equal("a <b> & c", SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text!.InitialText);
    }

    [Fact]
    public void ARowWithoutTextDataHasNoStyle()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("static", SwfCharacterKind.Text)));
        Assert.Null(SwfLayoutConversion.ToMenuLayouts(Ref(), record).Single().Rows.Single().Text);
    }

    [Fact]
    public void EachLabelledStateBecomesALayoutNamedByItsLabelAndAnUnlabelledOneIsDefault()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("a")), State("open", 5, Row("b")));
        var layouts = SwfLayoutConversion.ToMenuLayouts(Ref(), record);
        Assert.Equal(new[] { DefaultLayouts.State, "open" }, layouts.Select(layout => layout.State));
        Assert.Equal(new[] { "a" }, layouts[0].Rows.Select(row => row.Path));
        Assert.Equal(new[] { "b" }, layouts[1].Rows.Select(row => row.Path));
    }

    [Fact]
    public void AStateWithNoDrawableRowsProducesNoLayout()
    {
        var record = Movie(0, 0, 100, 100, State("", 0, Row("a")), State("past_end", 99));
        Assert.Equal(new[] { DefaultLayouts.State }, SwfLayoutConversion.ToMenuLayouts(Ref(), record).Select(layout => layout.State));
    }

    [Fact]
    public void AMovieWithNothingToDrawProducesNoLayouts()
    {
        Assert.Empty(SwfLayoutConversion.ToMenuLayouts(Ref(), Movie(0, 0, 100, 100)));
    }

    [Fact]
    public void AnAliasExposesASwfRowUnderTheLogicalName()
    {
        var aliases = new Dictionary<string, string> { ["title"] = "Menu/Header" };
        var record = Movie(0, 0, 100, 100, State("", 0, Row("Menu/Header", x: 5, parent: "Menu")));
        var layout = SwfLayoutConversion.ToMenuLayouts(Ref(rows: aliases), record).Single();
        var alias = layout.Row("title");
        Assert.NotNull(alias);
        Assert.Equal(5.0, alias.Rect.X);
        Assert.Equal(5.0, layout.Row("Menu/Header")!.Rect.X);
    }

    [Fact]
    public void AnAliasForAMissingRowIsIgnored()
    {
        var aliases = new Dictionary<string, string> { ["title"] = "Nowhere" };
        var record = Movie(0, 0, 100, 100, State("", 0, Row("a")));
        Assert.Null(SwfLayoutConversion.ToMenuLayouts(Ref(rows: aliases), record).Single().Row("title"));
    }

    [Fact]
    public void AnAliasNeverReplacesARowThatAlreadyHasThatPath()
    {
        var aliases = new Dictionary<string, string> { ["a"] = "b" };
        var record = Movie(0, 0, 100, 100, State("", 0, Row("a", x: 1), Row("b", x: 2)));
        var rows = SwfLayoutConversion.ToMenuLayouts(Ref(rows: aliases), record).Single().Rows;
        Assert.Equal(1.0, Assert.Single(rows, row => row.Path == "a").Rect.X);
    }

    [Fact]
    public void ReadAllConvertsEveryMappedMenu()
    {
        var refs = new[] { Ref("list_menu"), Ref("detail_pane") };
        var source = new RadicleMenuLayoutSource(refs, _ => Movie(0, 0, 100, 100, State("", 0, Row("a"))), null);
        Assert.Equal(new[] { "list_menu", "detail_pane" }, source.ReadAll().Select(layout => layout.Menu));
        Assert.Empty(source.Failures);
    }

    [Fact]
    public void ReadAllCarriesTheMoviesFontTableOntoEveryLayoutItProduces()
    {
        var font = new SwfLayoutFontRecord(9, 3, "Futura", true, false, 1, 2, true, 800, 200, 0, 1024, [65, 66], "Futura Display", null, "$MainFont");
        var record = MovieWithFonts([font], 0, 0, 100, 100, State("", 0, Row("a")), State("open", 5, Row("b")));
        var source = new RadicleMenuLayoutSource([Ref()], _ => record, null);
        var layouts = source.ReadAll();
        Assert.Equal(2, layouts.Count);
        Assert.All(layouts, layout =>
        {
            var carried = Assert.Single(layout.Fonts);
            Assert.Equal(9, carried.FontId);
            Assert.Equal("$MainFont", carried.FontClass);
        });
    }

    [Fact]
    public void ARefusedMovieIsRecordedAndTheOtherMenusStillLoad()
    {
        var refs = new[] { Ref("bad"), Ref("good") };
        var source = new RadicleMenuLayoutSource(refs, ref_ =>
            ref_.Menu == "bad" ? throw new RadicleException(default, "no display list") : Movie(0, 0, 100, 100, State("", 0, Row("a"))), null);
        Assert.Equal(new[] { "good" }, source.ReadAll().Select(layout => layout.Menu));
        var failure = Assert.Single(source.Failures);
        Assert.StartsWith("bad: ", failure);
        Assert.Contains("no display list", failure);
    }

    [Fact]
    public void AMissingFileIsRecordedNotThrown()
    {
        var source = new RadicleMenuLayoutSource([Ref()], _ => throw new FileNotFoundException("gone"), null);
        Assert.Empty(source.ReadAll());
        Assert.Single(source.Failures);
    }

    [Fact]
    public void ReadAllStartsEachPassWithAnEmptyFailureList()
    {
        var fail = true;
        var source = new RadicleMenuLayoutSource([Ref()], _ => fail ? throw new IOException("busy") : Movie(0, 0, 100, 100, State("", 0, Row("a"))), null);
        source.ReadAll();
        Assert.Single(source.Failures);
        fail = false;
        Assert.Single(source.ReadAll());
        Assert.Empty(source.Failures);
    }

    [Fact]
    public void AnUnexpectedExceptionIsRecordedAndTheOtherMenusStillLoad()
    {
        var refs = new[] { Ref("bad"), Ref("good") };
        var source = new RadicleMenuLayoutSource(refs, ref_ =>
            ref_.Menu == "bad" ? throw new ArgumentException("path has a NUL") : Movie(0, 0, 100, 100, State("", 0, Row("a"))), null);
        Assert.Equal(new[] { "good" }, source.ReadAll().Select(layout => layout.Menu));
        var failure = Assert.Single(source.Failures);
        Assert.StartsWith("bad: ", failure);
        Assert.Contains("path has a NUL", failure);
    }

    [Fact]
    public void AMovieWithNoDrawableRowsIsRecordedAsAFailure()
    {
        var source = new RadicleMenuLayoutSource([Ref()], _ => Movie(0, 0, 100, 100, State("", 0)), null);
        Assert.Empty(source.ReadAll());
        Assert.Equal(new[] { "list_menu: no drawable rows" }, source.Failures);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(double.NaN, 100)]
    public void AMovieWithAnEmptyStageIsSkippedAndRecordedAsAFailure(double width, double height)
    {
        var refs = new[] { Ref("flat"), Ref("good") };
        var source = new RadicleMenuLayoutSource(refs, ref_ =>
            ref_.Menu == "flat" ? Movie(0, 0, width, height, State("", 0, Row("a"))) : Movie(0, 0, 100, 100, State("", 0, Row("a"))), null);
        Assert.Equal(new[] { "good" }, source.ReadAll().Select(layout => layout.Menu));
        Assert.Equal(new[] { "flat: the movie has an empty stage" }, source.Failures);
    }

    [Fact]
    public void TheSourceReportsTheBindingAbiAndTheGivenFingerprint()
    {
        var source = new RadicleMenuLayoutSource([], _ => throw new InvalidOperationException(), "abc");
        Assert.True(source.AbiVersion >= 448, "the layout export needs the ABI 448 binding");
        Assert.Equal("abc", source.InstallFingerprint);
    }

    [Fact]
    public void TheFingerprintChangesWithTheMappingAndWithTheArchiveFile()
    {
        var directory = Directory.CreateTempSubdirectory("layout-fingerprint").FullName;
        try
        {
            var archive = Path.Combine(directory, "a.ba2");
            File.WriteAllText(archive, "one");
            File.SetLastWriteTimeUtc(archive, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var refs = new[] { new MenuSwfRef("m", archive, "x.swf", NoAliases) };
            var first = RadicleMenuLayoutSource.Fingerprint("{}", refs);
            Assert.Equal(first, RadicleMenuLayoutSource.Fingerprint("{}", refs));
            Assert.NotEqual(first, RadicleMenuLayoutSource.Fingerprint("{ }", refs));
            File.WriteAllText(archive, "three");
            Assert.NotEqual(first, RadicleMenuLayoutSource.Fingerprint("{}", refs));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RelativeArchiveAndLooseEntryPathsResolveAgainstTheMappingDirectory()
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "layout-resolve"));
        var absolute = Path.Combine(directory, "abs.ba2");
        var refs = new[]
        {
            new MenuSwfRef("a", "ui.ba2", "Interface/A.swf", NoAliases),
            new MenuSwfRef("b", null, "loose/B.swf", NoAliases),
            new MenuSwfRef("c", absolute, "Interface/C.swf", NoAliases),
        };
        var resolved = RadicleMenuLayoutSource.Resolve(refs, directory);
        Assert.Equal(Path.Combine(directory, "ui.ba2"), resolved[0].Archive);
        Assert.Equal("Interface/A.swf", resolved[0].Entry);
        Assert.Null(resolved[1].Archive);
        Assert.Equal(Path.Combine(directory, "loose", "B.swf"), resolved[1].Entry);
        Assert.Equal(absolute, resolved[2].Archive);
    }

    [Fact]
    public void APathWithANulIsLeftForTheReadToRefuse()
    {
        var refs = new[] { new MenuSwfRef("a", null, "bad\0.swf", NoAliases) };
        Assert.Equal("bad\0.swf", RadicleMenuLayoutSource.Resolve(refs, Path.GetTempPath()).Single().Entry);
    }

    [Fact]
    public void OpenFingerprintsTheArchiveNextToTheMappingNotInTheWorkingDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("layout-open").FullName;
        try
        {
            var mapping = Path.Combine(directory, "menu_layouts.json");
            File.WriteAllText(mapping, """{ "menus": [ { "menu": "m", "archive": "a.ba2", "entry": "x.swf" } ] }""");
            var archive = Path.Combine(directory, "a.ba2");
            File.WriteAllText(archive, "one");
            var first = RadicleMenuLayoutSource.Open(mapping).InstallFingerprint;
            File.WriteAllText(archive, "three");
            Assert.NotEqual(first, RadicleMenuLayoutSource.Open(mapping).InstallFingerprint);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AMissingArchiveStillFingerprints()
    {
        var archive = Path.Combine(Path.GetTempPath(), "layout-missing-archive.ba2");
        var refs = new[] { new MenuSwfRef("m", archive, "x.swf", NoAliases) };
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{}\n" + archive + "|")));
        Assert.Equal(expected, RadicleMenuLayoutSource.Fingerprint("{}", refs));
    }
}
