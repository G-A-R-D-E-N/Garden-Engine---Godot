using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class PelletSpreadTests
{
    [Fact]
    public void ASinglePelletStaysOnTheCentreRayAndDrawsNothing()
    {
        var offsets = PelletSpread.Offsets(1, 5, () => throw new InvalidOperationException());
        Assert.Equal((0.0, 0.0), Assert.Single(offsets));
    }

    [Fact]
    public void EachPelletGetsItsOwnOffsetInsideTheCone()
    {
        var random = new Random(7);
        var offsets = PelletSpread.Offsets(9, 6, random.NextDouble);
        Assert.Equal(9, offsets.Count);
        var limit = 6 * Math.PI / 180;
        Assert.All(offsets, o => Assert.True(Math.Sqrt(o.X * o.X + o.Y * o.Y) <= limit + 1e-12));
        Assert.True(offsets.Select(o => o.X).Distinct().Count() > 1);
    }

    [Fact]
    public void TheEdgeRollsReachTheConeEdge()
    {
        var rolls = new Queue<double>([0.999999, 0.0]);
        var offsets = PelletSpread.Offsets(2, 4, () => rolls.Count > 0 ? rolls.Dequeue() : 0.999999);
        Assert.Equal(4 * Math.PI / 180, offsets[0].X, 4);
        Assert.Equal(0, offsets[0].Y, 6);
    }

    [Theory]
    [InlineData(0.0, PelletSpread.DefaultHalfAngleDegrees)]
    [InlineData(-2.0, PelletSpread.DefaultHalfAngleDegrees)]
    [InlineData(double.NaN, PelletSpread.DefaultHalfAngleDegrees)]
    [InlineData(5.0, 5.0)]
    [InlineData(500.0, PelletSpread.MaxHalfAngleDegrees)]
    public void TheHalfAngleFallsBackAndIsCapped(double record, double expected)
    {
        Assert.Equal(expected, PelletSpread.HalfAngleDegrees(record));
    }
}
