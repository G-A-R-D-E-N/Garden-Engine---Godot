using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class ActionPointPoolTests
{
    [Fact]
    public void WithoutVitalsTheValueIsLocalAndClampedToTheExportedMaximum()
    {
        var pool = new ActionPointPool(() => null, () => 100);
        pool.Value = 250;
        Assert.Equal(100, pool.Value);
        pool.Value -= 30;
        Assert.Equal(70, pool.Value);
    }

    [Fact]
    public void RestoringPointsAfterSprintingRaisesTheValueSprintAndVatsSpend()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        var pool = new ActionPointPool(() => vitals, () => 100);
        pool.Value -= 60;
        Assert.Equal(30, vitals.ActionPoints);
        vitals.AddActionPoints(25);
        Assert.Equal(55, pool.Value);
        Assert.Equal(90, pool.Max);
    }

    [Fact]
    public void AnActionPointMaximumBuffRaisesTheCeilingTheControllerRecoversTo()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        var pool = new ActionPointPool(() => vitals, () => 100);
        vitals.AddBuff(VitalsEffects.ActionPointsValue, 20, 300);
        pool.Value = 500;
        Assert.Equal(110, pool.Value);
    }
}
