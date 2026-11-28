using System.Linq;
using OpenCommonwealth.Services.Ui.Credits;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CreditsRollTests
{
    const double Area = 1000.0;

    static CreditsRoll Make(params string[] lines) => new(lines, _ => 24.0, Area);

    [Fact]
    public void Content_moves_seventy_pixels_per_second_regardless_of_step_size()
    {
        var one = Make("A", "B");
        one.Advance(1.0);

        var many = Make("A", "B");
        for (var i = 0; i < 60; i++) many.Advance(1.0 / 60.0);

        Assert.Equal(70.0, one.Position, 6);
        Assert.Equal(70.0, many.Position, 6);
    }

    [Fact]
    public void The_first_line_starts_at_the_bottom_of_the_area_and_is_not_visible_until_it_enters()
    {
        var roll = Make("A", "B");

        Assert.Equal(Area, roll.Lines[0].Top);
        Assert.False(roll.Lines[0].Visible);

        roll.Advance(0.1);

        Assert.Equal(Area - 7.0, roll.Lines[0].Top, 6);
        Assert.True(roll.Lines[0].Visible);
    }

    [Fact]
    public void Opacity_is_full_sixty_pixels_above_the_fade_start_zero_at_it_and_linear_between()
    {
        var roll = Make("A");
        var fadeStart = Area - CreditsRoll.FadeInset;

        Assert.Equal(1.0, roll.OpacityOf(fadeStart - 60), 6);
        Assert.Equal(0.0, roll.OpacityOf(fadeStart), 6);
        Assert.Equal(0.5, roll.OpacityOf(fadeStart - 30), 6);
        Assert.Equal(1.0, roll.OpacityOf(fadeStart - 200), 6);
        Assert.Equal(0.0, roll.OpacityOf(fadeStart + 40), 6);
    }

    [Fact]
    public void A_line_wholly_above_the_top_is_not_visible()
    {
        var roll = Make("A", "B");
        roll.Advance(1060.0 / 70.0);

        Assert.False(roll.Lines[0].Visible);
        Assert.True(roll.Lines[1].Visible);
    }

    [Fact]
    public void Wheel_down_advances_thirty_per_notch_and_wheel_up_rewinds_but_never_past_the_start()
    {
        var roll = Make("A");

        roll.Wheel(-1);
        Assert.Equal(0.0, roll.Position);
        roll.Wheel(1);
        Assert.Equal(30.0, roll.Position);
        roll.Wheel(-1);
        Assert.Equal(0.0, roll.Position);
        roll.Wheel(-5);
        Assert.Equal(0.0, roll.Position);
        roll.Wheel(2, 2.0);
        Assert.Equal(120.0, roll.Position);
    }

    [Fact]
    public void The_roll_ends_when_the_last_line_bottom_reaches_minus_twenty_five_and_stops_advancing()
    {
        var roll = Make("A", "", "B");
        var total = 24.0 + CreditsRoll.BlankHeight + 24.0;
        var endAt = Area + total + 25.0;

        roll.Advance((endAt - 1.0) / 70.0);
        Assert.False(roll.IsFinished);

        roll.Advance(1.0);
        Assert.True(roll.IsFinished);

        var position = roll.Position;
        roll.Advance(5.0);
        Assert.Equal(position, roll.Position);
    }

    [Fact]
    public void Restart_resets_the_position_and_the_finished_state()
    {
        var roll = Make("A");
        roll.Advance(100.0);
        Assert.True(roll.IsFinished);

        roll.Restart();

        Assert.False(roll.IsFinished);
        Assert.Equal(0.0, roll.Position);
    }

    [Fact]
    public void A_blank_line_is_fifteen_pixels_and_is_not_measured()
    {
        var measured = 0;
        var roll = new CreditsRoll(new[] { "A", string.Empty, "B" }, text =>
        {
            measured++;
            return 24.0;
        }, Area);

        Assert.Equal(2, measured);
        Assert.Equal(new[] { 24.0, 15.0, 24.0 }, roll.Lines.Select(line => line.Height).ToArray());
        Assert.Equal(Area + 24.0, roll.Lines[1].Top, 6);
    }

    [Fact]
    public void A_measure_that_returns_nonsense_falls_back_to_the_blank_height()
    {
        var roll = new CreditsRoll(new[] { "A" }, _ => double.NaN, Area);

        Assert.Equal(CreditsRoll.BlankHeight, roll.Lines[0].Height);
    }

    [Fact]
    public void Resizing_keeps_the_scroll_position_in_design_units()
    {
        var roll = Make("A");
        roll.Advance(2.0);

        roll.AreaHeight = 500.0;

        Assert.Equal(140.0, roll.Position, 6);
        Assert.Equal(500.0 - 140.0, roll.Lines[0].Top, 6);
    }

    [Fact]
    public void An_empty_roll_is_finished_at_once()
    {
        Assert.True(Make().IsFinished);
    }

    [Fact]
    public void Non_finite_time_or_wheel_input_is_ignored()
    {
        var roll = Make("A");

        roll.Advance(double.NaN);
        roll.Advance(-1.0);
        roll.Wheel(double.PositiveInfinity);

        Assert.Equal(0.0, roll.Position);
    }
}
