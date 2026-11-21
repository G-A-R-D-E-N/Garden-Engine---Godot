using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class PlayerDeathTests
{
    [Fact]
    public void ZeroHealthRaisesDiedOnceAndMarksTheVitalsDead()
    {
        var vitals = new PlayerVitals(100, 30, 50, 50);
        var deaths = 0;
        vitals.Died += () => deaths++;

        vitals.AddHealth(-10);
        Assert.False(vitals.IsDead);
        vitals.AddHealth(-50);
        vitals.AddHealth(-5);

        Assert.True(vitals.IsDead);
        Assert.Equal(1, deaths);
    }

    [Fact]
    public void TheStaticDiedEventFollowsTheAttachedVitalsAndAHealedPlayerCanDieAgain()
    {
        var vitals = PlayerHealth.Attach(100, 20, 50, 50)!;
        var deaths = 0;
        void Count() => deaths++;
        PlayerHealth.Died += Count;
        try
        {
            vitals.AddHealth(-20);
            vitals.AddHealth(100);
            vitals.AddHealth(-100);
            Assert.Equal(2, deaths);
        }
        finally
        {
            PlayerHealth.Died -= Count;
            PlayerHealth.Detach();
        }
    }
}
