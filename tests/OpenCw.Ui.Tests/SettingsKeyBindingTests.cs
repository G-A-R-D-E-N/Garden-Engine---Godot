using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsKeyBindingTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;
    const BindingSlot S = BindingSlot.Secondary;

    static BindableAction[] Actions() => new[]
    {
        new BindableAction("jump", "Jump", "Game", "Space", null, "JoyA"),
        new BindableAction("use", "Use", "Game", "E", "F", "JoyX"),
        new BindableAction("accept", "Accept", "Menu", "E", null, "JoyA"),
    };

    static KeyBindingModel Make(InMemoryFileStore? store = null) => new(Actions(), store ?? new InMemoryFileStore());

    [Fact]
    public void Capturing_a_free_key_binds_it_and_returns_to_idle()
    {
        var model = Make();
        Assert.True(model.BeginCapture("jump", Kb, P));
        Assert.Equal(CaptureState.Listening, model.State);

        Assert.Equal(CaptureOutcome.Bound, model.Capture("G"));

        Assert.Equal("G", model.Get("jump", Kb, P));
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Null(model.Target);
        Assert.False(model.IsDefault("jump", Kb));
    }

    [Fact]
    public void Changed_fires_once_per_committed_edit_and_not_for_cancelled_or_pending_ones()
    {
        var model = Make();
        var raised = 0;
        model.Changed += () => raised++;

        model.BeginCapture("jump", Kb, P);
        model.Capture("E");
        model.Resolve(ConflictResolution.Cancel);
        Assert.Equal(0, raised);

        model.BeginCapture("jump", Kb, P);
        model.Capture("G");
        Assert.Equal(1, raised);
        model.Unbind("jump", Kb, P);
        Assert.Equal(2, raised);
        model.Reset("jump", Kb);
        Assert.Equal(3, raised);
        model.ResetDevice(Kb);
        Assert.Equal(4, raised);
        model.ResetAll();
        Assert.Equal(5, raised);
    }

    [Fact]
    public void Conflicts_are_found_case_insensitively_within_the_same_context_and_device_only()
    {
        var model = Make();

        var conflicts = model.FindConflicts("jump", Kb, P, "e");

        var holder = Assert.Single(conflicts);
        Assert.Equal(new BindingRef("use", Kb, P), holder);
        Assert.Empty(model.FindConflicts("jump", Pad, P, "e"));
        Assert.Empty(model.FindConflicts("use", Kb, P, "E"));
    }

    [Fact]
    public void A_conflicting_capture_waits_for_a_resolution_and_changes_nothing_yet()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);

        Assert.Equal(CaptureOutcome.Conflict, model.Capture("E"));

        Assert.Equal(CaptureState.Resolving, model.State);
        Assert.Equal("E", model.PendingCode);
        Assert.Equal(new[] { new BindingRef("use", Kb, P) }, model.Conflicts.ToArray());
        Assert.Equal("Space", model.Get("jump", Kb, P));
        Assert.Equal("E", model.Get("use", Kb, P));
        Assert.Equal(CaptureOutcome.Ignored, model.Capture("X"));
    }

    [Fact]
    public void Swap_gives_the_other_action_the_previous_binding_of_the_target_slot()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        model.Capture("E");

        Assert.True(model.Resolve(ConflictResolution.Swap));

        Assert.Equal("E", model.Get("jump", Kb, P));
        Assert.Equal("Space", model.Get("use", Kb, P));
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Equal("E", model.Get("accept", Kb, P));
    }

    [Fact]
    public void Unbind_clears_the_other_action_and_cancel_changes_nothing()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        model.Capture("E");
        Assert.True(model.Resolve(ConflictResolution.Unbind));
        Assert.Equal("E", model.Get("jump", Kb, P));
        Assert.Null(model.Get("use", Kb, P));

        model.BeginCapture("jump", Kb, S);
        model.Capture("F");
        Assert.True(model.Resolve(ConflictResolution.Cancel));
        Assert.Null(model.Get("jump", Kb, S));
        Assert.Equal("F", model.Get("use", Kb, S));
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.False(model.Resolve(ConflictResolution.Swap));
    }

    [Fact]
    public void Two_slots_of_the_same_action_conflict_and_swap_with_each_other()
    {
        var model = Make();
        model.BeginCapture("use", Kb, P);

        Assert.Equal(CaptureOutcome.Conflict, model.Capture("F"));
        model.Resolve(ConflictResolution.Swap);

        Assert.Equal("F", model.Get("use", Kb, P));
        Assert.Equal("E", model.Get("use", Kb, S));
    }

    [Fact]
    public void Keyboard_and_controller_bindings_never_conflict_with_each_other()
    {
        var model = Make();

        model.BeginCapture("use", Kb, S);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("JoyA"));
        model.BeginCapture("use", Pad, S);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("Space"));

        Assert.Equal("JoyA", model.Get("use", Kb, S));
        Assert.Equal("Space", model.Get("use", Pad, S));
        Assert.Equal("JoyA", model.Get("jump", Pad, P));
    }

    [Fact]
    public void Rebinding_the_same_key_to_the_same_slot_is_not_a_conflict()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);

        Assert.Equal(CaptureOutcome.Bound, model.Capture("space"));
    }

    [Fact]
    public void Escape_cancels_a_keyboard_capture_and_idle_or_blank_input_is_ignored()
    {
        var model = Make();
        Assert.Equal(CaptureOutcome.Ignored, model.Capture("G"));
        model.BeginCapture("jump", Kb, P);
        Assert.Equal(CaptureOutcome.Ignored, model.Capture("  "));
        Assert.Equal(CaptureState.Listening, model.State);

        Assert.Equal(CaptureOutcome.Cancelled, model.Capture("Escape"));

        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Equal("Space", model.Get("jump", Kb, P));
    }

    [Fact]
    public void A_listening_capture_times_out_but_a_pending_resolution_does_not()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        Assert.False(model.Tick(7.5));
        Assert.True(model.Tick(0.5));
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Equal(CaptureOutcome.Ignored, model.Capture("G"));

        model.BeginCapture("jump", Kb, P);
        model.Capture("E");
        Assert.False(model.Tick(1000));
        Assert.Equal(CaptureState.Resolving, model.State);
    }

    [Fact]
    public void Capture_seconds_left_counts_down_while_listening_and_cancelling_ends_any_capture()
    {
        var model = Make();
        Assert.Equal(0, model.CaptureSecondsLeft);

        model.BeginCapture("jump", Kb, P);
        Assert.Equal(8, model.CaptureSecondsLeft);
        Assert.False(model.Tick(3));
        Assert.Equal(5, model.CaptureSecondsLeft);
        Assert.False(model.Tick(double.NaN));
        Assert.Equal(5, model.CaptureSecondsLeft);
        model.CancelCapture();
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Null(model.Target);
        Assert.Equal(0, model.CaptureSecondsLeft);

        model.BeginCapture("jump", Kb, P);
        Assert.Equal(CaptureOutcome.Conflict, model.Capture("E"));
        Assert.Equal(0, model.CaptureSecondsLeft);
        model.CancelCapture();
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Null(model.PendingCode);
        Assert.Empty(model.Conflicts);
        Assert.Equal("Space", model.Get("jump", Kb, P));
        Assert.Equal("E", model.Get("use", Kb, P));
    }

    [Fact]
    public void Beginning_a_new_capture_discards_a_pending_resolution_and_unknown_actions_are_refused()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        model.Capture("E");

        Assert.True(model.BeginCapture("jump", Kb, S));

        Assert.Equal(CaptureState.Listening, model.State);
        Assert.Empty(model.Conflicts);
        Assert.Null(model.PendingCode);
        Assert.False(model.BeginCapture("nope", Kb, P));
    }

    [Fact]
    public void Reset_restores_defaults_and_unbinds_other_actions_that_took_the_default_key()
    {
        var model = Make();
        model.BeginCapture("jump", Kb, P);
        model.Capture("E");
        model.Resolve(ConflictResolution.Unbind);
        model.BeginCapture("use", Kb, P);
        model.Capture("Space");
        Assert.Equal("E", model.Get("jump", Kb, P));

        var displaced = model.Reset("jump", Kb);

        Assert.Equal("Space", model.Get("jump", Kb, P));
        Assert.Equal(new[] { new BindingRef("use", Kb, P) }, displaced.ToArray());
        Assert.Null(model.Get("use", Kb, P));
        Assert.True(model.IsDefault("jump", Kb));
    }

    [Fact]
    public void Unbind_clears_a_slot_and_reset_device_and_reset_all_are_scoped()
    {
        var model = Make();
        model.Unbind("use", Kb, S);
        model.BeginCapture("jump", Pad, P);
        model.Capture("JoyB");
        Assert.Null(model.Get("use", Kb, S));

        model.ResetDevice(Kb);
        Assert.Equal("F", model.Get("use", Kb, S));
        Assert.Equal("JoyB", model.Get("jump", Pad, P));

        model.ResetAll();
        Assert.Equal("JoyA", model.Get("jump", Pad, P));
        Assert.True(Actions().All(a => model.IsDefault(a.Id, Kb) && model.IsDefault(a.Id, Pad)));
    }

    [Fact]
    public void Bindings_including_unbound_slots_persist_and_reload()
    {
        var store = new InMemoryFileStore();
        var model = Make(store);
        Assert.Equal(SettingsLoadResult.NoFile, model.LoadResult);
        model.BeginCapture("jump", Kb, S);
        model.Capture("J");
        model.Unbind("use", Kb, P);
        model.BeginCapture("jump", Pad, P);
        model.Capture("JoyB");

        var loaded = Make(store);

        Assert.Equal(SettingsLoadResult.Loaded, loaded.LoadResult);
        Assert.Equal("J", loaded.Get("jump", Kb, S));
        Assert.Null(loaded.Get("use", Kb, P));
        Assert.Equal("F", loaded.Get("use", Kb, S));
        Assert.Equal("JoyB", loaded.Get("jump", Pad, P));
    }

    [Fact]
    public void Corrupt_or_malformed_files_fall_back_to_defaults_and_unknown_actions_are_dropped()
    {
        var corrupt = new InMemoryFileStore();
        corrupt.Files[KeyBindingModel.FileName] = "[[[";
        var model = Make(corrupt);
        Assert.Equal(SettingsLoadResult.Corrupt, model.LoadResult);
        Assert.Equal("Space", model.Get("jump", Kb, P));

        var partial = new InMemoryFileStore();
        partial.Files[KeyBindingModel.FileName] =
            "{\"keyboard\":{\"jump\":[\"G\",null],\"use\":[\"X\"],\"ghost\":[\"Q\",null],\"accept\":[5,null]}}";
        var loaded = Make(partial);
        Assert.Equal(SettingsLoadResult.Loaded, loaded.LoadResult);
        Assert.Equal("G", loaded.Get("jump", Kb, P));
        Assert.Null(loaded.Get("jump", Kb, S));
        Assert.Equal("E", loaded.Get("use", Kb, P));
        Assert.Equal("E", loaded.Get("accept", Kb, P));
    }

    [Fact]
    public void Construction_rejects_duplicate_or_blank_action_ids()
    {
        var duplicate = new[] { new BindableAction("a", "A", "Game"), new BindableAction("a", "B", "Game") };
        var blank = new[] { new BindableAction(" ", "A", "Game") };

        Assert.Throws<ArgumentException>(() => new KeyBindingModel(duplicate, new InMemoryFileStore()));
        Assert.Throws<ArgumentException>(() => new KeyBindingModel(blank, new InMemoryFileStore()));
    }

    [Fact]
    public void Unbinding_an_unknown_action_is_a_no_op_and_leaves_known_actions_working()
    {
        var store = new InMemoryFileStore();
        var model = Make(store);

        Assert.False(model.Unbind("ghost", Kb, P));

        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => model.Get("ghost", Kb, P));
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => model.Reset("ghost", Kb));
        Assert.Equal("Space", model.Get("jump", Kb, P));
        Assert.Empty(model.Reset("jump", Kb));
        Assert.True(model.Unbind("jump", Kb, P));
        Assert.Null(model.Get("jump", Kb, P));
    }

    [Fact]
    public void BoundLabel_follows_the_device_and_falls_back_to_the_secondary_slot()
    {
        var model = Make();
        Assert.Equal("E", model.BoundLabel("use", Kb));
        Assert.Equal("JoyX", model.BoundLabel("use", Pad));

        model.Unbind("use", Kb, P);
        Assert.Equal("F", model.BoundLabel("use", Kb));

        model.Unbind("use", Kb, S);
        Assert.Null(model.BoundLabel("use", Kb));
        Assert.Equal("JoyX", model.BoundLabel("use", Pad));
    }
}
