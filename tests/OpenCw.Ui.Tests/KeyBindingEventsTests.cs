using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingEventsTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;

    static KeyBindingModel Make() => new(DefaultBindableActions.All, new InMemoryFileStore());

    static InputMapEntry[] ControllerEntries(KeyBindingPlan plan, string action) =>
        plan.Entries.Where(e => e.ActionName == action && e.Device == Pad).ToArray();

    [Fact]
    public void A_pad_code_is_a_button_unless_it_carries_the_axis_marker()
    {
        Assert.Equal(new ControllerBinding(ControllerBindingKind.Button, "B"), KeyBindingEvents.ParseController("Pad B"));
        Assert.Equal(new ControllerBinding(ControllerBindingKind.Button, "RightShoulder"), KeyBindingEvents.ParseController("Pad RightShoulder"));
        Assert.Equal(new ControllerBinding(ControllerBindingKind.Axis, "TriggerRight"), KeyBindingEvents.ParseController("Pad Axis TriggerRight"));
        Assert.Equal(new ControllerBinding(ControllerBindingKind.Axis, "TriggerLeft"), KeyBindingEvents.ParseController("Pad Axis TriggerLeft"));
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController("Pad ").Kind);
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController("Pad Axis ").Kind);
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController("Mouse Left").Kind);
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController("W").Kind);
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController(null).Kind);
        Assert.Equal(ControllerBindingKind.None, KeyBindingEvents.ParseController("").Kind);
    }

    [Fact]
    public void A_controller_remap_owns_trigger_axes_and_leaves_stick_motion_and_keyboard_alone()
    {
        Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadButton));
        Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight));
        Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxis.TriggerLeft));
        Assert.False(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxis.Other));
        Assert.False(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxis.None));
        Assert.False(KeyBindingEvents.IsManaged(Pad, ObservedEvent.Key));
        Assert.False(KeyBindingEvents.IsManaged(Pad, ObservedEvent.MouseButton));
        Assert.True(KeyBindingEvents.IsManaged(Kb, ObservedEvent.Key));
        Assert.True(KeyBindingEvents.IsManaged(Kb, ObservedEvent.MouseButton));
        Assert.False(KeyBindingEvents.IsManaged(Kb, ObservedEvent.JoypadButton));
        Assert.False(KeyBindingEvents.IsManaged(Kb, ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight));
        Assert.False(KeyBindingEvents.IsManaged(Pad, ObservedEvent.Other));
    }

    [Fact]
    public void Attack_and_aim_default_to_the_trigger_axes_and_remapping_attack_drops_the_axis()
    {
        var model = Make();
        var defaults = KeyBindingInputMap.Build(model);

        Assert.Equal(new[] { new InputMapEntry("attack", Pad, "Pad Axis TriggerRight") }, ControllerEntries(defaults, "attack"));
        Assert.Equal(new[] { new InputMapEntry("iron_sights", Pad, "Pad Axis TriggerLeft") }, ControllerEntries(defaults, "iron_sights"));

        model.BeginCapture("attack", Pad, P);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("Pad Y"));

        var remapped = KeyBindingInputMap.Build(model);

        Assert.Equal(new[] { new InputMapEntry("attack", Pad, "Pad Y") }, ControllerEntries(remapped, "attack"));
        Assert.DoesNotContain(remapped.Entries, e => e.ActionName == "attack" && e.Device == Pad &&
            KeyBindingEvents.ParseController(e.Code).Kind == ControllerBindingKind.Axis);
        Assert.Equal(new[] { new InputMapEntry("iron_sights", Pad, "Pad Axis TriggerLeft") }, ControllerEntries(remapped, "iron_sights"));
    }

    [Fact]
    public void Unbinding_attack_removes_both_trigger_entries_and_keeps_the_other_actions()
    {
        var model = Make();
        model.Unbind("attack", Pad, BindingSlot.Primary);

        var plan = KeyBindingInputMap.Build(model);

        Assert.Empty(ControllerEntries(plan, "attack"));
        Assert.Contains(new InputTarget("attack", Pad, true), plan.Targets);
        Assert.Equal(new[] { new InputMapEntry("iron_sights", Pad, "Pad Axis TriggerLeft") }, ControllerEntries(plan, "iron_sights"));
        Assert.Equal(new[] { new InputMapEntry("sneak", Pad, "Pad B") }, ControllerEntries(plan, "sneak"));
    }

    [Fact]
    public void Resetting_attack_restores_the_trigger_axis_code()
    {
        var model = Make();
        model.BeginCapture("attack", Pad, P);
        model.Capture("Pad Y");

        model.Reset("attack", Pad);

        Assert.Equal("Pad Axis TriggerRight", model.Get("attack", Pad, BindingSlot.Primary));
        Assert.True(model.IsDefault("attack", Pad));
        Assert.Equal(new[] { new InputMapEntry("attack", Pad, "Pad Axis TriggerRight") },
            ControllerEntries(KeyBindingInputMap.Build(model), "attack"));
    }

    [Fact]
    public void A_persisted_axis_code_reloads_as_the_same_default()
    {
        var store = new InMemoryFileStore();
        var first = new KeyBindingModel(DefaultBindableActions.All, store);
        first.BeginCapture("attack", Pad, P);
        first.Capture("Pad Y");

        var reloaded = new KeyBindingModel(DefaultBindableActions.All, store);

        Assert.Equal("Pad Axis TriggerLeft", reloaded.Get("aim", Pad, BindingSlot.Primary));
        Assert.True(reloaded.IsDefault("aim", Pad));
        Assert.Equal("Pad Y", reloaded.Get("attack", Pad, BindingSlot.Primary));
        Assert.False(reloaded.IsDefault("attack", Pad));
        Assert.Equal(new[] { new InputMapEntry("attack", Pad, "Pad Y") },
            ControllerEntries(KeyBindingInputMap.Build(reloaded), "attack"));
    }

    [Fact]
    public void A_pad_entry_plans_a_button_and_an_axis_entry_plans_full_deflection_motion()
    {
        Assert.Equal(new PlannedBinding(BindingEventKind.JoypadButton, "B", 0f),
            KeyBindingEvents.Plan(new InputMapEntry("sneak", Pad, "Pad B")));
        Assert.Equal(new PlannedBinding(BindingEventKind.JoypadMotion, "TriggerRight", 1f),
            KeyBindingEvents.Plan(new InputMapEntry("attack", Pad, "Pad Axis TriggerRight")));
        Assert.Equal(BindingEventKind.None, KeyBindingEvents.Plan(new InputMapEntry("attack", Pad, "Pad ")).Kind);
        Assert.Equal(BindingEventKind.None, KeyBindingEvents.Plan(new InputMapEntry("attack", Pad, "W")).Kind);
        Assert.Equal(BindingEventKind.None, KeyBindingEvents.Plan(new InputMapEntry("attack", Pad, "Axis TriggerRight")).Kind);
    }

    [Fact]
    public void A_keyboard_entry_plans_its_mouse_button_name_or_the_key_string()
    {
        Assert.Equal(new PlannedBinding(BindingEventKind.MouseButton, "Left", 0f),
            KeyBindingEvents.Plan(new InputMapEntry("attack", Kb, "Mouse Left")));
        Assert.Equal(new PlannedBinding(BindingEventKind.MouseButton, "Xbutton2", 0f),
            KeyBindingEvents.Plan(new InputMapEntry("attack", Kb, "Mouse 5")));
        Assert.Equal(new PlannedBinding(BindingEventKind.Key, "F5", 0f),
            KeyBindingEvents.Plan(new InputMapEntry("quick_save", Kb, "F5")));
        Assert.Equal(BindingEventKind.None, KeyBindingEvents.Plan(new InputMapEntry("quick_save", Kb, "Ctrl+S")).Kind);
        Assert.Equal(BindingEventKind.None, KeyBindingEvents.Plan(new InputMapEntry("quick_save", Kb, "")).Kind);
        Assert.Null(KeyBindingEvents.MouseName("Mouse 6"));
        Assert.Equal("Middle", KeyBindingEvents.MouseName("Mouse Middle"));
    }
}
