using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Prompts;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MultiActivateTests
{
    static ActivationOption[] Four() => new[]
    {
        new ActivationOption("Talk"),
        new ActivationOption("Trade"),
        new ActivationOption("Order"),
        new ActivationOption("Move"),
    };

    static (MultiActivateController Ctrl, List<int> Chosen, State Flags) Make(IReadOnlyList<ActivationOption>? options = null)
    {
        var chosen = new List<int>();
        var flags = new State();
        var ctrl = MultiActivateController.TryCreate(options ?? Four(), () => flags.Valid, () => flags.Aimed, chosen.Add);
        Assert.NotNull(ctrl);
        return (ctrl!, chosen, flags);
    }

    sealed class State
    {
        public bool Valid { get; set; } = true;

        public bool Aimed { get; set; } = true;
    }

    [Theory]
    [InlineData(MenuInputAction.Down, 0)]
    [InlineData(MenuInputAction.Right, 1)]
    [InlineData(MenuInputAction.Left, 2)]
    [InlineData(MenuInputAction.Up, 3)]
    public void The_four_direction_inputs_choose_slots_zero_to_three_directly(MenuInputAction action, int index)
    {
        var (ctrl, chosen, _) = Make();

        ctrl.Handle(action);

        Assert.Equal(new[] { index }, chosen);
        Assert.True(ctrl.IsFinished);
        Assert.True(ctrl.IsConfirmed);
    }

    [Fact]
    public void Fewer_than_two_options_do_not_open_and_more_than_four_keep_the_first_four()
    {
        Assert.Null(MultiActivateController.TryCreate(new[] { new ActivationOption("Only") }, () => true, () => true, _ => { }));

        var five = Four().Append(new ActivationOption("Fifth")).ToArray();
        var (ctrl, _, _) = Make(five);

        Assert.Equal(4, ctrl.List.RowCount);
    }

    [Fact]
    public void An_invalid_target_or_missing_callback_does_not_open()
    {
        Assert.Null(MultiActivateController.TryCreate(Four(), () => false, () => true, _ => { }));
        Assert.Null(MultiActivateController.TryCreate(Four(), () => true, () => true, null));
        Assert.Null(MultiActivateController.TryCreate(null, () => true, () => true, _ => { }));
    }

    [Fact]
    public void Rows_show_their_slot_input_and_a_second_choice_after_closing_is_ignored()
    {
        var (ctrl, chosen, _) = Make();

        Assert.Equal(new[] { "Down: Talk", "Right: Trade", "Left: Order", "Up: Move" }, WorkshopTestHelpers.RowTexts(ctrl.List));

        ctrl.Handle(MenuInputAction.Down);
        Assert.False(ctrl.Handle(MenuInputAction.Right));

        Assert.Equal(new[] { 0 }, chosen);
    }

    [Fact]
    public void A_disabled_option_says_why_and_the_menu_stays_open_without_choosing()
    {
        var options = new[] { new ActivationOption("Talk"), new ActivationOption("Trade", false, "Not now.") };
        var (ctrl, chosen, _) = Make(options);

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal("Not now.", ctrl.Message);
        Assert.False(ctrl.IsFinished);
        Assert.Empty(chosen);
    }

    [Fact]
    public void Accept_chooses_the_focused_row_and_focus_starts_on_the_first_enabled_one()
    {
        var options = new[] { new ActivationOption("Talk", false), new ActivationOption("Trade") };
        var (ctrl, chosen, _) = Make(options);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { 1 }, chosen);
    }

    [Fact]
    public void A_mouse_click_on_a_row_chooses_it()
    {
        var (ctrl, chosen, _) = Make();

        ctrl.Rows.Activate(2);

        Assert.Equal(new[] { 2 }, chosen);
    }

    [Fact]
    public void Back_cancels_without_choosing()
    {
        var (ctrl, chosen, _) = Make();

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.IsConfirmed);
        Assert.Empty(chosen);
    }

    [Fact]
    public void An_explicit_slot_overrides_list_order_when_unique_and_a_duplicate_falls_back_to_list_order()
    {
        var unique = new[] { new ActivationOption("Talk", Slot: 3), new ActivationOption("Trade", Slot: 2) };
        var (ctrl, chosen, _) = Make(unique);
        ctrl.Handle(MenuInputAction.Up);
        Assert.Equal(new[] { 0 }, chosen);

        var duplicate = new[] { new ActivationOption("Talk", Slot: 3), new ActivationOption("Trade", Slot: 3) };
        var (other, otherChosen, _) = Make(duplicate);
        other.Handle(MenuInputAction.Right);
        Assert.Equal(new[] { 1 }, otherChosen);
    }

    [Fact]
    public void Looking_away_for_half_a_second_closes_without_a_choice_and_looking_back_resets_the_timer()
    {
        var (ctrl, chosen, flags) = Make();

        flags.Aimed = false;
        ctrl.Tick(0.4);
        Assert.False(ctrl.IsFinished);
        flags.Aimed = true;
        ctrl.Tick(0.1);
        flags.Aimed = false;
        ctrl.Tick(0.4);
        Assert.False(ctrl.IsFinished);

        ctrl.Tick(0.1);

        Assert.True(ctrl.IsFinished);
        Assert.Empty(chosen);
    }

    [Fact]
    public void A_target_that_stops_being_valid_closes_the_menu()
    {
        var (ctrl, chosen, flags) = Make();

        flags.Valid = false;
        ctrl.Tick(0.01);

        Assert.True(ctrl.IsFinished);
        Assert.Empty(chosen);
    }

    [Fact]
    public void Replacing_options_keeps_focus_and_fewer_than_two_closes_the_menu()
    {
        var (ctrl, chosen, _) = Make();
        ctrl.Rows.Select(2);

        Assert.True(ctrl.SetOptions(new[] { new ActivationOption("A"), new ActivationOption("B"), new ActivationOption("C") }));
        Assert.Equal(2, ctrl.Rows.SelectedIndex);

        Assert.False(ctrl.SetOptions(new[] { new ActivationOption("A") }));

        Assert.True(ctrl.IsFinished);
        Assert.Empty(chosen);
    }

    [Fact]
    public void The_callback_cannot_run_a_second_time_even_if_it_calls_back_into_the_menu()
    {
        var chosen = new List<int>();
        MultiActivateController? ctrl = null;
        ctrl = MultiActivateController.TryCreate(Four(), () => true, () => true, index =>
        {
            chosen.Add(index);
            ctrl!.Handle(MenuInputAction.Down);
        });

        ctrl!.Handle(MenuInputAction.Down);

        Assert.Single(chosen);
    }
}
