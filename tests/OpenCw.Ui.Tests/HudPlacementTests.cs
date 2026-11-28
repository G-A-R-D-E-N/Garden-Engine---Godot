using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Layout;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudPlacementTests
{
    private const double StageW = 1280;
    private const double StageH = 720;

    private static LayoutRect Slot(HudElement element) =>
        HudLayouts.Default.Row(HudSlots.Key(element))!.Rect;

    private static HudPlaced Place(HudElement element, double areaW, double areaH, double margin = 0, double scale = 1,
        HudElementPlacement user = default) =>
        HudPlacement.Place(Slot(element), StageW, StageH, new LayoutRect(0, 0, areaW, areaH), margin, scale, user);

    private static void AssertRect(LayoutRect expected, LayoutRect actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void DefaultLayoutHasOneUniqueSlotPerElement()
    {
        var keys = HudLayouts.Default.Rows.Select(row => row.Path).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var element in System.Enum.GetValues<HudElement>())
            Assert.Contains(HudSlots.Key(element), keys);
        Assert.Equal(HudSlots.Menu, HudLayouts.Default.Menu);
    }

    [Fact]
    public void DefaultSlotsStayInsideTheStageAndDoNotOverlap()
    {
        var rows = HudLayouts.Default.Rows;
        foreach (var row in rows)
        {
            Assert.True(row.Rect.X >= 0 && row.Rect.Y >= 0);
            Assert.True(row.Rect.X + row.Rect.Width <= StageW);
            Assert.True(row.Rect.Y + row.Rect.Height <= StageH);
        }
        for (int i = 0; i < rows.Count; i++)
            for (int j = i + 1; j < rows.Count; j++)
            {
                var a = rows[i].Rect;
                var b = rows[j].Rect;
                bool apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"{rows[i].Path} overlaps {rows[j].Path}");
            }
    }

    [Fact]
    public void AtStageSizeEverySlotLandsWhereTheLayoutPutIt()
    {
        foreach (var element in System.Enum.GetValues<HudElement>())
        {
            var placed = Place(element, StageW, StageH);
            AssertRect(Slot(element), placed.Rect);
            Assert.Equal(1.0, placed.Scale, 6);
        }
    }

    [Fact]
    public void ScalingTheWindowScalesEdgeDistancesAndSizes()
    {
        var placed = Place(HudElement.Health, 1920, 1080);
        AssertRect(new LayoutRect(60, 996, 420, 30), placed.Rect);
        Assert.Equal(1.5, placed.Scale, 6);
    }

    [Fact]
    public void RightAndCentreSlotsStickToTheirEdgeOnAnUltrawideScreen()
    {
        var right = Place(HudElement.ActionPoints, 2560, 720).Rect;
        Assert.Equal(2560 - 40 - 280.0, right.X, 6);
        var centre = Place(HudElement.Compass, 2560, 720).Rect;
        Assert.Equal(1280 - 200.0, centre.X, 6);
    }

    [Fact]
    public void UserScaleGrowsTheWidgetButKeepsItsAnchoredEdge()
    {
        var left = Place(HudElement.Health, StageW, StageH, scale: 2);
        Assert.Equal(40.0, left.Rect.X, 6);
        Assert.Equal(560.0, left.Rect.Width, 6);
        Assert.Equal(2.0, left.Scale, 6);

        var right = Place(HudElement.ActionPoints, StageW, StageH, scale: 2).Rect;
        Assert.Equal(StageW - 40, right.X + right.Width, 6);
    }

    [Fact]
    public void SafeMarginInsetsEveryEdge()
    {
        var left = Place(HudElement.Health, StageW, StageH, margin: 30).Rect;
        Assert.Equal(70.0, left.X, 6);
        Assert.Equal(664.0 - 30, left.Y, 6);
    }

    [Fact]
    public void UserOffsetMovesTheWidget()
    {
        var moved = Place(HudElement.Health, StageW, StageH, user: new HudElementPlacement(null, 10, -5)).Rect;
        Assert.Equal(50.0, moved.X, 6);
        Assert.Equal(659.0, moved.Y, 6);
    }

    [Fact]
    public void AnchorOverrideSnapsToTheChosenCorner()
    {
        var rect = Place(HudElement.Health, StageW, StageH, user: new HudElementPlacement(HudAnchor.TopRight, 0, 0)).Rect;
        Assert.Equal(StageW - 280, rect.X, 6);
        Assert.Equal(0.0, rect.Y, 6);

        var centred = Place(HudElement.Health, StageW, StageH, user: new HudElementPlacement(HudAnchor.Center, 0, 0)).Rect;
        Assert.Equal((StageW - 280) / 2, centred.X, 6);
        Assert.Equal((StageH - 20) / 2, centred.Y, 6);
    }

    [Fact]
    public void OffsetsCannotPushAWidgetOutOfTheSafeArea()
    {
        var far = Place(HudElement.Health, StageW, StageH, margin: 20, user: new HudElementPlacement(null, 5000, 5000)).Rect;
        Assert.Equal(StageW - 20 - 280, far.X, 6);
        Assert.Equal(StageH - 20 - 20, far.Y, 6);

        var near = Place(HudElement.Health, StageW, StageH, margin: 20, user: new HudElementPlacement(null, -5000, -5000)).Rect;
        Assert.Equal(20.0, near.X, 6);
        Assert.Equal(20.0, near.Y, 6);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1080)]
    [InlineData(1024, 768)]
    [InlineData(800, 600)]
    public void EverySlotStaysOnScreenAtCommonResolutions(double width, double height)
    {
        foreach (var element in System.Enum.GetValues<HudElement>())
        {
            var rect = Place(element, width, height).Rect;
            Assert.True(rect.X >= -1e-6 && rect.Y >= -1e-6, element.ToString());
            Assert.True(rect.X + rect.Width <= width + 1e-6, element.ToString());
            Assert.True(rect.Y + rect.Height <= height + 1e-6, element.ToString());
        }
    }

    [Fact]
    public void ReplacementLayoutRowsWinOverTheDefaults()
    {
        var custom = new MenuLayout(HudSlots.Menu, "default", 1920, 1080, [],
            [new LayoutRow("health", LayoutRowKind.Shape, 0, true, new LayoutRect(100, 900, 400, 40), null)]);
        var resolved = LayoutRows.Resolve(custom, HudLayouts.Default, HudSlots.Key(HudElement.Health));
        Assert.NotNull(resolved);
        Assert.Same(custom, resolved.Value.Layout);
        var compass = LayoutRows.Resolve(custom, HudLayouts.Default, HudSlots.Key(HudElement.Compass));
        Assert.Same(HudLayouts.Default, compass!.Value.Layout);
    }
}
