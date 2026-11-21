using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingInputMapTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;

    static KeyBindingModel Make() => new(DefaultBindableActions.All, new InMemoryFileStore());

    [Fact]
    public void Action_names_follow_the_input_map_and_alias_the_renamed_ones()
    {
        Assert.Equal("activate", KeyBindingInputMap.ActionNameFor("interact"));
        Assert.Equal("iron_sights", KeyBindingInputMap.ActionNameFor("aim"));
        Assert.Equal("jump", KeyBindingInputMap.ActionNameFor("jump"));
        Assert.Equal("reload", KeyBindingInputMap.ActionNameFor("reload"));
    }

    [Fact]
    public void Default_bindings_produce_one_entry_per_bound_slot_and_no_overrides()
    {
        var plan = KeyBindingInputMap.Build(Make());

        var expected = DefaultBindableActions.All.Sum(a =>
            new[] { a.KeyboardPrimary, a.KeyboardSecondary, a.ControllerPrimary, a.ControllerSecondary }.Count(c => c is not null));
        Assert.Equal(expected, plan.Entries.Count);
        Assert.Equal(DefaultBindableActions.All.Count * 2, plan.Targets.Count);
        Assert.All(plan.Targets, t => Assert.False(t.Overridden));
        Assert.Contains(new InputMapEntry("activate", Kb, "E"), plan.Entries);
        Assert.Contains(new InputMapEntry("iron_sights", Kb, "Mouse Right"), plan.Entries);
        Assert.Contains(new InputMapEntry("jump", Pad, "Pad A"), plan.Entries);
        Assert.DoesNotContain(plan.Entries, e => string.IsNullOrEmpty(e.Code));
    }

    [Fact]
    public void Rebinding_marks_only_that_device_of_that_action_as_overridden()
    {
        var model = Make();
        model.BeginCapture("interact", Kb, P);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("T"));

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal(new[] { new InputTarget("activate", Kb, true) }, plan.Targets.Where(t => t.Overridden).ToArray());
        Assert.Contains(new InputMapEntry("activate", Kb, "T"), plan.Entries);
        Assert.DoesNotContain(new InputMapEntry("activate", Kb, "E"), plan.Entries);
        Assert.Contains(new InputMapEntry("activate", Pad, "Pad X"), plan.Entries);
    }

    [Fact]
    public void Unbinding_leaves_an_override_with_no_entries()
    {
        var model = Make();
        model.Unbind("jump", Kb, P);

        var plan = KeyBindingInputMap.Build(model);

        Assert.Contains(new InputTarget("jump", Kb, true), plan.Targets);
        Assert.DoesNotContain(plan.Entries, e => e.ActionName == "jump" && e.Device == Kb);
    }

    [Fact]
    public void Signature_is_stable_until_a_binding_changes_and_returns_after_a_reset()
    {
        var model = Make();
        var initial = KeyBindingInputMap.Build(model).Signature;
        Assert.Equal(initial, KeyBindingInputMap.Build(model).Signature);

        model.BeginCapture("jump", Kb, P);
        model.Capture("G");
        var changed = KeyBindingInputMap.Build(model).Signature;
        Assert.NotEqual(initial, changed);

        model.Reset("jump", Kb);
        Assert.Equal(initial, KeyBindingInputMap.Build(model).Signature);
    }

    [Fact]
    public void Persisted_overrides_are_picked_up_by_a_new_model()
    {
        var store = new InMemoryFileStore();
        var first = new KeyBindingModel(DefaultBindableActions.All, store);
        first.BeginCapture("sneak", Pad, P);
        first.Capture("Pad Start");

        var plan = KeyBindingInputMap.Build(new KeyBindingModel(DefaultBindableActions.All, store));

        Assert.Contains(new InputMapEntry("sneak", Pad, "Pad Start"), plan.Entries);
        Assert.Contains(new InputTarget("sneak", Pad, true), plan.Targets);
    }
}
