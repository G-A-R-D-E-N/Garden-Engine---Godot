using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ExplosionBlastTests
{
    private static readonly ExplosionInfo Blast = new(100f, 0f, 100f, 300f, 0);

    [Theory]
    [InlineData(0f, 100f)]
    [InlineData(100f, 100f)]
    [InlineData(200f, 50f)]
    [InlineData(300f, 0f)]
    [InlineData(450f, 0f)]
    public void DamageFallsLinearlyBetweenTheRadii(float distance, float expected) =>
        Assert.Equal(expected, ExplosionBlast.DamageAt(Blast, distance), 3);

    [Fact]
    public void ARadiusPairWithOuterBelowInnerDamagesInsideTheInnerOnly()
    {
        var odd = new ExplosionInfo(40f, 0f, 200f, 100f, 0);
        Assert.Equal(40f, ExplosionBlast.DamageAt(odd, 150f));
        Assert.Equal(0f, ExplosionBlast.DamageAt(odd, 201f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    public void NoDamageOrANonFiniteReadingDoesNothing(float damage) =>
        Assert.Equal(0f, ExplosionBlast.DamageAt(new ExplosionInfo(damage, 0f, 100f, 300f, 0), 10f));

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ABadDistanceDoesNothing(float distance) => Assert.Equal(0f, ExplosionBlast.DamageAt(Blast, distance));
}
