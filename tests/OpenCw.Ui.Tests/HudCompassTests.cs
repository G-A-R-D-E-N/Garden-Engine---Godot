using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudCompassTests
{
    private static readonly CompassMarker[] None = [];

    private static CompassMarker Marker(string id, double bearing, bool pinned = false) =>
        new(id, bearing, CompassMarkerKind.Location, pinned);

    [Theory]
    [InlineData(350, 10, -20)]
    [InlineData(10, 350, 20)]
    [InlineData(0, 180, -180)]
    [InlineData(180, 0, -180)]
    [InlineData(90, 90, 0)]
    [InlineData(725, 5, 0)]
    [InlineData(-10, 10, -20)]
    public void DeltaWrapsToTheShortestSignedAngle(double bearing, double heading, double expected)
    {
        Assert.Equal(expected, CompassModel.Delta(bearing, heading), 6);
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(725, 5)]
    [InlineData(0, 0)]
    public void HeadingNormalisesIntoOneTurn(double input, double expected)
    {
        Assert.Equal(expected, CompassModel.Normalize360(input), 6);
    }

    [Theory]
    [InlineData(359.6, 0)]
    [InlineData(359.4, 359)]
    [InlineData(-0.4, 0)]
    [InlineData(90.5, 90)]
    public void WholeDegreesNeverReadsThreeSixty(double heading, int expected)
    {
        Assert.Equal(expected, CompassModel.WholeDegrees(heading));
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(44, "NE")]
    [InlineData(350, "N")]
    [InlineData(180, "S")]
    [InlineData(225, "SW")]
    [InlineData(270, "W")]
    public void CardinalNamesTheNearestPoint(double heading, string expected)
    {
        Assert.Equal(expected, CompassModel.Cardinal(heading));
    }

    [Theory]
    [InlineData(22.5, "NE")]
    [InlineData(67.5, "E")]
    [InlineData(112.5, "SE")]
    [InlineData(157.5, "S")]
    [InlineData(202.5, "SW")]
    [InlineData(247.5, "W")]
    [InlineData(292.5, "NW")]
    [InlineData(337.5, "N")]
    public void CardinalExactBoundaryGoesToTheNextPointClockwise(double heading, string expected)
    {
        Assert.Equal(expected, CompassModel.Cardinal(heading));
    }

    [Fact]
    public void FacingNorthPutsNInTheMiddleOfAHalfTurnWindow()
    {
        var model = new CompassModel();
        model.Update(0, None);
        Assert.Equal(13, model.Ticks.Count);
        var north = model.Ticks.Single(tick => tick.Label == "N");
        Assert.Equal(0.0, north.Position, 6);
        Assert.Equal(1.0, north.Alpha, 6);
        Assert.True(north.Major);
        Assert.Contains(model.Ticks, tick => tick.Label == "E" && System.Math.Abs(tick.Position - 1) < 1e-9);
        Assert.Contains(model.Ticks, tick => tick.Label == "W" && System.Math.Abs(tick.Position + 1) < 1e-9);
    }

    [Fact]
    public void TicksWrapAroundNorth()
    {
        var model = new CompassModel();
        model.Update(350, None);
        var north = model.Ticks.Single(tick => tick.Label == "N");
        Assert.Equal(10.0 / 90, north.Position, 6);
        Assert.Contains(model.Ticks, tick => System.Math.Abs(tick.Position + 5.0 / 90) < 1e-9);
    }

    [Fact]
    public void MarkerPositionIsRelativeToHeadingAcrossTheWrap()
    {
        var model = new CompassModel();
        model.Update(355, [Marker("m", 5)]);
        var view = Assert.Single(model.Markers);
        Assert.Equal(10.0 / 90, view.Position, 6);
        Assert.False(view.Pinned);
    }

    [Fact]
    public void MarkersFadeTowardTheEdges()
    {
        var model = new CompassModel();
        model.Update(0, [Marker("mid", 0), Marker("edge", 81)]);
        Assert.Equal(1.0, model.Markers.Single(m => m.Id == "mid").Alpha, 6);
        Assert.Equal(0.4, model.Markers.Single(m => m.Id == "edge").Alpha, 6);
    }

    [Fact]
    public void UnpinnedMarkersOutOfRangeDisappearAndPinnedOnesStickToTheEdge()
    {
        var model = new CompassModel();
        model.Update(0, [Marker("a", 180), Marker("b", 120, pinned: true), Marker("c", 240, pinned: true)]);
        Assert.DoesNotContain(model.Markers, m => m.Id == "a");
        var right = model.Markers.Single(m => m.Id == "b");
        Assert.True(right.Pinned);
        Assert.Equal(1.0, right.Position, 6);
        var left = model.Markers.Single(m => m.Id == "c");
        Assert.True(left.Pinned);
        Assert.Equal(-1.0, left.Position, 6);
    }

    [Fact]
    public void HysteresisKeepsAMarkerFromFlickeringAtTheEdge()
    {
        var model = new CompassModel(180, 4);
        model.Update(0, [Marker("m", 89)]);
        Assert.Single(model.Markers);

        model.Update(0, [Marker("m", 92)]);
        var held = Assert.Single(model.Markers);
        Assert.Equal(1.0, held.Position, 6);

        model.Update(0, [Marker("m", 95)]);
        Assert.Empty(model.Markers);

        model.Update(0, [Marker("m", 92)]);
        Assert.Empty(model.Markers);

        model.Update(0, [Marker("m", 90)]);
        Assert.Single(model.Markers);
    }

    [Fact]
    public void PinnedMarkerKeepsItsSideNearTheBackOfTheCompass()
    {
        var model = new CompassModel(180, 4);
        model.Update(0, [Marker("m", 179, pinned: true)]);
        Assert.Equal(1.0, model.Markers[0].Position, 6);

        model.Update(0, [Marker("m", 181, pinned: true)]);
        Assert.Equal(1.0, model.Markers[0].Position, 6);

        model.Update(0, [Marker("m", 200, pinned: true)]);
        Assert.Equal(-1.0, model.Markers[0].Position, 6);
    }

    [Fact]
    public void NonFiniteInputIsIgnored()
    {
        var model = new CompassModel();
        model.Update(double.NaN, [Marker("bad", double.NaN), Marker("ok", 10)]);
        var view = Assert.Single(model.Markers);
        Assert.Equal("ok", view.Id);
        Assert.Equal(10.0 / 90, view.Position, 6);
    }

    [Fact]
    public void ConstructorRejectsBadSettings()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CompassModel(0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CompassModel(361));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CompassModel(180, -1));
    }
}
