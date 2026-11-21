using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class BlastMathTests
{
    [Theory]
    [InlineData(0.0, 50.0)]
    [InlineData(-1.0, 50.0)]
    [InlineData(double.NaN, 50.0)]
    [InlineData(80.0, 80.0)]
    public void ABaseDamageFallsBackWhenTheRecordGivesNone(double weapon, double expected)
    {
        Assert.Equal(expected, BlastMath.BaseDamage(weapon));
    }

    [Fact]
    public void DamageFallsLinearlyFromTheCentreToTheRadius()
    {
        Assert.Equal(100.0, BlastMath.Damage(100, 0, 5));
        Assert.Equal(50.0, BlastMath.Damage(100, 2.5, 5), 9);
        Assert.Equal(0.0, BlastMath.Damage(100, 5, 5));
        Assert.Equal(0.0, BlastMath.Damage(100, 9, 5));
    }

    [Fact]
    public void BadInputsDealNothing()
    {
        Assert.Equal(0.0, BlastMath.Damage(0, 1, 5));
        Assert.Equal(0.0, BlastMath.Damage(100, 1, 0));
        Assert.Equal(0.0, BlastMath.Damage(100, double.NaN, 5));
    }

    [Fact]
    public void AMineNeedsToBeArmedAndCloseEnough()
    {
        Assert.False(BlastMath.MineTriggered(BlastMath.MineArmSeconds - 0.1, 0.5));
        Assert.False(BlastMath.MineTriggered(BlastMath.MineArmSeconds, BlastMath.MineTriggerMeters + 0.1));
        Assert.True(BlastMath.MineTriggered(BlastMath.MineArmSeconds, BlastMath.MineTriggerMeters));
    }
}
