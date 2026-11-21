using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class VatsSlowTimeTests
{
    private sealed class Plain : IMenu
    {
        public string Id => "plain";
        public bool HandleAction(MenuInputAction action) => false;
    }

    [Fact]
    public void ARealDeltaUndoesTheEngineTimeScale()
    {
        Assert.Equal(0.5, VatsAssumptions.RealDelta(0.05, VatsAssumptions.SlowTimeScale), 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnusableScaleLeavesTheDeltaAlone(double scale)
    {
        Assert.Equal(0.016, VatsAssumptions.RealDelta(0.016, scale));
    }

    [Fact]
    public void TheSlowScaleIsASlowdown()
    {
        Assert.InRange(VatsAssumptions.SlowTimeScale, 0.01, 0.99);
    }

    [Fact]
    public void AMenuPausesTheWorldUnlessItSaysOtherwise()
    {
        Assert.True(((IMenu)new Plain()).PausesWorld);
    }
}
