using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class EssentialDownSettingsTests
{
    private static EssentialDownSettings Read(string? seconds, string? regain)
    {
        var reader = new FakeRecordReader();
        if (seconds is not null) reader.Add("000001:fo4.esm", "GMST", EssentialDownSettings.RecoverSecondsSetting).Scalars[PickpocketSettings.FloatPath] = seconds;
        if (regain is not null) reader.Add("000002:fo4.esm", "GMST", EssentialDownSettings.RegainFractionSetting).Scalars[PickpocketSettings.FloatPath] = regain;
        return EssentialDownSettings.Read(reader);
    }

    [Fact]
    public void Missing_settings_read_as_the_documented_defaults()
    {
        var s = Read(null, null);
        Assert.Equal(EssentialDownSettings.Default.RecoverSeconds, s.RecoverSeconds);
        Assert.Equal(EssentialDownSettings.Default.RegainFraction, s.RegainFraction);
    }

    [Fact]
    public void Present_settings_are_used_and_a_value_above_one_is_a_percent()
    {
        var s = Read("12", "30");
        Assert.Equal(12f, s.RecoverSeconds);
        Assert.Equal(0.3f, s.RegainFraction, 5);
        Assert.Equal(0.75f, Read("12", "0.75").RegainFraction, 5);
    }

    [Fact]
    public void Negative_seconds_and_out_of_range_fractions_fall_back()
    {
        var s = Read("-4", "-0.5");
        Assert.Equal(EssentialDownSettings.Default.RecoverSeconds, s.RecoverSeconds);
        Assert.Equal(EssentialDownSettings.Default.RegainFraction, s.RegainFraction);
        Assert.Equal(EssentialDownSettings.Default.RegainFraction, Read("5", "250").RegainFraction);
    }
}
