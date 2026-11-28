using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingGap9Tests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;

    static BindableAction[] Actions() => new[]
    {
        new BindableAction("jump", "Jump", "Game", "Space", null, "JoyA"),
        new BindableAction("use", "Use", "Game", "E", "F", "JoyX"),
    };

    static KeyBindingModel Make(InMemoryFileStore? store = null) => new(Actions(), store ?? new InMemoryFileStore());

    [Fact]
    public void The_defaults_are_eight_seconds_and_escape()
    {
        var model = Make();

        Assert.Equal(8.0, model.CaptureTimeoutSeconds);
        Assert.Equal("Escape", model.KeyboardCancelCode);
        Assert.Equal(0.0, model.CaptureSecondsLeft);
    }

    [Fact]
    public void A_listening_capture_counts_down_from_the_timeout_and_expires_exactly_at_zero()
    {
        var model = Make();
        Assert.True(model.BeginCapture("jump", Kb, P));
        Assert.Equal(8.0, model.CaptureSecondsLeft);

        Assert.False(model.Tick(3));
        Assert.Equal(5.0, model.CaptureSecondsLeft);
        Assert.False(model.Tick(4.5));
        Assert.Equal(0.5, model.CaptureSecondsLeft);
        Assert.Equal(CaptureState.Listening, model.State);

        Assert.True(model.Tick(0.5));

        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Null(model.Target);
        Assert.Equal(0.0, model.CaptureSecondsLeft);
        Assert.Equal("Space", model.Get("jump", Kb, P));
    }

    [Fact]
    public void A_custom_timeout_applies_to_the_next_capture_only()
    {
        var model = Make();
        model.CaptureTimeoutSeconds = 2.5;
        model.BeginCapture("jump", Kb, P);
        Assert.Equal(2.5, model.CaptureSecondsLeft);

        model.CaptureTimeoutSeconds = 10;

        Assert.Equal(2.5, model.CaptureSecondsLeft);
        Assert.False(model.Tick(2.4));
        Assert.True(model.Tick(0.2));
        model.BeginCapture("jump", Kb, P);
        Assert.Equal(10.0, model.CaptureSecondsLeft);
    }

    [Fact]
    public void A_zero_timeout_expires_on_the_first_tick()
    {
        var model = Make();
        model.CaptureTimeoutSeconds = 0;
        model.BeginCapture("jump", Kb, P);

        Assert.Equal(0.0, model.CaptureSecondsLeft);
        Assert.Equal(CaptureState.Listening, model.State);
        Assert.True(model.Tick(0.001));
        Assert.Equal(CaptureState.Idle, model.State);
    }

    [Fact]
    public void Expiry_neither_saves_nor_raises_changed_and_bad_ticks_are_ignored()
    {
        var store = new InMemoryFileStore();
        var model = Make(store);
        var raised = 0;
        model.Changed += () => raised++;

        Assert.False(model.Tick(1));
        model.BeginCapture("jump", Kb, P);
        Assert.False(model.Tick(0));
        Assert.False(model.Tick(-1));
        Assert.False(model.Tick(double.NaN));
        Assert.False(model.Tick(double.PositiveInfinity));
        Assert.Equal(8.0, model.CaptureSecondsLeft);
        Assert.True(model.Tick(8));

        Assert.Equal(0, raised);
        Assert.Empty(store.Files);
    }

    [Fact]
    public void A_pending_conflict_does_not_time_out()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        Assert.Equal(CaptureOutcome.Conflict, model.Capture("E"));

        Assert.False(model.Tick(100));

        Assert.Equal(CaptureState.Resolving, model.State);
        Assert.Equal("E", model.PendingCode);
        Assert.Equal(0.0, model.CaptureSecondsLeft);
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("escape")]
    [InlineData("ESCAPE")]
    public void The_cancel_code_cancels_a_keyboard_capture_without_binding_or_saving(string code)
    {
        var store = new InMemoryFileStore();
        var model = Make(store);
        var raised = 0;
        model.Changed += () => raised++;
        model.BeginCapture("jump", Kb, P);

        Assert.Equal(CaptureOutcome.Cancelled, model.Capture(code));

        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Null(model.Target);
        Assert.Equal("Space", model.Get("jump", Kb, P));
        Assert.Equal(0, raised);
        Assert.Empty(store.Files);
    }

    [Fact]
    public void The_cancel_code_is_replaceable_and_the_old_code_then_binds()
    {
        var model = Make();
        model.KeyboardCancelCode = "Backspace";

        model.BeginCapture("jump", Kb, P);
        Assert.Equal(CaptureOutcome.Cancelled, model.Capture("backspace"));
        Assert.Equal("Space", model.Get("jump", Kb, P));

        model.BeginCapture("jump", Kb, P);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("Escape"));
        Assert.Equal("Escape", model.Get("jump", Kb, P));
    }

    [Fact]
    public void The_cancel_code_is_an_ordinary_binding_on_a_controller_capture()
    {
        var model = Make();
        model.BeginCapture("jump", Pad, P);

        Assert.Equal(CaptureOutcome.Bound, model.Capture("Escape"));

        Assert.Equal("Escape", model.Get("jump", Pad, P));
        Assert.Equal(CaptureState.Idle, model.State);
    }
}
