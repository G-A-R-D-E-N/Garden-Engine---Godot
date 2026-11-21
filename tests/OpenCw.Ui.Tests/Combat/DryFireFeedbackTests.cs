using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class DryFireFeedbackTests
{
    [Fact]
    public void AnEmptyWeaponWithNoReserveNotifiesOnce()
    {
        var feedback = new DryFireFeedback();
        Assert.True(feedback.ShouldNotify(FireResult.Empty, 0));
        Assert.False(feedback.ShouldNotify(FireResult.Empty, 0));
    }

    [Fact]
    public void ItNotifiesAgainAfterTheInterval()
    {
        var feedback = new DryFireFeedback();
        Assert.True(feedback.ShouldNotify(FireResult.Empty, 0));
        feedback.Tick(DryFireFeedback.IntervalSeconds - 0.1);
        Assert.False(feedback.ShouldNotify(FireResult.Empty, 0));
        feedback.Tick(0.2);
        Assert.True(feedback.ShouldNotify(FireResult.Empty, 0));
    }

    [Fact]
    public void AReserveMeansTheReloadIsTheFeedback()
    {
        Assert.False(new DryFireFeedback().ShouldNotify(FireResult.Empty, 3));
    }

    [Theory]
    [InlineData(FireResult.Fired)]
    [InlineData(FireResult.NotReady)]
    [InlineData(FireResult.Busy)]
    [InlineData(FireResult.NoWeapon)]
    [InlineData(FireResult.Unsupported)]
    public void OtherResultsNeverNotify(FireResult result)
    {
        Assert.False(new DryFireFeedback().ShouldNotify(result, 0));
    }
}
