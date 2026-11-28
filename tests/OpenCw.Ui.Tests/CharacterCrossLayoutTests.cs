using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterCrossLayoutTests
{
    static FavouritesCrossModel Open(CrossMode mode, params int[] filled)
    {
        var slots = Enumerable.Range(1, FavouritesCrossModel.SlotCount)
            .Select(s => filled.Contains(s) ? new FavoriteEntry("Item " + s) : null).ToArray();
        return FavouritesCrossModel.TryOpen(mode, slots, false, false)!;
    }

    [Theory]
    [InlineData(3, -1, 0)]
    [InlineData(1, -3, 0)]
    [InlineData(4, 1, 0)]
    [InlineData(6, 3, 0)]
    [InlineData(9, 0, -1)]
    [InlineData(7, 0, -3)]
    [InlineData(10, 0, 1)]
    [InlineData(12, 0, 3)]
    public void Slots_sit_on_the_four_arms_with_ring_one_next_to_the_centre(int slot, int x, int y)
    {
        var at = CrossLayout.Offset(slot, 10);

        Assert.Equal(x * 10.0, at.X);
        Assert.Equal(y * 10.0, at.Y);
    }

    [Fact]
    public void An_out_of_range_slot_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossLayout.Offset(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossLayout.Offset(13, 10));
    }

    [Fact]
    public void A_point_hits_the_nearest_visible_slot_within_the_radius_and_nothing_outside_it()
    {
        var model = Open(CrossMode.Assign);

        Assert.Equal(4, CrossLayout.Hit(model, 11, 2, 10, 6));
        Assert.Equal(9, CrossLayout.Hit(model, 0, -9, 10, 6));
        Assert.Null(CrossLayout.Hit(model, 40, 40, 10, 6));
        Assert.Null(CrossLayout.Hit(model, 0, 0, 10, 6));
        Assert.Null(CrossLayout.Hit(model, double.NaN, 0, 10, 6));
        Assert.Null(CrossLayout.Hit(model, 10, 0, 10, 0));
    }

    [Fact]
    public void A_quick_menu_skips_hidden_slots_when_hit_testing()
    {
        var model = Open(CrossMode.Quick, 3);

        Assert.True(model.IsVisible(3));
        Assert.False(model.IsVisible(1));
        Assert.Null(CrossLayout.Hit(model, -30, 0, 10, 6));
        Assert.Equal(3, CrossLayout.Hit(model, -10, 0, 10, 6));
    }
}
