using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SweepShapeTests
{
    [Fact]
    public void TheRadiusIsTheMeanHalfExtentJustInsideTheBoundsAndClamped()
    {
        Assert.Equal(0.09, SweepShape.Radius(0.2, 0.2, 0.2), 6);
        Assert.Equal(SweepShape.MinRadius, SweepShape.Radius(0, 0, 0));
        Assert.Equal(SweepShape.MaxRadius, SweepShape.Radius(10, 10, 10));
        Assert.Equal(SweepShape.MinRadius, SweepShape.Radius(double.NaN, 1, 1));
        Assert.Equal(SweepShape.MinRadius, SweepShape.Radius(double.PositiveInfinity, 1, 1));
    }

    [Fact]
    public void AMotionCastCutShortIsBlockedAndAClearOrUnreadableOneIsNot()
    {
        Assert.Equal(0.4, SweepShape.SafeFraction(new[] { 0.4f, 0.5f }), 6);
        Assert.True(SweepShape.Blocked(new[] { 0f, 0f }));
        Assert.False(SweepShape.Blocked(new[] { 1f, 1f }));
        Assert.False(SweepShape.Blocked(System.Array.Empty<float>()));
        Assert.False(SweepShape.Blocked(null));
        Assert.Equal(1, SweepShape.SafeFraction(new[] { float.NaN, 1f }));
        Assert.Equal(0, SweepShape.SafeFraction(new[] { -3f, 1f }));
    }

    [Fact]
    public void TheBackoffStartsAtTheObjectStepsTowardThePlayerAndEndsExactlyAtThePlayer()
    {
        var distances = SweepShape.Backoff(0.25).ToArray();

        Assert.Equal(new[] { 0.0, 0.1, 0.2, 0.25 }, distances.Select(distance => System.Math.Round(distance, 6)).ToArray());
        Assert.Empty(SweepShape.Backoff(0));
        Assert.Empty(SweepShape.Backoff(double.NaN));
        Assert.Equal(new[] { 0.0, 0.1 }, SweepShape.Backoff(0.1).Select(distance => System.Math.Round(distance, 6)).ToArray());
    }
}
