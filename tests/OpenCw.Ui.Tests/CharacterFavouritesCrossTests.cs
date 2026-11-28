using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterFavouritesCrossTests
{
    static FavoriteEntry?[] Slots(params int[] filled)
    {
        var slots = new FavoriteEntry?[FavouritesCrossModel.SlotCount];
        foreach (var s in filled) slots[s - 1] = new FavoriteEntry($"Item{s}");
        return slots;
    }

    static (FavouritesCrossModel Model, List<string> Sounds) Open(CrossMode mode, params int[] filled)
    {
        var model = FavouritesCrossModel.TryOpen(mode, Slots(filled), false, false)!;
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;
        return (model, sounds);
    }

    static int? Press(FavouritesCrossModel model, MenuInputAction action)
    {
        model.Handle(action);
        return model.Highlighted;
    }

    [Fact]
    public void Open_is_refused_for_bad_input_another_modal_or_a_pause_and_an_empty_quick_cross_plays_the_error()
    {
        var errors = new List<string>();

        Assert.Null(FavouritesCrossModel.TryOpen(CrossMode.Assign, null, false, false));
        Assert.Null(FavouritesCrossModel.TryOpen(CrossMode.Assign, Slots().Take(11).ToArray(), false, false));
        Assert.Null(FavouritesCrossModel.TryOpen(CrossMode.Assign, Slots(1), true, false));
        Assert.Null(FavouritesCrossModel.TryOpen(CrossMode.Assign, Slots(1), false, true));
        Assert.Null(FavouritesCrossModel.TryOpen(CrossMode.Quick, Slots(), false, false, errors.Add));
        Assert.Equal(new[] { "ui.error" }, errors);
        Assert.Equal(CrossMode.Assign, FavouritesCrossModel.TryOpen(CrossMode.Assign, Slots(), false, false)?.Mode);
        Assert.Equal(CrossMode.Quick, FavouritesCrossModel.TryOpen(CrossMode.Quick, Slots(3), false, false)?.Mode);
    }

    [Fact]
    public void Slot_geometry_round_trips_and_the_key_labels_follow_the_number_row()
    {
        for (var slot = 1; slot <= FavouritesCrossModel.SlotCount; slot++)
            Assert.Equal(slot, FavouritesCrossModel.SlotAt(FavouritesCrossModel.ArmOf(slot), FavouritesCrossModel.RingOf(slot)));
        Assert.Equal(new[] { "1", "0", "-", "=" }, new[] { 1, 10, 11, 12 }.Select(FavouritesCrossModel.LabelOf));
        Assert.Equal(3, FavouritesCrossModel.RingOf(1));
        Assert.Equal(1, FavouritesCrossModel.RingOf(4));
        Assert.Equal(1, FavouritesCrossModel.RingOf(9));
        Assert.Equal(1, FavouritesCrossModel.RingOf(10));
    }

    [Theory]
    [InlineData(MenuInputAction.Up, 9)]
    [InlineData(MenuInputAction.Down, 10)]
    [InlineData(MenuInputAction.Left, 3)]
    [InlineData(MenuInputAction.Right, 4)]
    public void The_first_direction_press_lands_on_the_ring_one_slot_of_that_arm(MenuInputAction action, int slot)
    {
        var (model, sounds) = Open(CrossMode.Assign);

        Assert.Null(model.Highlighted);
        Assert.Equal(slot, Press(model, action));
        Assert.Equal(new[] { "ui.focus.1" }, sounds);
    }

    [Theory]
    [InlineData(4, MenuInputAction.Right, 5)]
    [InlineData(5, MenuInputAction.Right, 6)]
    [InlineData(6, MenuInputAction.Right, 6)]
    [InlineData(5, MenuInputAction.Left, 4)]
    [InlineData(4, MenuInputAction.Left, 3)]
    [InlineData(6, MenuInputAction.Up, 9)]
    [InlineData(6, MenuInputAction.Down, 10)]
    [InlineData(3, MenuInputAction.Left, 2)]
    [InlineData(2, MenuInputAction.Left, 1)]
    [InlineData(1, MenuInputAction.Left, 1)]
    [InlineData(1, MenuInputAction.Right, 2)]
    [InlineData(3, MenuInputAction.Right, 4)]
    [InlineData(1, MenuInputAction.Up, 9)]
    [InlineData(9, MenuInputAction.Up, 8)]
    [InlineData(8, MenuInputAction.Up, 7)]
    [InlineData(7, MenuInputAction.Up, 7)]
    [InlineData(7, MenuInputAction.Down, 8)]
    [InlineData(9, MenuInputAction.Down, 10)]
    [InlineData(8, MenuInputAction.Left, 3)]
    [InlineData(8, MenuInputAction.Right, 4)]
    [InlineData(10, MenuInputAction.Down, 11)]
    [InlineData(11, MenuInputAction.Down, 12)]
    [InlineData(12, MenuInputAction.Down, 12)]
    [InlineData(10, MenuInputAction.Up, 9)]
    [InlineData(12, MenuInputAction.Up, 11)]
    [InlineData(12, MenuInputAction.Left, 3)]
    [InlineData(11, MenuInputAction.Right, 4)]
    public void Direction_presses_walk_the_cross_without_wrapping(int from, MenuInputAction action, int expected)
    {
        var (model, _) = Open(CrossMode.Assign);
        model.Select(from);

        Assert.Equal(expected, Press(model, action));
    }

    [Fact]
    public void Moving_out_along_an_arm_plays_the_ring_cue_for_each_ring()
    {
        var (model, sounds) = Open(CrossMode.Assign);

        model.Select(4);
        model.Move(MenuInputAction.Right);
        model.Move(MenuInputAction.Right);
        model.Move(MenuInputAction.Right);

        Assert.Equal(new[] { "ui.focus.1", "ui.focus.2", "ui.focus.3" }, sounds);
    }

    [Fact]
    public void Stick_deflection_picks_the_arm_by_direction_and_the_ring_by_distance()
    {
        var (model, sounds) = Open(CrossMode.Assign);

        Assert.False(model.Stick(0.1, 0.1));
        Assert.False(model.Stick(double.NaN, 1));
        Assert.Null(model.Highlighted);

        Assert.True(model.Stick(0.5, 0));
        Assert.Equal(4, model.Highlighted);
        Assert.True(model.Stick(0.7, 0));
        Assert.Equal(5, model.Highlighted);
        Assert.True(model.Stick(1, 0));
        Assert.Equal(6, model.Highlighted);
        Assert.True(model.Stick(-1, 0));
        Assert.Equal(1, model.Highlighted);
        Assert.True(model.Stick(0, 1));
        Assert.Equal(7, model.Highlighted);
        Assert.True(model.Stick(0, -1));
        Assert.Equal(12, model.Highlighted);
        Assert.Equal(new[] { "ui.focus.1", "ui.focus.2", "ui.focus.3", "ui.focus.3", "ui.focus.3", "ui.focus.3" }, sounds);
    }

    [Fact]
    public void Stick_falls_back_to_an_inner_ring_when_the_outer_slots_are_hidden_in_quick_mode()
    {
        var (model, _) = Open(CrossMode.Quick, 4);

        Assert.True(model.Stick(1, 0));
        Assert.Equal(4, model.Highlighted);
    }

    [Fact]
    public void Quick_mode_hides_outer_empty_slots_until_a_slot_further_out_is_filled()
    {
        var (middle, _) = Open(CrossMode.Quick, 5);
        Assert.True(middle.IsVisible(4));
        Assert.True(middle.IsVisible(5));
        Assert.False(middle.IsVisible(6));
        Assert.False(middle.Select(6));
        Assert.True(middle.Select(5));
        middle.Move(MenuInputAction.Right);
        Assert.Equal(5, middle.Highlighted);

        var (outer, _) = Open(CrossMode.Quick, 6);
        Assert.True(outer.IsVisible(5));
        Assert.True(outer.IsVisible(6));

        var (assign, _) = Open(CrossMode.Assign);
        Assert.All(Enumerable.Range(1, 12), s => Assert.True(assign.IsVisible(s)));
        Assert.False(assign.IsVisible(0));
        Assert.False(assign.IsVisible(13));
        Assert.False(assign.Select(13));
    }

    [Fact]
    public void The_wheel_cycles_filled_slots_in_order_wraps_both_ways_and_never_activates()
    {
        var (model, _) = Open(CrossMode.Assign, 2, 5, 9);
        var used = 0;
        var assigned = 0;
        model.Used += (_, _) => used++;
        model.Assigned += _ => assigned++;

        Assert.False(model.Wheel(0));
        Assert.True(model.Wheel(1));
        Assert.Equal(2, model.Highlighted);
        model.Wheel(1);
        Assert.Equal(5, model.Highlighted);
        model.Wheel(1);
        Assert.Equal(9, model.Highlighted);
        model.Wheel(1);
        Assert.Equal(2, model.Highlighted);
        model.Wheel(-1);
        Assert.Equal(9, model.Highlighted);
        model.Wheel(-1);
        Assert.Equal(5, model.Highlighted);
        Assert.Equal(0, used);
        Assert.Equal(0, assigned);
        Assert.False(model.IsClosed);

        var (empty, _) = Open(CrossMode.Assign);
        Assert.False(empty.Wheel(1));
        var (backwards, _) = Open(CrossMode.Assign, 2, 5);
        backwards.Wheel(-1);
        Assert.Equal(5, backwards.Highlighted);
    }

    [Fact]
    public void Activating_in_quick_mode_uses_the_item_plays_accept_and_closes_once()
    {
        var (model, sounds) = Open(CrossMode.Quick, 4);
        var used = new List<(int Slot, string Name)>();
        var closes = 0;
        model.Used += (slot, entry) => used.Add((slot, entry.Name));
        model.Closed += () => closes++;
        model.Select(4);

        Assert.True(model.Handle(MenuInputAction.Accept));
        model.Handle(MenuInputAction.Accept);
        model.Cancel();

        Assert.Equal(new[] { (4, "Item4") }, used);
        Assert.Equal(1, closes);
        Assert.True(model.IsClosed);
        Assert.Contains("ui.accept", sounds);
        Assert.False(model.Handle(MenuInputAction.Up));
        Assert.False(model.Select(4));
    }

    [Fact]
    public void Activating_an_empty_unavailable_or_unhighlighted_slot_in_quick_mode_does_not_use_or_close()
    {
        var slots = Slots(4);
        slots[3] = new FavoriteEntry("Broken", Available: false);
        var model = FavouritesCrossModel.TryOpen(CrossMode.Quick, slots, false, false)!;
        var sounds = new List<string>();
        var used = 0;
        model.SoundRequested += sounds.Add;
        model.Used += (_, _) => used++;

        model.Activate();
        Assert.Empty(sounds);

        model.Select(4);
        sounds.Clear();
        model.Activate();
        Assert.Equal(new[] { "ui.error" }, sounds);
        model.Select(3);
        sounds.Clear();
        model.Activate();

        Assert.Equal(new[] { "ui.error" }, sounds);
        Assert.Equal(0, used);
        Assert.False(model.IsClosed);
    }

    [Fact]
    public void Back_closes_the_cross_and_other_buttons_are_not_handled()
    {
        var (model, _) = Open(CrossMode.Assign);

        Assert.False(model.Handle(MenuInputAction.Sort));
        Assert.True(model.Handle(MenuInputAction.Back));
        Assert.True(model.IsClosed);
    }

    [Fact]
    public void Activating_in_assign_mode_reports_the_slot_and_closes_after_the_hold_without_extending_it()
    {
        var (model, sounds) = Open(CrossMode.Assign);
        var assigned = new List<int>();
        var closes = 0;
        model.Assigned += assigned.Add;
        model.Closed += () => closes++;
        model.Select(5);
        sounds.Clear();

        model.Activate();
        Assert.Equal(new[] { 5 }, assigned);
        Assert.Equal(new[] { "ui.accept" }, sounds);
        Assert.False(model.IsClosed);
        model.Tick(0.3);
        Assert.False(model.IsClosed);
        model.Activate();
        model.Tick(0.2);

        Assert.True(model.IsClosed);
        Assert.Equal(new[] { 5, 5 }, assigned);
        Assert.Equal(1, closes);
        model.Tick(1);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void A_failed_assignment_closes_with_the_inventory_changed_message()
    {
        var (model, _) = Open(CrossMode.Assign);
        var closes = 0;
        model.Closed += () => closes++;
        model.Select(2);
        model.Activate();

        model.FailAssign();
        model.Cancel();

        Assert.Equal("inventory changed, select the item again", model.Message);
        Assert.True(model.IsClosed);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void Replacing_the_slots_rejects_a_wrong_length_and_moves_a_vanished_highlight_to_the_nearest_item()
    {
        var (model, _) = Open(CrossMode.Quick, 3, 4, 9);
        model.Select(4);

        Assert.False(model.SetSlots(null));
        Assert.False(model.SetSlots(Slots(3).Take(5).ToArray()));
        Assert.Equal(4, model.Highlighted);
        Assert.True(model.SetSlots(Slots(3, 9)));
        Assert.Equal(3, model.Highlighted);
        Assert.Equal("Item3 (1)", model.NameLine);

        var unavailable = Slots(3, 9);
        unavailable[2] = new FavoriteEntry("Item3", Available: false);
        model.SetSlots(unavailable);
        Assert.Equal(9, model.Highlighted);
    }

    [Fact]
    public void Replacing_the_slots_with_nothing_filled_closes_a_quick_cross_but_keeps_an_assign_cross_open()
    {
        var (quick, _) = Open(CrossMode.Quick, 3);
        var closes = 0;
        quick.Closed += () => closes++;
        quick.Select(3);

        Assert.True(quick.SetSlots(Slots()));
        Assert.True(quick.IsClosed);
        Assert.Equal(1, closes);
        Assert.False(quick.SetSlots(Slots(3)));

        var (assign, _) = Open(CrossMode.Assign, 3);
        assign.Select(3);
        Assert.True(assign.SetSlots(Slots()));
        Assert.False(assign.IsClosed);
        Assert.Equal(3, assign.Highlighted);
    }

    [Fact]
    public void The_name_and_ammo_lines_describe_the_highlighted_entry()
    {
        var slots = Slots();
        slots[3] = new FavoriteEntry("10mm Pistol", 3, "10mm", 42, Equipped: true);
        slots[4] = new FavoriteEntry("Stimpak", 5);
        var model = FavouritesCrossModel.TryOpen(CrossMode.Assign, slots, false, false)!;

        Assert.Equal(string.Empty, model.NameLine);
        Assert.Equal(string.Empty, model.AmmoLine);
        model.Select(4);
        Assert.Equal("10mm Pistol (3)  Equipped", model.NameLine);
        Assert.Equal("10mm (42)", model.AmmoLine);
        Assert.Equal("10mm Pistol", model.HighlightedEntry!.Name);
        model.Select(5);
        Assert.Equal("Stimpak (5)", model.NameLine);
        Assert.Equal(string.Empty, model.AmmoLine);
        model.Select(1);
        Assert.Null(model.HighlightedEntry);
        Assert.Equal(string.Empty, model.NameLine);
    }

    [Fact]
    public void Selecting_the_same_slot_again_does_not_repeat_the_cue_or_the_change_event()
    {
        var (model, sounds) = Open(CrossMode.Assign);
        var changes = 0;
        model.Changed += () => changes++;

        model.Select(5);
        model.Select(5);

        Assert.Equal(new[] { "ui.focus.2" }, sounds);
        Assert.Equal(1, changes);
    }
}
