using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class XpCurveReadStatusTests
{
    const string BaseId = "000001:a.esm";
    const string BumpId = "000002:a.esm";

    static FakeRecordReader Settings(string? baseValue = "100", string? bumpValue = "10")
    {
        var reader = new FakeRecordReader();
        if (baseValue is not null) reader.Add(BaseId, "GMST", XpCurve.BaseSetting).Scalars[RecordCrimeFacts.IntegerPath] = baseValue;
        if (bumpValue is not null) reader.Add(BumpId, "GMST", XpCurve.BumpSetting).Scalars[RecordCrimeFacts.IntegerPath] = bumpValue;
        return reader;
    }

    [Fact]
    public void A_failed_setting_read_reports_failure_and_keeps_the_default_pair()
    {
        var reader = Settings();
        reader.Failing.Add(BumpId);

        var curve = XpCurve.Read(reader, out var failed);

        Assert.True(failed);
        Assert.Equal(XpCurve.Default, curve);
    }

    [Fact]
    public void The_real_curve_is_read_once_the_source_recovers()
    {
        var reader = Settings();
        reader.Failing.Add(BaseId);
        XpCurve.Read(reader, out var failed);
        Assert.True(failed);

        reader.Failing.Clear();
        var curve = XpCurve.Read(reader, out failed);

        Assert.False(failed);
        Assert.Equal(new XpCurve(100, 10), curve);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("100", null)]
    [InlineData("0", "10")]
    [InlineData("100", "-1")]
    [InlineData("100", "x")]
    public void An_absent_or_invalid_setting_is_a_settled_default_and_not_a_failure(string? baseValue, string? bumpValue)
    {
        var curve = XpCurve.Read(Settings(baseValue, bumpValue), out var failed);

        Assert.False(failed);
        Assert.Equal(XpCurve.Default, curve);
    }

    [Fact]
    public void A_recovered_curve_rederives_the_level_of_the_loaded_total()
    {
        var experience = new PlayerExperience();
        experience.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = "{\"total\":475}"u8.ToArray() });
        var wide = new XpCurve(1000, 0);
        experience.Curve = wide;
        Assert.Equal(1, experience.Level);
        var changed = 0;
        experience.Changed += () => changed++;

        experience.Curve = XpCurve.Default;

        Assert.Equal(3, experience.Level);
        Assert.Equal(1, changed);
    }
}
