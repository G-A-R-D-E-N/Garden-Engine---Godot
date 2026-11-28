using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsHudFeedTests
{
    static FakeVatsTargets Fake()
    {
        var fake = new FakeVatsTargets { Critical = 0.5, CriticalReady = true, InitialTargetId = "A" };
        fake.Targets.Add(new VatsTarget("A", "A", 10, 100, true, new List<VatsResistance>(),
            new[] { new VatsPart("torso", "torso", 70, 100, false, 15, new VatsPoint(960, 500)) }));
        return fake;
    }

    [Fact]
    public void Open_model_feeds_the_preview_and_the_crit_meter()
    {
        var model = new VatsMenuModel(Fake());
        var instruments = new HudInstruments();
        model.Begin();
        VatsHudFeed.Attach(model, instruments);
        Assert.NotNull(instruments.ActionPointPreview);
        Assert.True(instruments.CriticalVisible);
    }

    [Fact]
    public void Closing_clears_the_preview_and_crit_meter_even_though_Changed_fires_after_Closed()
    {
        var model = new VatsMenuModel(Fake());
        var instruments = new HudInstruments();
        model.Begin();
        VatsHudFeed.Attach(model, instruments);
        model.Cancel();
        Assert.Equal(VatsPhase.Closed, model.Phase);
        Assert.Null(instruments.ActionPointPreview);
        Assert.False(instruments.CriticalVisible);
    }

    [Fact]
    public void Attaching_to_an_already_closed_model_leaves_the_hud_clear()
    {
        var model = new VatsMenuModel(Fake());
        var instruments = new HudInstruments();
        model.Begin();
        model.Cancel();
        VatsHudFeed.Attach(model, instruments);
        Assert.Null(instruments.ActionPointPreview);
        Assert.False(instruments.CriticalVisible);
    }
}
