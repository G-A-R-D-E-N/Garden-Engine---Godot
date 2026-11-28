using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ProgressionSnapshotTests
{
    static ProgressionSnapshot Make(int level = 1, int points = 0, int attributes = 7) =>
        new(new PerkChartSnapshot(3, level, points, Enumerable.Repeat(5, attributes).ToArray(), Array.Empty<PerkChartEntry>()), 10, 100);

    [Fact]
    public void Max_perk_points_is_255()
    {
        Assert.Equal(255, ProgressionCurve.MaxPerkPoints);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 255, true)]
    [InlineData(-1, 0, false)]
    [InlineData(1, -1, false)]
    [InlineData(1, 256, false)]
    public void Level_and_perk_point_bounds_are_exact(int level, int points, bool valid)
    {
        Assert.Equal(valid, Make(level, points).IsValid);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void Exactly_seven_attributes_are_required(int count, bool valid)
    {
        Assert.Equal(valid, Make(attributes: count).IsValid);
    }

    [Fact]
    public void Level_perk_points_and_revision_read_through_to_the_chart()
    {
        var snapshot = Make(level: 12, points: 4);

        Assert.Equal(3, snapshot.Revision);
        Assert.Equal(12, snapshot.Level);
        Assert.Equal(4, snapshot.PerkPoints);
    }
}
