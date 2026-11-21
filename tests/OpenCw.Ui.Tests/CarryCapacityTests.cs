using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CarryCapacityTests
{
    // Detects: a capacity that ignores Strength or accepts a bad value; it must be the base plus ten per point and null when Strength is unknown.
    [Theory]
    [InlineData(1.0, 210.0)]
    [InlineData(10.0, 300.0)]
    public void CapacityGrowsWithStrength(double strength, double expected) =>
        Assert.Equal(expected, CarryCapacity.FromStrength(strength));

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void AnUnknownStrengthHasNoCapacity(double? strength) =>
        Assert.Null(CarryCapacity.FromStrength(strength));

    [Fact]
    public void TheFooterShowsTheSuppliedCapacity()
    {
        var status = new LivePipBoyStatus(new PlayerVitals(100, 40, 50, 10), () => 3, () => 10.0, () => 50.0, carryWeight: () => 120.0, carryCapacity: () => 250.0);
        Assert.Equal(250.0, status.CarryCapacity);
    }

    [Fact]
    public void TheFooterHasNoCapacityWithoutASource()
    {
        var status = new LivePipBoyStatus(null, () => 3, () => 10.0, () => 50.0);
        Assert.Null(status.CarryCapacity);
    }
}
