using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyStatusWiringTests
{
    [Fact]
    public void The_sample_status_feeds_every_footer_tab()
    {
        var status = new SamplePlayerStatus();

        Assert.Equal(new[] { "HP 62/100", "LEVEL 7 50%", "AP 70/90" }, PipBoyFooter.Segments(PipBoyTab.Stats, status));
        Assert.Equal(new[] { "WT 64/100", "CAPS 215" }, PipBoyFooter.Segments(PipBoyTab.Inventory, status));
        Assert.Equal(new[] { "03.14.2026", "3:09 PM" }, PipBoyFooter.Segments(PipBoyTab.Data, status));
        Assert.Equal(new[] { "03.14.2026", "3:09 PM", "Town B" }, PipBoyFooter.Segments(PipBoyTab.Map, status));
        Assert.Empty(PipBoyFooter.Segments(PipBoyTab.Radio, status));
    }

    [Fact]
    public void The_status_tab_model_built_from_the_sample_is_the_stats_source_with_rows()
    {
        var model = GallerySamples.NewStatus(new SamplePlayerStatus());

        Assert.Equal(PipBoyTab.Stats, model.Tab);
        Assert.Equal("Character A", model.Heading);
        Assert.Equal("limb:head", model.CurrentKey);
        Assert.Equal(new[] { "STATUS", "SPECIAL", "PERKS" }, model.SubTabLabels);

        model.SetSubTab(StatusSubTab.Perks);

        Assert.Equal(string.Empty, model.EmptyMessage);
        Assert.Equal("perk:perk_a", model.CurrentKey);
    }
}
