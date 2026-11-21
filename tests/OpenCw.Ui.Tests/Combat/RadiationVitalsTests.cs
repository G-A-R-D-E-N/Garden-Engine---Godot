using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RadiationVitalsTests
{
    [Fact]
    public void Rads_shrink_the_maximum_health_by_their_fraction_and_clamp_current_health()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);

        vitals.AddRads(250);

        Assert.Equal(75, vitals.MaxHealth);
        Assert.Equal(75, vitals.Health);
        Assert.Equal(0.25, vitals.RadiationFraction);
    }

    [Fact]
    public void Removing_rads_raises_the_maximum_again_but_does_not_heal()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        vitals.AddRads(500);

        vitals.AddRads(-500);

        Assert.Equal(100, vitals.MaxHealth);
        Assert.Equal(50, vitals.Health);
    }

    [Fact]
    public void A_full_radiation_dose_kills_once_and_leaves_no_maximum()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        var deaths = 0;
        vitals.Died += () => deaths++;

        vitals.AddRads(1000);
        vitals.AddRads(500);

        Assert.Equal(0, vitals.MaxHealth);
        Assert.Equal(0, vitals.Health);
        Assert.Equal(1, deaths);
    }

    [Fact]
    public void A_restored_save_clamps_health_to_the_maximum_its_rads_leave()
    {
        var saved = new PlayerVitals(100, 100, 90, 90);
        saved.AddRads(400);
        var snapshot = saved.Capture() with { Health = 100 };
        var live = new PlayerVitals(100, 100, 90, 90);

        live.Restore(snapshot);

        Assert.Equal(60, live.Health);
        Assert.Equal(400, live.Rads);
    }

    [Fact]
    public void The_live_status_reports_the_radiation_fraction()
    {
        var vitals = new PlayerVitals(100, 100, 90, 90);
        vitals.AddRads(100);

        Assert.Equal(0.1, new LivePlayerStatus(vitals: vitals).Radiation);
    }
}
