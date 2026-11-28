using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsRevertCountdownTests
{
    [Fact]
    public void It_expires_exactly_once_after_the_duration_and_then_goes_inactive()
    {
        var countdown = new RevertCountdown();
        countdown.Start(3);

        Assert.True(countdown.IsActive);
        Assert.False(countdown.Tick(1));
        Assert.False(countdown.Tick(1.5));
        Assert.Equal(1, countdown.WholeSecondsLeft);
        Assert.True(countdown.Tick(0.5));
        Assert.False(countdown.IsActive);
        Assert.False(countdown.Tick(10));
        Assert.Equal(0, countdown.WholeSecondsLeft);
    }

    [Fact]
    public void Zero_negative_and_non_finite_ticks_do_not_advance_it()
    {
        var countdown = new RevertCountdown();
        countdown.Start(2);

        Assert.False(countdown.Tick(0));
        Assert.False(countdown.Tick(-5));
        Assert.False(countdown.Tick(double.NaN));
        Assert.False(countdown.Tick(double.PositiveInfinity));
        Assert.Equal(2.0, countdown.Remaining);
    }

    [Fact]
    public void A_large_single_tick_expires_it_and_stop_cancels_without_expiry()
    {
        var countdown = new RevertCountdown();
        countdown.Start(2);
        Assert.True(countdown.Tick(60));

        countdown.Start(2);
        countdown.Stop();
        Assert.False(countdown.Tick(60));
    }
}
