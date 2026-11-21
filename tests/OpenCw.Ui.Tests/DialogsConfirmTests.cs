using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DialogsConfirmTests
{
    [Fact]
    public void Holding_to_the_end_completes_exactly_once()
    {
        var model = new HoldToConfirmModel(1.0);
        model.Press();

        Assert.False(model.Tick(0.5));
        Assert.Equal(0.5, model.Progress);
        Assert.Equal(HoldState.Holding, model.State);

        Assert.True(model.Tick(0.6));
        Assert.True(model.IsCompleted);
        Assert.Equal(1.0, model.Progress);

        Assert.False(model.Tick(1.0));
        Assert.False(model.Press());
    }

    [Fact]
    public void Releasing_early_resets_progress_and_the_next_hold_starts_from_zero()
    {
        var model = new HoldToConfirmModel(1.0);
        model.Press();
        model.Tick(0.9);

        model.Release();

        Assert.Equal(HoldState.Idle, model.State);
        Assert.Equal(0.0, model.Progress);

        model.Press();
        Assert.False(model.Tick(0.9));
        Assert.Equal(0.9, model.Progress, 6);
    }

    [Fact]
    public void Release_after_completion_does_not_undo_it()
    {
        var model = new HoldToConfirmModel(0.5);
        model.Press();
        model.Tick(1.0);

        model.Release();

        Assert.True(model.IsCompleted);
    }

    [Fact]
    public void Key_repeat_presses_while_holding_do_not_restart_the_timer()
    {
        var model = new HoldToConfirmModel(1.0);
        model.Press();
        model.Tick(0.6);

        model.Press();

        Assert.Equal(0.6, model.Progress, 6);
        Assert.True(model.Tick(0.5));
    }

    [Fact]
    public void Time_does_not_advance_unless_a_hold_is_in_progress_and_bad_deltas_are_ignored()
    {
        var model = new HoldToConfirmModel(1.0);

        Assert.False(model.Tick(5.0));
        Assert.Equal(HoldState.Idle, model.State);

        model.Press();
        Assert.False(model.Tick(double.NaN));
        Assert.False(model.Tick(-1.0));
        Assert.False(model.Tick(double.PositiveInfinity));
        Assert.Equal(0.0, model.Progress);
    }

    [Fact]
    public void One_oversized_tick_completes_once_with_progress_clamped_to_one()
    {
        var model = new HoldToConfirmModel(1.0);
        model.Press();

        Assert.True(model.Tick(60.0));
        Assert.False(model.Tick(60.0));
        Assert.Equal(1.0, model.Progress);
    }

    [Fact]
    public void Double_press_mode_arms_on_the_first_press_and_completes_on_the_second()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress, 1.5);

        Assert.False(model.Press());
        Assert.Equal(HoldState.Armed, model.State);
        Assert.Equal(0.5, model.Progress);

        model.Release();
        Assert.Equal(HoldState.Armed, model.State);

        Assert.True(model.Press());
        Assert.True(model.IsCompleted);
        Assert.False(model.Press());
    }

    [Fact]
    public void Double_press_arming_expires_after_the_window_and_needs_two_fresh_presses()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress, 1.5);
        model.Press();

        Assert.False(model.Tick(1.0));
        Assert.False(model.Tick(0.6));
        Assert.Equal(HoldState.Idle, model.State);

        Assert.False(model.Press());
        Assert.Equal(HoldState.Armed, model.State);
    }

    [Fact]
    public void A_second_press_inside_the_window_completes_even_after_time_has_passed()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress, 1.5);
        model.Press();
        model.Tick(1.4);

        Assert.True(model.Press());
    }

    [Fact]
    public void Switching_mode_resets_an_unfinished_attempt_but_not_a_completed_one()
    {
        var model = new HoldToConfirmModel(1.0);
        model.Press();
        model.Tick(0.5);

        model.SetMode(HoldMode.DoublePress);
        Assert.Equal(HoldState.Idle, model.State);
        Assert.Equal(HoldMode.DoublePress, model.Mode);

        model.Press();
        model.Press();
        model.SetMode(HoldMode.Hold);

        Assert.True(model.IsCompleted);
        Assert.Equal(HoldMode.DoublePress, model.Mode);
    }

    [Fact]
    public void Reset_returns_an_unfinished_attempt_to_idle()
    {
        var model = new HoldToConfirmModel(1.0, HoldMode.DoublePress);
        model.Press();

        model.Reset();

        Assert.Equal(HoldState.Idle, model.State);
        Assert.Equal(0.0, model.Progress);
    }

    [Fact]
    public void Invalid_durations_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldToConfirmModel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldToConfirmModel(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldToConfirmModel(1.0, HoldMode.Hold, -1));
    }

    [Fact]
    public void Typed_confirmation_needs_the_exact_phrase_when_case_matters()
    {
        var model = new TypedConfirmModel("delete world");

        model.Prompt.Insert("Delete World");
        Assert.False(model.Matches);
        Assert.False(model.CanConfirm);
        Assert.False(model.Confirm());
        Assert.Equal(DialogOutcome.Pending, model.Outcome);

        model.Prompt.SelectAll();
        model.Prompt.Insert("delete world");
        Assert.True(model.Matches);
        Assert.True(model.Confirm());
        Assert.Equal(DialogOutcome.Confirmed, model.Outcome);
    }

    [Fact]
    public void Typed_confirmation_ignores_case_when_asked_to()
    {
        var model = new TypedConfirmModel("Delete World", CaseRule.Insensitive);

        model.Prompt.Insert("DELETE world");

        Assert.True(model.Confirm());
    }

    [Fact]
    public void Typed_confirmation_ignores_surrounding_and_repeated_spaces()
    {
        var model = new TypedConfirmModel("delete world");

        model.Prompt.Insert("  delete    world ");

        Assert.True(model.Matches);
    }

    [Fact]
    public void Typed_confirmation_treats_composed_and_decomposed_accents_as_equal()
    {
        var model = new TypedConfirmModel("café");

        model.Prompt.Insert("café");

        Assert.True(model.Confirm());
    }

    [Fact]
    public void Typed_confirmation_with_a_partial_phrase_reports_what_to_type()
    {
        var model = new TypedConfirmModel("erase");

        Assert.False(model.CanConfirm);
        Assert.Equal("Type \"erase\" to continue.", model.Prompt.Validity.Message);

        model.Prompt.Insert("eras");
        Assert.Equal("Type \"erase\" to continue.", model.Prompt.Validity.Message);

        model.Prompt.Insert("e");
        Assert.True(model.Prompt.Validity.IsValid);
    }

    [Fact]
    public void Typed_confirmation_cannot_be_confirmed_after_cancel()
    {
        var model = new TypedConfirmModel("erase");
        model.Prompt.Insert("erase");

        model.Cancel();

        Assert.Equal(DialogOutcome.Cancelled, model.Outcome);
        Assert.False(model.Confirm());
    }

    [Fact]
    public void Typed_confirmation_limits_input_to_a_little_more_than_the_phrase()
    {
        var model = new TypedConfirmModel("ab");

        model.Prompt.Insert(new string('x', 100));

        Assert.Equal(model.Phrase.Length + 16, model.Prompt.Value.Length);
        Assert.False(model.Matches);
    }

    [Fact]
    public void Typed_confirmation_rejects_a_blank_phrase()
    {
        Assert.Throws<ArgumentException>(() => new TypedConfirmModel("   "));
    }
}
