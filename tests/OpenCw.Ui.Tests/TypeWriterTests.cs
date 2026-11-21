using System;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TypeWriterTests
{
    [Fact]
    public void Default_rate_is_sixty_characters_per_second()
    {
        var writer = new TypeWriter();

        Assert.Equal(60, writer.CharsPerSecond);
        Assert.Equal(60, writer.Advance(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_rate_that_is_not_a_positive_finite_number_is_rejected(double rate)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new TypeWriter(rate));

        Assert.Equal("charsPerSecond", error.ParamName);
    }

    [Fact]
    public void A_whole_number_of_characters_is_released_for_the_elapsed_time()
    {
        var writer = new TypeWriter(10);

        Assert.Equal(5, writer.Advance(0.5));
        Assert.Equal(20, writer.Advance(2));
    }

    [Fact]
    public void Fractions_of_a_character_carry_over_to_the_next_advance()
    {
        var writer = new TypeWriter(10);

        Assert.Equal(2, writer.Advance(0.25));
        Assert.Equal(3, writer.Advance(0.25));
    }

    [Fact]
    public void Small_steps_add_up_to_a_character_only_once_enough_time_has_passed()
    {
        var writer = new TypeWriter(4);

        Assert.Equal(0, writer.Advance(0.1));
        Assert.Equal(0, writer.Advance(0.1));
        Assert.Equal(1, writer.Advance(0.1));
        Assert.Equal(0, writer.Advance(0.1));
    }

    [Fact]
    public void A_product_that_lands_a_hair_under_a_whole_number_still_releases_that_whole_number()
    {
        var writer = new TypeWriter(100);

        Assert.Equal(29, writer.Advance(0.29));
        Assert.Equal(0, writer.Advance(0.001));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Elapsed_time_that_is_not_positive_and_finite_releases_nothing_and_keeps_the_carry(double seconds)
    {
        var writer = new TypeWriter(10);
        Assert.Equal(2, writer.Advance(0.25));

        Assert.Equal(0, writer.Advance(seconds));

        Assert.Equal(1, writer.Advance(0.05));
    }

    [Fact]
    public void Reset_drops_the_fractional_carry()
    {
        var writer = new TypeWriter(10);
        Assert.Equal(2, writer.Advance(0.25));

        writer.Reset();

        Assert.Equal(0, writer.Advance(0.05));
    }
}
