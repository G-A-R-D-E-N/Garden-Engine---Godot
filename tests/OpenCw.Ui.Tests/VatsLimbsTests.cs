using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class VatsLimbsTests
{
    [Theory]
    [InlineData(10f, 2f, 20f)]
    [InlineData(10f, 0f, 10f)]
    [InlineData(10f, -1f, 10f)]
    [InlineData(10f, float.NaN, 10f)]
    public void PartDamageScalesByAUsableMultiplier(float damage, float mult, float expected)
    {
        Assert.Equal(expected, VatsLimbs.PartDamage(damage, mult));
    }

    [Fact]
    public void CapacityIsAShareOfMaxHealth()
    {
        Assert.Equal(25f, VatsLimbs.Capacity(100f, 25));
        Assert.Equal(0f, VatsLimbs.Capacity(100f, 0));
        Assert.Equal(0f, VatsLimbs.Capacity(null, 25));
        Assert.Equal(0f, VatsLimbs.Capacity(0f, 25));
    }

    [Fact]
    public void DamageToOnePartLeavesTheOthersWhole()
    {
        var limbs = new VatsLimbs();
        limbs.Add("ref1", 3, 10f);
        Assert.Equal(0.6, limbs.Fraction("ref1", 3, 25f), 6);
        Assert.Equal(1.0, limbs.Fraction("ref1", 4, 25f));
        Assert.Equal(1.0, limbs.Fraction("ref2", 3, 25f));
    }

    [Fact]
    public void APartIsCrippledOnceItsCapacityIsReached()
    {
        var limbs = new VatsLimbs();
        limbs.Add("ref1", 3, 20f);
        Assert.False(limbs.Crippled("ref1", 3, 25f));
        limbs.Add("ref1", 3, 5f);
        Assert.True(limbs.Crippled("ref1", 3, 25f));
        Assert.Equal(0.0, limbs.Fraction("ref1", 3, 25f));
    }

    [Fact]
    public void APartWithNoCapacityIsNeverCrippled()
    {
        var limbs = new VatsLimbs();
        limbs.Add("ref1", 3, 1000f);
        Assert.False(limbs.Crippled("ref1", 3, 0f));
        Assert.Equal(1.0, limbs.Fraction("ref1", 3, 0f));
    }

    [Fact]
    public void ActorIdsMatchIgnoringCaseAndForgetClearsThem()
    {
        var limbs = new VatsLimbs();
        limbs.Add("Ref:AB", 1, 4f);
        Assert.Equal(4f, limbs.Taken("ref:ab", 1));
        limbs.Forget("REF:AB");
        Assert.Equal(0f, limbs.Taken("ref:ab", 1));
    }

    [Theory]
    [InlineData(50f, 100f, 0.5)]
    [InlineData(150f, 100f, 1.0)]
    [InlineData(null, 100f, 0.0)]
    [InlineData(10f, null, 0.0)]
    public void HealthFractionIsClampedAndNeedsBothValues(float? health, float? max, double expected)
    {
        Assert.Equal(expected, VatsLimbs.HealthFraction(health, max), 6);
    }

    [Fact]
    public void NonPositiveDamageIsIgnored()
    {
        var limbs = new VatsLimbs();
        limbs.Add("a", 1, -5f);
        limbs.Add("a", 1, float.NaN);
        Assert.Equal(0f, limbs.Taken("a", 1));
    }
}
