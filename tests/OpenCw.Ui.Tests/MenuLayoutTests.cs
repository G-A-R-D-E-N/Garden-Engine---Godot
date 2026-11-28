using System.Linq;
using OpenCommonwealth.Services.Ui.Layout;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class MenuLayoutTests
{
    private static readonly LayoutRect None = new(0, 0, 0, 0);

    private static MenuLayout Layout(double stageWidth, double stageHeight, params LayoutRow[] rows) =>
        new("message_box", "default", stageWidth, stageHeight, [], rows);

    private static LayoutRow Row(string path, LayoutRect rect) =>
        new(path, LayoutRowKind.Shape, 0, true, rect, null);

    [Fact]
    public void ResolvePrefersTheCachedRowOverTheDefault()
    {
        var cached = Layout(1920, 1080, Row("title", new(1, 2, 3, 4)));
        var fallback = Layout(1280, 720, Row("title", new(9, 9, 9, 9)));
        var resolved = LayoutRows.Resolve(cached, fallback, "title");
        Assert.NotNull(resolved);
        Assert.Same(cached, resolved.Value.Layout);
        Assert.Equal(new LayoutRect(1, 2, 3, 4), resolved.Value.Row.Rect);
    }

    [Fact]
    public void ResolveFallsBackToTheDefaultRowWhenTheCacheLacksIt()
    {
        var cached = Layout(1920, 1080, Row("renamed", new(1, 2, 3, 4)));
        var fallback = Layout(1280, 720, Row("title", new(9, 9, 9, 9)));
        var resolved = LayoutRows.Resolve(cached, fallback, "title");
        Assert.NotNull(resolved);
        Assert.Same(fallback, resolved.Value.Layout);
        Assert.Equal(new LayoutRect(9, 9, 9, 9), resolved.Value.Row.Rect);
    }

    [Fact]
    public void ResolveScalesADefaultRowWithTheDefaultStage()
    {
        var cached = Layout(1920, 1080);
        var fallback = Layout(1280, 720, Row("title", None));
        var resolved = LayoutRows.Resolve(cached, fallback, "title");
        Assert.NotNull(resolved);
        Assert.Equal(1280.0, resolved.Value.Layout.StageWidth);
        Assert.Equal(720.0, resolved.Value.Layout.StageHeight);
    }

    [Fact]
    public void ResolveWithoutAnyCachedLayoutUsesTheDefault()
    {
        var fallback = Layout(1280, 720, Row("title", new(9, 9, 9, 9)));
        var resolved = LayoutRows.Resolve(null, fallback, "title");
        Assert.NotNull(resolved);
        Assert.Same(fallback, resolved.Value.Layout);
    }

    [Fact]
    public void ResolveReturnsNothingForAPathNoLayoutHas()
    {
        var cached = Layout(1920, 1080, Row("title", None));
        var fallback = Layout(1280, 720, Row("title", None));
        Assert.Null(LayoutRows.Resolve(cached, fallback, "missing"));
    }

    [Fact]
    public void FitScalesByTheSmallerAxisAndCentresTheStage()
    {
        var fit = StageFit.Fit(new(0, 0, 1920, 1200), new(0, 0, 1920, 1200), 1280, 720);
        Assert.Equal(1.5, fit.Scale, 6);
        Assert.Equal(0.0, fit.OffsetX, 6);
        Assert.Equal(60.0, fit.OffsetY, 6);
    }

    [Fact]
    public void FitLetterboxesAWindowThatIsTallerThanTheStage()
    {
        var fit = StageFit.Fit(new(0, 0, 1280, 1440), new(0, 0, 1280, 1440), 1280, 720);
        Assert.Equal(1.0, fit.Scale, 6);
        Assert.Equal(0.0, fit.OffsetX, 6);
        Assert.Equal(360.0, fit.OffsetY, 6);
    }

    [Fact]
    public void FitPillarboxesAWindowThatIsWiderThanTheStage()
    {
        var fit = StageFit.Fit(new(0, 0, 2560, 720), new(0, 0, 2560, 720), 1280, 720);
        Assert.Equal(1.0, fit.Scale, 6);
        Assert.Equal(640.0, fit.OffsetX, 6);
        Assert.Equal(0.0, fit.OffsetY, 6);
    }

    [Fact]
    public void FitKeepsTheStageInsideTheSafeAreaNotTheWholeArea()
    {
        var fit = StageFit.Fit(new(0, 0, 1920, 1080), new(96, 54, 1728, 972), 1280, 720);
        var stage = fit.Apply(new(0, 0, 1280, 720));
        Assert.Equal(96.0, stage.X, 6);
        Assert.Equal(54.0, stage.Y, 6);
        Assert.Equal(1728.0, stage.Width, 6);
        Assert.Equal(972.0, stage.Height, 6);
    }

    [Fact]
    public void FitUsesTheWholeAreaWhenTheSafeAreaIsEmpty()
    {
        var fit = StageFit.Fit(new(0, 0, 1920, 1080), None, 1280, 720);
        Assert.Equal(1.5, fit.Scale, 6);
        Assert.Equal(0.0, fit.OffsetX, 6);
        Assert.Equal(0.0, fit.OffsetY, 6);
    }

    [Fact]
    public void FitOfAZeroAreaGivesZeroSizesNotNaN()
    {
        var fit = StageFit.Fit(None, None, 1280, 720);
        var rect = fit.Apply(new(10, 20, 300, 40));
        Assert.Equal(0.0, rect.Width, 6);
        Assert.Equal(0.0, rect.Height, 6);
        Assert.False(double.IsNaN(rect.X) || double.IsNaN(rect.Y));
    }

    [Fact]
    public void ApplyMapsARowThroughTheTransform()
    {
        var transform = new StageTransform(1.5, 0, 60);
        Assert.Equal(new LayoutRect(150, 135, 300, 60), transform.Apply(new(100, 50, 200, 40)));
    }

    [Fact]
    public void DefaultLayoutsUseTheDefaultStateAndAPositiveStage()
    {
        Assert.Equal(new[] { "message_box", "list_menu", "detail_pane" }, DefaultLayouts.All.Select(layout => layout.Menu));
        Assert.All(DefaultLayouts.All, layout =>
        {
            Assert.Equal(DefaultLayouts.State, layout.State);
            Assert.True(layout.StageWidth > 0 && layout.StageHeight > 0);
        });
    }

    [Fact]
    public void DefaultRowPathsAreUniqueWithinEachMenu()
    {
        foreach (var layout in DefaultLayouts.All)
        {
            var paths = layout.Rows.Select(row => row.Path).ToList();
            Assert.Equal(paths.Count, paths.Distinct().Count());
        }
    }

    [Fact]
    public void DefaultRowsStayInsideTheirStage()
    {
        foreach (var layout in DefaultLayouts.All)
        {
            foreach (var row in layout.Rows)
            {
                var rect = row.Rect;
                Assert.True(rect.X >= 0 && rect.Y >= 0, $"{layout.Menu}/{row.Path} starts outside the stage");
                Assert.True(rect.X + rect.Width <= layout.StageWidth && rect.Y + rect.Height <= layout.StageHeight,
                    $"{layout.Menu}/{row.Path} ends outside the stage");
            }
        }
    }
}
