using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyTabPlanTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Samples_plan_has_every_tab_whatever_data_exists(bool hasInventory, bool hasQuestLog)
    {
        var tabs = PipBoyTabPlan.Tabs(true, hasInventory, hasQuestLog);

        Assert.Equal(new[] { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio }, tabs);
        Assert.Equal(PipBoyTab.Stats, PipBoyTabPlan.Start(tabs));
    }

    [Fact]
    public void Without_samples_and_without_data_there_are_no_tabs_and_no_start()
    {
        var tabs = PipBoyTabPlan.Tabs(false, false, false);

        Assert.Empty(tabs);
        Assert.Null(PipBoyTabPlan.Start(tabs));
    }

    [Fact]
    public void Without_samples_inventory_only_gives_inventory()
    {
        var tabs = PipBoyTabPlan.Tabs(false, true, false);

        Assert.Equal(new[] { PipBoyTab.Inventory }, tabs);
        Assert.Equal(PipBoyTab.Inventory, PipBoyTabPlan.Start(tabs));
    }

    [Fact]
    public void Without_samples_quest_log_only_gives_data_and_starts_there()
    {
        var tabs = PipBoyTabPlan.Tabs(false, false, true);

        Assert.Equal(new[] { PipBoyTab.Data }, tabs);
        Assert.Equal(PipBoyTab.Data, PipBoyTabPlan.Start(tabs));
    }

    [Fact]
    public void Without_samples_both_feeds_give_inventory_then_data_and_never_sample_tabs()
    {
        var tabs = PipBoyTabPlan.Tabs(false, true, true);

        Assert.Equal(new[] { PipBoyTab.Inventory, PipBoyTab.Data }, tabs);
        Assert.Equal(PipBoyTab.Inventory, PipBoyTabPlan.Start(tabs));
        Assert.DoesNotContain(PipBoyTab.Stats, tabs);
        Assert.DoesNotContain(PipBoyTab.Map, tabs);
        Assert.DoesNotContain(PipBoyTab.Radio, tabs);
    }

    [Fact]
    public void Header_lists_only_built_tabs_in_tab_order()
    {
        Assert.Equal(new[] { PipBoyTab.Inventory, PipBoyTab.Data }, PipBoyTabPlan.Header(new[] { PipBoyTab.Data, PipBoyTab.Inventory }));
        Assert.Equal(new[] { PipBoyTab.Inventory }, PipBoyTabPlan.Header(new[] { PipBoyTab.Inventory, PipBoyTab.Inventory }));
        Assert.Empty(PipBoyTabPlan.Header(System.Array.Empty<PipBoyTab>()));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void Header_of_a_plan_is_the_plan_with_nothing_greyed_in(bool samples, bool hasInventory, bool hasQuestLog)
    {
        var plan = PipBoyTabPlan.Tabs(samples, hasInventory, hasQuestLog);

        Assert.Equal(plan.OrderBy(tab => tab), PipBoyTabPlan.Header(plan));
    }

    // Constraint: detects a live plan that drops the Radio tab when a feed exists, or one that shows it with no feed.
    [Theory]
    [InlineData(false, false, false, false, new PipBoyTab[0])]
    [InlineData(false, false, false, true, new[] { PipBoyTab.Radio })]
    [InlineData(false, true, true, true, new[] { PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Radio })]
    [InlineData(false, true, false, false, new[] { PipBoyTab.Inventory })]
    public void Without_samples_the_radio_tab_exists_exactly_when_a_radio_feed_does(bool samples, bool hasInventory, bool hasQuestLog, bool hasRadio, PipBoyTab[] expected)
    {
        var tabs = PipBoyTabPlan.Tabs(samples, hasInventory, hasQuestLog, hasRadio);

        Assert.Equal(expected, tabs);
        Assert.Equal(expected.Length > 0 ? expected[0] : (PipBoyTab?)null, PipBoyTabPlan.Start(tabs));
    }

    // Constraint: detects a live plan that drops the Map tab when a map feed exists, shows it with no feed, or orders it outside Inventory, Data, Map, Radio.
    [Theory]
    [InlineData(false, false, false, false, true, new[] { PipBoyTab.Map })]
    [InlineData(false, true, true, true, true, new[] { PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio })]
    [InlineData(false, false, true, false, true, new[] { PipBoyTab.Data, PipBoyTab.Map })]
    [InlineData(false, true, true, true, false, new[] { PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Radio })]
    public void Without_samples_the_map_tab_exists_exactly_when_a_map_feed_does(bool samples, bool hasInventory, bool hasQuestLog, bool hasRadio, bool hasMap, PipBoyTab[] expected)
    {
        var tabs = PipBoyTabPlan.Tabs(samples, hasInventory, hasQuestLog, hasRadio, hasMap);

        Assert.Equal(expected, tabs);
        Assert.Equal(tabs.OrderBy(tab => tab), PipBoyTabPlan.Header(tabs));
    }

    // Constraint: detects a live plan that drops the Stats tab when status data exists, shows it with none, or orders it after another tab.
    [Theory]
    [InlineData(false, false, false, false, false, true, new[] { PipBoyTab.Stats })]
    [InlineData(false, true, true, true, true, true, new[] { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio })]
    [InlineData(false, true, false, false, false, true, new[] { PipBoyTab.Stats, PipBoyTab.Inventory })]
    [InlineData(false, true, true, true, true, false, new[] { PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio })]
    [InlineData(true, false, false, false, false, false, new[] { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio })]
    public void The_stats_tab_exists_exactly_when_status_data_does(bool samples, bool hasInventory, bool hasQuestLog, bool hasRadio, bool hasMap, bool hasStats, PipBoyTab[] expected)
    {
        var tabs = PipBoyTabPlan.Tabs(samples, hasInventory, hasQuestLog, hasRadio, hasMap, hasStats);

        Assert.Equal(expected, tabs);
        Assert.Equal(PipBoyTabPlan.Header(tabs), tabs);
        Assert.Equal(expected[0], PipBoyTabPlan.Start(tabs));
    }
}
