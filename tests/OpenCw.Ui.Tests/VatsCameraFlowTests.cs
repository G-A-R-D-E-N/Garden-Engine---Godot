using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class VatsCameraFlowTests
{
    [Fact]
    public void ARefusedMenuNeverEntersTheCamera()
    {
        var log = new List<string>();
        Assert.False(VatsCameraFlow.Open(_ => false, () => log.Add("enter"), () => log.Add("exit")));
        Assert.Empty(log);
    }

    [Fact]
    public void AnOpenedMenuEntersTheCameraAndItsOutcomeLeavesItOnce()
    {
        var log = new List<string>();
        Action<VatsOutcome>? finish = null;
        Assert.True(VatsCameraFlow.Open(f => { finish = f; return true; }, () => log.Add("enter"), () => log.Add("exit")));
        Assert.Equal(["enter"], log);
        finish!(VatsOutcome.Executed);
        finish(VatsOutcome.Cancelled);
        Assert.Equal(["enter", "exit"], log);
    }

    [Fact]
    public void AnOutcomeReportedBeforeLaunchReturnsNeverEntersTheCamera()
    {
        var log = new List<string>();
        Assert.True(VatsCameraFlow.Open(f => { f(VatsOutcome.Cancelled); return true; }, () => log.Add("enter"), () => log.Add("exit")));
        Assert.Empty(log);
    }

    [Fact]
    public void AClaimedPauseSurvivesAnotherOwnerReleasing()
    {
        var claims = new PauseClaims();
        var menus = new object();
        var death = new object();
        claims.Set(menus, true);
        claims.Set(death, true);
        claims.Set(death, false);
        Assert.True(claims.Paused);
        claims.Set(menus, false);
        Assert.False(claims.Paused);
        Assert.False(claims.Set(death, false));
    }
}
