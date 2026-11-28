using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsActivationRangeTests
{
    [Fact]
    public void The_local_foot_unit_constant_matches_the_shared_WorldScale()
    {
        Assert.Equal(WorldScale.Fo4UnitsPerMeter, VatsActivationRange.Fo4UnitsPerMeter);
    }

    [Fact]
    public void Ranged_weapons_use_weapon_max_range_in_meters_not_the_activate_default()
    {
        const float activate = 2.5f;
        var meters = VatsActivationRange.Meters(4096, activate);
        Assert.True(meters > activate);
        Assert.InRange(meters, 4096 / WorldScale.Fo4UnitsPerMeter - 0.01f, 4096 / WorldScale.Fo4UnitsPerMeter + 0.01f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Without_a_ranged_range_VATS_keeps_the_interaction_meter_default(double maxRange)
    {
        Assert.Equal(2.5f, VatsActivationRange.Meters(maxRange, 2.5f));
    }

    [Fact]
    public void Absurd_weapon_ranges_are_capped_in_meters()
    {
        const float activate = 2.5f;
        var meters = VatsActivationRange.Meters(double.PositiveInfinity, activate);
        Assert.Equal(activate, meters);
        Assert.Equal(VatsActivationRange.MaxWeaponMeters, VatsActivationRange.Meters(1_000_000, activate));
    }
}