using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class CriticalMeterTests
{
    [Fact]
    public void ItStartsEmptyAndFillsOneHitAtATime()
    {
        var meter = new CriticalMeter();
        Assert.Equal(0.0, meter.Charge);
        Assert.False(meter.Ready);
        meter.AddHit();
        Assert.Equal(1.0 / CriticalMeter.HitsToFill, meter.Charge, 9);
    }

    [Fact]
    public void ItIsReadyExactlyAtTheLastHitAndNeverOverfills()
    {
        var meter = new CriticalMeter();
        for (var i = 0; i < CriticalMeter.HitsToFill - 1; i++) meter.AddHit();
        Assert.False(meter.Ready);
        meter.AddHit();
        Assert.True(meter.Ready);
        meter.AddHit();
        Assert.Equal(1.0, meter.Charge);
    }

    [Fact]
    public void ConsumingNeedsAFullMeterAndEmptiesIt()
    {
        var meter = new CriticalMeter();
        meter.AddHit();
        Assert.False(meter.TryConsume());
        Assert.Equal(1.0 / CriticalMeter.HitsToFill, meter.Charge, 9);
        for (var i = 0; i < CriticalMeter.HitsToFill; i++) meter.AddHit();
        Assert.True(meter.TryConsume());
        Assert.False(meter.Ready);
        Assert.Equal(0.0, meter.Charge);
        Assert.False(meter.TryConsume());
    }
}
