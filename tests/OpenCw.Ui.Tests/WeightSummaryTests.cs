using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WeightSummaryTests
{
    [Fact]
    public void Remaining_is_capacity_minus_carried_and_goes_negative_when_over()
    {
        Assert.Equal(75.0, new WeightSummary(25, 100).Remaining);
        Assert.Equal(0.0, new WeightSummary(100, 100).Remaining);
        Assert.Equal(-20.0, new WeightSummary(120, 100).Remaining);
    }

    [Fact]
    public void Carrying_exactly_the_capacity_is_not_over_encumbered()
    {
        Assert.False(new WeightSummary(100, 100).IsOverEncumbered);
        Assert.True(new WeightSummary(100.5, 100).IsOverEncumbered);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_capacity_of_zero_or_less_is_never_over_encumbered(double capacity)
    {
        var summary = new WeightSummary(50, capacity);

        Assert.False(summary.IsOverEncumbered);
        Assert.Equal(capacity - 50, summary.Remaining);
    }

    [Fact]
    public void An_empty_model_with_no_capacity_carries_nothing()
    {
        var model = new InventoryModel(Array.Empty<InventoryItem>());

        Assert.Equal(new WeightSummary(0, 0), model.Burden);
        Assert.False(model.Burden.IsOverEncumbered);
    }

    [Fact]
    public void Compare_lists_item_stats_first_then_weight_and_value_with_lower_weight_counting_as_better()
    {
        var left = new InventoryItem("a", "A", "Misc", 10, 5);
        var right = new InventoryItem("b", "B", "Misc", 4, 9);

        var view = InventoryModel.Compare(left, right);

        Assert.Equal(new[] { "Weight", "Value" }, view.Rows.Select(r => r.Name));
        Assert.Equal(new StatComparison("Weight", 10, 4, false), view.Rows[0]);
        Assert.True(view.Rows[0].RightIsBetter);
        Assert.Equal(-6.0, view.Rows[0].Delta);
        Assert.Equal(new StatComparison("Value", 5, 9, true), view.Rows[1]);
    }

    [Fact]
    public void A_stat_named_on_both_sides_is_listed_once_with_the_left_direction_and_a_missing_side_reads_zero()
    {
        var left = new InventoryItem("a", "A", "Misc", 1, 1, Stats: new[] { new ItemStat("Dmg", 3, true) });
        var right = new InventoryItem("b", "B", "Misc", 1, 1,
            Stats: new[] { new ItemStat("Dmg", 7, false), new ItemStat("Spread", 2, false) });

        var view = InventoryModel.Compare(left, right);

        Assert.Equal(new[] { "Dmg", "Spread", "Weight", "Value" }, view.Rows.Select(r => r.Name));
        Assert.Equal(new StatComparison("Dmg", 3, 7, true), view.Rows[0]);
        Assert.Equal(new StatComparison("Spread", 0, 2, false), view.Rows[1]);
        Assert.True(view.Rows[1].RightIsWorse);
    }
}
