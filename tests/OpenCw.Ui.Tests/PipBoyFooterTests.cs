using System;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyFooterTests
{
    sealed class FakeStatus : IPipBoyStatusSource
    {
        public double? Health { get; set; }

        public double? MaxHealth { get; set; }

        public int? Level { get; set; }

        public double? LevelProgress { get; set; }

        public double? ActionPoints { get; set; }

        public double? MaxActionPoints { get; set; }

        public double? CarryWeight { get; set; }

        public double? CarryCapacity { get; set; }

        public long? Caps { get; set; }

        public DateTime? WorldTime { get; set; }

        public string? Location { get; set; }
    }

    [Fact]
    public void Stats_bar_floors_values_and_shows_level_percentage()
    {
        var source = new FakeStatus
        {
            Health = 87.9, MaxHealth = 120.4, Level = 7, LevelProgress = 0.425, ActionPoints = 59.9, MaxActionPoints = 60,
        };

        Assert.Equal(new[] { "HP 87/120", "LEVEL 7 42%", "AP 59/60" }, PipBoyFooter.Segments(PipBoyTab.Stats, source));
        Assert.Equal("HP 87/120    LEVEL 7 42%    AP 59/60", PipBoyFooter.Text(PipBoyTab.Stats, source));
    }

    [Fact]
    public void Stats_bar_never_shows_negative_health_and_prints_dashes_for_unknown_values()
    {
        var source = new FakeStatus { Health = -4, MaxHealth = null, Level = null };

        Assert.Equal(new[] { "HP 0/--", "LEVEL --", "AP --/--" }, PipBoyFooter.Segments(PipBoyTab.Stats, source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void Action_points_show_dashes_when_the_maximum_is_missing_or_zero(double? max)
    {
        var source = new FakeStatus { ActionPoints = 30, MaxActionPoints = max };

        Assert.Equal("AP --/--", PipBoyFooter.Segments(PipBoyTab.Stats, source)[2]);
    }

    [Fact]
    public void Inventory_bar_shows_weight_with_capacity_and_caps()
    {
        var source = new FakeStatus { CarryWeight = 140.7, CarryCapacity = 200, Caps = 1234 };

        Assert.Equal(new[] { "WT 140/200", "CAPS 1234" }, PipBoyFooter.Segments(PipBoyTab.Inventory, source));
    }

    [Fact]
    public void Inventory_bar_drops_the_capacity_when_unknown_and_caps_out_at_one_million()
    {
        var source = new FakeStatus { CarryWeight = 12, CarryCapacity = null, Caps = 1_000_000 };

        Assert.Equal(new[] { "WT 12", "CAPS 1000000+" }, PipBoyFooter.Segments(PipBoyTab.Inventory, source));

        source.Caps = 999_999;
        Assert.Equal("CAPS 999999", PipBoyFooter.Segments(PipBoyTab.Inventory, source)[1]);

        source.Caps = null;
        source.CarryWeight = null;
        Assert.Equal(new[] { "WT --", "CAPS --" }, PipBoyFooter.Segments(PipBoyTab.Inventory, source));
    }

    [Theory]
    [InlineData(0, 5, "12:05 AM")]
    [InlineData(9, 30, "9:30 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(23, 59, "11:59 PM")]
    public void Data_bar_uses_a_twelve_hour_clock(int hour, int minute, string expected)
    {
        var source = new FakeStatus { WorldTime = new DateTime(2287, 10, 23, hour, minute, 0) };

        Assert.Equal(new[] { "10.23.2287", expected }, PipBoyFooter.Segments(PipBoyTab.Data, source));
    }

    [Fact]
    public void Map_bar_adds_the_location_only_when_one_is_known()
    {
        var source = new FakeStatus { WorldTime = new DateTime(2287, 1, 2, 15, 4, 0), Location = "Sample Town" };

        Assert.Equal(new[] { "01.02.2287", "3:04 PM", "Sample Town" }, PipBoyFooter.Segments(PipBoyTab.Map, source));

        source.Location = " ";
        Assert.Equal(2, PipBoyFooter.Segments(PipBoyTab.Map, source).Count);
    }

    [Fact]
    public void Time_is_dashes_when_the_world_clock_is_unknown()
    {
        var source = new FakeStatus();

        Assert.Equal(new[] { "--.--.----", "--:-- --" }, PipBoyFooter.Segments(PipBoyTab.Data, source));
    }

    [Fact]
    public void Radio_bar_is_empty_and_a_missing_source_prints_dashes()
    {
        Assert.Empty(PipBoyFooter.Segments(PipBoyTab.Radio, new FakeStatus()));
        Assert.Empty(PipBoyFooter.Segments(PipBoyTab.Radio, null));
        Assert.Equal(new[] { "--" }, PipBoyFooter.Segments(PipBoyTab.Stats, null));
        Assert.Equal(string.Empty, PipBoyFooter.Text(PipBoyTab.Radio, null));
    }

    [Theory]
    [InlineData(0.57, "LEVEL 3 57%")]
    [InlineData(0.29, "LEVEL 3 29%")]
    [InlineData(0.999, "LEVEL 3 99%")]
    [InlineData(1.0, "LEVEL 3 100%")]
    public void Level_percentage_floors_without_losing_a_point_to_float_error(double progress, string expected)
    {
        var source = new FakeStatus { Level = 3, LevelProgress = progress };

        Assert.Equal(expected, PipBoyFooter.Segments(PipBoyTab.Stats, source)[1]);
    }
}
