using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GameClockConversionTests
{
    // Detects: a world clock converted to the wrong day or minute for the wait dialog; the result must keep the whole day and the minute of the day.
    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(10.0, 0, 600)]
    [InlineData(49.5, 2, 90)]
    [InlineData(23.999, 0, 1439)]
    public void TotalHoursSplitIntoDayAndMinute(double hours, int day, int minute)
    {
        var clock = GameClock.FromTotalHours(hours);
        Assert.Equal(day, clock.Day);
        Assert.Equal(minute, clock.MinuteOfDay);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5.0)]
    public void ABadTotalClampsToTheStart(double hours)
    {
        var clock = GameClock.FromTotalHours(hours);
        Assert.Equal(0, clock.Day);
        Assert.Equal(0, clock.MinuteOfDay);
    }
}
