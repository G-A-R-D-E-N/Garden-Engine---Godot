using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DropPlacementTests
{
    [Fact]
    public void AheadMovesTheDropForwardByTheFixedDistanceWhateverTheLookLength()
    {
        var (x, z) = DropPlacement.Ahead(10, 20, 0, -5);

        Assert.Equal(10, x, 9);
        Assert.Equal(20 - DropPlacement.ForwardMeters, z, 9);
    }

    [Fact]
    public void LookingStraightUpOrDownDropsAtTheFeet()
    {
        Assert.Equal((10.0, 20.0), DropPlacement.Ahead(10, 20, 0.001, 0));
        Assert.Equal((10.0, 20.0), DropPlacement.Ahead(10, 20, 0, 0));
    }

    [Fact]
    public void ARestingItemSitsOnTheGroundWithItsLowestPointLifted()
    {
        Assert.Equal(5 + DropPlacement.LiftMeters, DropPlacement.RestY(5, 0), 9);
        Assert.Equal(5 + DropPlacement.LiftMeters + 0.1, DropPlacement.RestY(5, -0.1), 9);
        Assert.Equal(5 + DropPlacement.LiftMeters - 0.2, DropPlacement.RestY(5, 0.2), 9);
    }

    [Fact]
    public void ARunawayModelExtentIsClampedAndANonFiniteOneIsIgnored()
    {
        Assert.Equal(5 + DropPlacement.LiftMeters + DropPlacement.MaxRestShiftMeters, DropPlacement.RestY(5, -50), 9);
        Assert.Equal(5 + DropPlacement.LiftMeters - DropPlacement.MaxRestShiftMeters, DropPlacement.RestY(5, 50), 9);
        Assert.Equal(5 + DropPlacement.LiftMeters, DropPlacement.RestY(5, double.PositiveInfinity), 9);
        Assert.Equal(5 + DropPlacement.LiftMeters, DropPlacement.RestY(5, double.NaN), 9);
    }
}
