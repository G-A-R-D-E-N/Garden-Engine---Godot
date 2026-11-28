using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompareViewTests
{
    sealed record Part(double Damage, double Weight);

    static readonly CompareStat<Part>[] Stats =
    {
        new CompareStat<Part>("Damage", p => p.Damage, true),
        new CompareStat<Part>("Weight", p => p.Weight, false),
    };

    [Fact]
    public void Lower_is_better_stat_marks_the_lighter_side_as_better_and_the_heavier_as_worse()
    {
        var view = new CompareView<Part>(new Part(10, 3), new Part(10, 5), Stats);

        var weight = view.Rows[1];
        Assert.Equal("Weight", weight.Name);
        Assert.Equal(2.0, weight.Delta);
        Assert.False(weight.RightIsBetter);
        Assert.True(weight.RightIsWorse);
    }

    [Fact]
    public void Higher_is_better_stat_marks_the_larger_side_and_a_tie_marks_neither_side()
    {
        var view = new CompareView<Part>(new Part(10, 3), new Part(12, 3), Stats);

        var damage = view.Rows[0];
        Assert.Equal(2.0, damage.Delta);
        Assert.True(damage.RightIsBetter);

        var weight = view.Rows[1];
        Assert.Equal(0.0, weight.Delta);
        Assert.False(weight.RightIsBetter);
        Assert.False(weight.RightIsWorse);
    }
}
