using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ArmorMitigationTests
{
    [Theory]
    [InlineData(100.0)]
    [InlineData(0.5)]
    [InlineData(1e9)]
    public void UnarmoredTargetsTakeTheFullDamage(double rawDamage)
    {
        Assert.Equal(1.0, ArmorMitigation.DamageMultiplier(rawDamage, 0));
        Assert.Equal(rawDamage, ArmorMitigation.Mitigate(rawDamage, 0), 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-10.0)]
    public void AnUnusableResistanceCountsAsUnarmored(double resistance)
    {
        Assert.Equal(1.0, ArmorMitigation.DamageMultiplier(100, resistance));
        Assert.Equal(100.0, ArmorMitigation.Mitigate(100, resistance), 9);
    }

    [Fact]
    public void InfiniteResistanceMitigatesToTheMinimumMultiplier()
    {
        Assert.Equal(0.01, ArmorMitigation.DamageMultiplier(100, double.PositiveInfinity), 12);
        Assert.Equal(1.0, ArmorMitigation.Mitigate(100, double.PositiveInfinity), 9);
    }

    [Theory]
    [InlineData(double.NaN, 0.366)]
    [InlineData(-0.5, 0.366)]
    [InlineData(0.5, double.NaN)]
    [InlineData(0.5, -1.0)]
    public void AnUnusableBaseFactorOrExponentFallsBackToDefaults(double baseFactor, double exponent)
    {
        var curve = new ArmorMitigationCurve(BaseFactor: baseFactor, Exponent: exponent);
        Assert.Equal(0.5, curve.EffectiveBaseFactor, 12);
        Assert.Equal(0.366, curve.EffectiveExponent, 12);
        Assert.Equal(0.5, ArmorMitigation.DamageMultiplier(100, 100, curve), 12);
    }

    [Theory]
    [InlineData(0.0, 10.0)]
    [InlineData(-5.0, 10.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(double.PositiveInfinity, 10.0)]
    [InlineData(0.0, 0.0)]
    public void ADamageThatIsNotAPositiveFiniteNumberDealsNothing(double rawDamage, double resistance)
    {
        Assert.Equal(0.0, ArmorMitigation.DamageMultiplier(rawDamage, resistance));
        Assert.Equal(0.0, ArmorMitigation.Mitigate(rawDamage, resistance));
    }

    [Fact]
    public void DamageEqualToResistanceIsHalvedOnTheStandardCurve()
    {
        Assert.Equal(0.5, ArmorMitigation.DamageMultiplier(100, 100), 12);
        Assert.Equal(50.0, ArmorMitigation.Mitigate(100, 100), 12);
    }

    [Fact]
    public void HeavyArmorKeepsTheMinimumMultiplier()
    {
        Assert.Equal(0.01, ArmorMitigation.DamageMultiplier(1, 1e9));
        Assert.Equal(0.01, ArmorMitigation.Mitigate(1, 1e9), 12);
    }

    [Fact]
    public void LightArmorIsCappedAtTheMaximumMultiplier()
    {
        Assert.Equal(0.99, ArmorMitigation.DamageMultiplier(1e9, 1));
        Assert.Equal(9.9e8, ArmorMitigation.Mitigate(1e9, 1), 6);
    }

    [Theory]
    [InlineData(0.0, 0.01)]
    [InlineData(-1.0, 0.01)]
    [InlineData(double.NaN, 0.01)]
    [InlineData(2.0, 0.01)]
    public void AnUnusableMinimumFallsBackToTheDefaultFloor(double minMultiplier, double expectedMin)
    {
        Assert.Equal(expectedMin, new ArmorMitigationCurve(MinMultiplier: minMultiplier).Min);
    }

    [Theory]
    [InlineData(2.0, 0.99)]
    [InlineData(-1.0, 0.99)]
    [InlineData(double.NaN, 0.99)]
    [InlineData(double.PositiveInfinity, 0.99)]
    public void AnUnusableMaximumFallsBackToTheDefaultCeiling(double maxMultiplier, double expectedMax)
    {
        Assert.Equal(expectedMax, new ArmorMitigationCurve(MaxMultiplier: maxMultiplier).Max);
    }

    [Fact]
    public void AMaximumBelowTheMinimumIsRaisedToTheMinimum()
    {
        var curve = new ArmorMitigationCurve(MinMultiplier: 0.4, MaxMultiplier: 0.2);
        Assert.Equal(0.4, curve.Min);
        Assert.Equal(0.4, curve.Max);
        Assert.Equal(0.4, ArmorMitigation.DamageMultiplier(100, 100, curve), 12);
    }

    [Fact]
    public void ACustomCurveMovesTheMultiplier()
    {
        var curve = new ArmorMitigationCurve(BaseFactor: 0.25, Exponent: 1.0);
        Assert.Equal(0.25, ArmorMitigation.DamageMultiplier(100, 100, curve), 12);
        Assert.Equal(0.5, ArmorMitigation.DamageMultiplier(200, 100, curve), 12);
    }

    [Fact]
    public void MitigateAllSumsEachChannelWithItsOwnResistance()
    {
        var channels = new List<DamageChannel> { new DamageChannel(100, 0), new DamageChannel(50, 50), new DamageChannel(10, 0) };
        Assert.Equal(135.0, ArmorMitigation.MitigateAll(channels), 12);
    }

    [Fact]
    public void MitigateAllOfNoChannelsIsNoDamage()
    {
        Assert.Equal(0.0, ArmorMitigation.MitigateAll([]));
        Assert.Equal(0.0, ArmorMitigation.MitigateAll(null!));
    }

    [Fact]
    public void MitigateAllIgnoresAChannelWithoutDamage()
    {
        var channels = new List<DamageChannel> { new DamageChannel(0, 100), new DamageChannel(double.NaN, 100), new DamageChannel(40, 0) };
        Assert.Equal(40.0, ArmorMitigation.MitigateAll(channels), 12);
    }
}
