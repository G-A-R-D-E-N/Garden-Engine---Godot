using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

[Collection("PlayerHealth")]
public sealed class PlayerDamageTests : IDisposable
{
    public PlayerDamageTests() => PlayerHealth.Attach(100, 100, 90, 90);

    public void Dispose()
    {
        PlayerDamage.ArmorRating = null;
        PlayerHealth.Detach();
    }

    [Fact]
    public void Worn_armor_reduces_physical_damage_but_not_unmitigated_damage()
    {
        PlayerDamage.ArmorRating = () => 40;

        PlayerDamage.Take(10, DamageKind.Physical);
        PlayerDamage.Take(10, DamageKind.Unmitigated);

        Assert.Equal(84, PlayerHealth.Current!.Health);
    }

    [Fact]
    public void The_rating_path_is_the_armor_row_and_a_rating_reads_as_a_positive_number()
    {
        Assert.Equal("ARMO\\ArmorRating", ArmorMitigation.RatingPath);
        Assert.Equal((12.0, 0.0, 0.0), (ArmorMitigation.RatingOf("12"), ArmorMitigation.RatingOf("-3"), ArmorMitigation.RatingOf(null)));
    }

    [Fact]
    public void Without_armor_the_whole_physical_damage_lands()
    {
        PlayerDamage.Take(10, DamageKind.Physical);

        Assert.Equal(90, PlayerHealth.Current!.Health);
    }

    [Fact]
    public void A_huge_rating_is_capped_so_some_damage_always_lands()
    {
        Assert.Equal(1.5, ArmorMitigation.Reduced(10, 5000), 6);
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("-3", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("x", 0)]
    public void An_unreadable_rating_counts_as_zero(string? raw, double expected) =>
        Assert.Equal(expected, ArmorMitigation.RatingOf(raw));

    [Fact]
    public void A_fall_below_the_safe_height_does_nothing_and_one_above_it_costs_health_per_metre()
    {
        var gravity = 9.8;
        var speedFor = (double meters) => System.Math.Sqrt(2 * gravity * meters);

        Assert.Equal(0, FallDamage.Amount(speedFor(3.9), gravity, false, false), 6);
        Assert.Equal(20, FallDamage.Amount(speedFor(6), gravity, false, false), 6);
    }

    [Fact]
    public void Power_armor_and_water_take_no_fall_damage_and_bad_input_is_ignored()
    {
        Assert.Equal(0, FallDamage.Amount(30, 9.8, true, false));
        Assert.Equal(0, FallDamage.Amount(30, 9.8, false, true));
        Assert.Equal(0, FallDamage.Amount(double.NaN, 9.8, false, false));
        Assert.Equal(0, FallDamage.Amount(30, 0, false, false));
    }
}
