using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class Garden121WiringTests
{
    [Fact]
    public void Live_status_plan_uses_only_available_feeds()
    {
        var tabs = PipBoyTabPlan.Tabs(false, true, false, hasStats: true);

        Assert.Equal(new[] { PipBoyTab.Stats, PipBoyTab.Inventory }, tabs);
        Assert.Equal(PipBoyTab.Stats, PipBoyTabPlan.Start(tabs));
    }

    [Fact]
    public void Pause_flow_keeps_live_tabs_when_status_is_present_even_with_samples_enabled()
    {
        const bool samplesEnabled = true;
        const bool statusMissing = false;
        var tabs = PipBoyTabPlan.Tabs(samplesEnabled && statusMissing, hasInventory: true, hasQuestLog: false, hasStats: true);

        Assert.Equal(new[] { PipBoyTab.Stats, PipBoyTab.Inventory }, tabs);
    }

    [Fact]
    public void Gallery_samples_still_fill_in_when_nothing_live_is_available()
    {
        var tabs = PipBoyTabPlan.Tabs(samples: true, hasInventory: false, hasQuestLog: false, hasStats: false);

        Assert.Equal(5, tabs.Count);
        Assert.Equal(PipBoyTab.Stats, tabs[0]);
    }

    [Fact]
    public void Melee_vats_keeps_the_interaction_default_while_ranged_weapons_widen_range()
    {
        const float activate = 2.5f;
        Assert.Equal(activate, VatsActivationRange.Meters(0, activate));
        Assert.True(VatsActivationRange.Meters(4096, activate) > activate);
    }
}