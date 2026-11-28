using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingApplierPlanTests
{
    const InputDevice Pad = InputDevice.Controller;
    const InputDevice Kb = InputDevice.Keyboard;

    static ManagedAxis ManagedAxisFor(string? name) => name switch
    {
        "TriggerLeft" => ManagedAxis.TriggerLeft,
        "TriggerRight" => ManagedAxis.TriggerRight,
        _ => ManagedAxis.Other,
    };

    [Fact]
    public void Every_default_controller_code_plans_as_the_event_the_applier_builds()
    {
        foreach (var action in DefaultBindableActions.All.Where(a => a.ControllerPrimary is not null))
        {
            var planned = KeyBindingEvents.Plan(new InputMapEntry(action.Id, Pad, action.ControllerPrimary!));
            var parsed = KeyBindingEvents.ParseController(action.ControllerPrimary);
            var expected = parsed.Kind == ControllerBindingKind.Axis ? BindingEventKind.JoypadMotion : BindingEventKind.JoypadButton;
            Assert.Equal(expected, planned.Kind);
            Assert.Equal(parsed.Name, planned.Name);
            Assert.Equal(expected == BindingEventKind.JoypadMotion ? 1f : 0f, planned.AxisValue);
            Assert.Equal(-1, planned.Device);
        }
    }

    [Theory]
    [InlineData("attack", "TriggerRight", ManagedAxis.TriggerRight)]
    [InlineData("aim", "TriggerLeft", ManagedAxis.TriggerLeft)]
    public void Trigger_defaults_plan_a_positive_full_deflection_on_a_managed_axis(string id, string axis, ManagedAxis managed)
    {
        var planned = KeyBindingEvents.Plan(new InputMapEntry(id, Pad, "Pad Axis " + axis));

        Assert.Equal(new PlannedBinding(BindingEventKind.JoypadMotion, axis, 1f), planned);
        Assert.Equal(-1, planned.Device);
        Assert.Equal(managed, ManagedAxisFor(planned.Name));
        Assert.True(KeyBindingEvents.IsManagedAxis(managed));
        Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, managed));
    }

    [Fact]
    public void Only_the_two_trigger_axes_are_managed()
    {
        Assert.True(KeyBindingEvents.IsManagedAxis(ManagedAxis.TriggerLeft));
        Assert.True(KeyBindingEvents.IsManagedAxis(ManagedAxis.TriggerRight));
        Assert.False(KeyBindingEvents.IsManagedAxis(ManagedAxis.Other));
        Assert.False(KeyBindingEvents.IsManagedAxis(ManagedAxis.None));
    }

    [Theory]
    [InlineData(ObservedEvent.Key, ManagedAxis.None, false)]
    [InlineData(ObservedEvent.MouseButton, ManagedAxis.None, false)]
    [InlineData(ObservedEvent.JoypadButton, ManagedAxis.None, true)]
    [InlineData(ObservedEvent.JoypadMotion, ManagedAxis.None, false)]
    [InlineData(ObservedEvent.JoypadMotion, ManagedAxis.Other, false)]
    [InlineData(ObservedEvent.JoypadMotion, ManagedAxis.TriggerLeft, true)]
    [InlineData(ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight, true)]
    [InlineData(ObservedEvent.Other, ManagedAxis.None, false)]
    public void A_controller_remap_manages_buttons_and_the_two_trigger_axes_only(ObservedEvent observed, ManagedAxis axis, bool managed) =>
        Assert.Equal(managed, KeyBindingEvents.IsManaged(Pad, observed, axis));

    [Theory]
    [InlineData(ObservedEvent.Key, true)]
    [InlineData(ObservedEvent.MouseButton, true)]
    [InlineData(ObservedEvent.JoypadButton, false)]
    [InlineData(ObservedEvent.JoypadMotion, false)]
    [InlineData(ObservedEvent.Other, false)]
    public void A_keyboard_remap_manages_keys_and_mouse_buttons_only(ObservedEvent observed, bool managed) =>
        Assert.Equal(managed, KeyBindingEvents.IsManaged(Kb, observed, ManagedAxis.TriggerRight));

    [Fact]
    public void A_default_controller_axis_plans_the_trigger_axis_the_applier_manages()
    {
        foreach (var action in DefaultBindableActions.All.Where(a => a.ControllerPrimary is not null))
        {
            var planned = KeyBindingEvents.Plan(new InputMapEntry(action.Id, Pad, action.ControllerPrimary!));
            if (planned.Kind != BindingEventKind.JoypadMotion) continue;
            Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxisFor(planned.Name)));
        }
    }

    [Fact]
    public void A_pad_button_carries_no_axis_value_and_a_keyboard_device_never_plans_joypad_motion()
    {
        Assert.Equal(0f, KeyBindingEvents.Plan(new InputMapEntry("jump", Pad, "Pad A")).AxisValue);
        Assert.NotEqual(BindingEventKind.JoypadMotion, KeyBindingEvents.Plan(new InputMapEntry("jump", Kb, "Pad Axis TriggerLeft")).Kind);
    }

    [Fact]
    public void A_controller_remap_erases_buttons_and_trigger_axes_and_leaves_stick_motion_alone()
    {
        var live = new[]
        {
            new ObservedBinding(ObservedEvent.Key),
            new ObservedBinding(ObservedEvent.MouseButton),
            new ObservedBinding(ObservedEvent.JoypadButton),
            new ObservedBinding(ObservedEvent.JoypadMotion, ManagedAxis.Other),
            new ObservedBinding(ObservedEvent.JoypadMotion, ManagedAxis.TriggerLeft),
            new ObservedBinding(ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight),
            new ObservedBinding(ObservedEvent.Other),
        };

        var managed = KeyBindingEvents.ManagedIndexes(Pad, live);
        var kept = Enumerable.Range(0, live.Length).Where(i => !managed.Contains(i)).Select(i => live[i]).ToArray();

        Assert.Equal(new[] { 2, 4, 5 }, managed);
        Assert.Equal(new[] { ObservedEvent.Key, ObservedEvent.MouseButton, ObservedEvent.JoypadMotion, ObservedEvent.Other },
            kept.Select(e => e.Observed));
        Assert.Equal(new[] { ManagedAxis.None, ManagedAxis.None, ManagedAxis.Other, ManagedAxis.None },
            kept.Select(e => e.Axis));
    }

    [Fact]
    public void A_keyboard_remap_erases_keys_and_mouse_buttons_only()
    {
        var live = new[]
        {
            new ObservedBinding(ObservedEvent.Key),
            new ObservedBinding(ObservedEvent.MouseButton),
            new ObservedBinding(ObservedEvent.JoypadButton),
            new ObservedBinding(ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight),
            new ObservedBinding(ObservedEvent.Other),
        };

        Assert.Equal(new[] { 0, 1 }, KeyBindingEvents.ManagedIndexes(Kb, live));
        Assert.Empty(KeyBindingEvents.ManagedIndexes(Kb, Array.Empty<ObservedBinding>()));
    }

    [Fact]
    public void A_remap_creates_only_the_entries_for_its_own_action_and_device()
    {
        var entries = new[]
        {
            new InputMapEntry("attack", Pad, "Pad Axis TriggerRight"),
            new InputMapEntry("attack", Pad, "Pad B"),
            new InputMapEntry("iron_sights", Pad, "Pad Y"),
            new InputMapEntry("attack", Kb, "Mouse Left"),
            new InputMapEntry("attack", Kb, "F"),
            new InputMapEntry("attack", Pad, ""),
            new InputMapEntry("attack", Pad, "Ctrl+Y"),
        };

        Assert.Equal(new[]
        {
            new PlannedBinding(BindingEventKind.JoypadMotion, "TriggerRight", 1f),
            new PlannedBinding(BindingEventKind.JoypadButton, "B", 0f),
        }, KeyBindingEvents.Creations("attack", Pad, entries));

        Assert.Equal(new[]
        {
            new PlannedBinding(BindingEventKind.MouseButton, "Left", 0f),
            new PlannedBinding(BindingEventKind.Key, "F", 0f),
        }, KeyBindingEvents.Creations("attack", Kb, entries));

        Assert.Empty(KeyBindingEvents.Creations("unbound", Pad, entries));
    }

    public enum Sample
    {
        Invalid = -1,
        A = 0,
        B = 1,
    }

    [Theory]
    [InlineData("A", true, Sample.A)]
    [InlineData("B", true, Sample.B)]
    [InlineData("Invalid", false, Sample.Invalid)]
    [InlineData("7", false, (Sample)7)]
    [InlineData("a", false, default(Sample))]
    [InlineData("", false, default(Sample))]
    [InlineData(null, false, default(Sample))]
    public void Names_resolve_only_to_defined_values_other_than_the_excluded_one(string? name, bool ok, Sample parsed)
    {
        Assert.Equal(ok, KeyBindingEvents.TryParseDefined(name, Sample.Invalid, out Sample value));
        if (ok)
            Assert.Equal(parsed, value);
    }

    [Fact]
    public void Planned_controller_names_are_never_empty_so_the_applier_gets_a_name_to_resolve()
    {
        foreach (var action in DefaultBindableActions.All.Where(a => a.ControllerPrimary is not null))
            Assert.NotEmpty(KeyBindingEvents.Plan(new InputMapEntry(action.Id, Pad, action.ControllerPrimary!)).Name);
    }

    [Theory]
    [InlineData("InputEventKey", ObservedEvent.Key)]
    [InlineData("InputEventMouseButton", ObservedEvent.MouseButton)]
    [InlineData("InputEventJoypadButton", ObservedEvent.JoypadButton)]
    [InlineData("InputEventJoypadMotion", ObservedEvent.JoypadMotion)]
    [InlineData("InputEventMouseMotion", ObservedEvent.Other)]
    [InlineData("InputEventAction", ObservedEvent.Other)]
    [InlineData("", ObservedEvent.Other)]
    [InlineData(null, ObservedEvent.Other)]
    public void An_engine_class_name_observes_the_event_kind_the_applier_classifies(string? className, ObservedEvent observed) =>
        Assert.Equal(observed, KeyBindingEvents.ObservedFor(className));

    [Theory]
    [InlineData("LeftX", ManagedAxis.Other)]
    [InlineData("LeftY", ManagedAxis.Other)]
    [InlineData("RightX", ManagedAxis.Other)]
    [InlineData("RightY", ManagedAxis.Other)]
    [InlineData("TriggerLeft", ManagedAxis.TriggerLeft)]
    [InlineData("TriggerRight", ManagedAxis.TriggerRight)]
    [InlineData("Max", ManagedAxis.Other)]
    [InlineData("", ManagedAxis.Other)]
    [InlineData(null, ManagedAxis.Other)]
    public void An_engine_axis_name_is_managed_only_for_the_two_triggers(string? axisName, ManagedAxis axis)
    {
        Assert.Equal(axis, KeyBindingEvents.AxisFor(axisName));
        Assert.Equal(KeyBindingEvents.IsManagedAxis(axis), KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, axis));
    }

    [Theory]
    [InlineData("InputEventJoypadMotion", "TriggerRight", true)]
    [InlineData("InputEventJoypadMotion", "TriggerLeft", true)]
    [InlineData("InputEventJoypadMotion", "LeftY", false)]
    [InlineData("InputEventJoypadButton", "TriggerRight", true)]
    [InlineData("InputEventKey", "TriggerRight", false)]
    [InlineData("InputEventMouseButton", "TriggerRight", false)]
    [InlineData("InputEventMouseMotion", "TriggerRight", false)]
    public void A_live_event_is_classified_from_its_engine_names_before_the_remap_owns_it(
        string className, string axisName, bool managed)
    {
        var live = new[]
        {
            new ObservedBinding(KeyBindingEvents.ObservedFor(className), KeyBindingEvents.AxisFor(axisName)),
            new ObservedBinding(ObservedEvent.Key),
        };

        Assert.Equal(managed ? new[] { 0 } : System.Array.Empty<int>(), KeyBindingEvents.ManagedIndexes(Pad, live));
    }

    [Fact]
    public void Every_default_controller_axis_plans_a_name_the_engine_axis_mapping_owns()
    {
        foreach (var action in DefaultBindableActions.All.Where(a => a.ControllerPrimary is not null))
        {
            var planned = KeyBindingEvents.Plan(new InputMapEntry(action.Id, Pad, action.ControllerPrimary!));
            if (planned.Kind != BindingEventKind.JoypadMotion) continue;
            Assert.True(KeyBindingEvents.IsManagedAxis(KeyBindingEvents.AxisFor(planned.Name)),
                action.Id + " plans " + planned.Name + " which the applier would leave unmanaged");
        }
    }

    sealed class FakeActionMap : IBindingEngine<string>
    {
        readonly Dictionary<string, ObservedBinding> _shapes = new();

        public List<LiveBinding<string>> Events { get; } = new();

        public List<string> Erased { get; } = new();

        public List<string> Added { get; } = new();

        public List<PlannedBinding> Requested { get; } = new();

        public bool Refuse { get; set; }

        public FakeActionMap With(string handle, string className, string? axisName = null)
        {
            var shape = new ObservedBinding(KeyBindingEvents.ObservedFor(className),
                axisName is null ? ManagedAxis.None : KeyBindingEvents.AxisFor(axisName));
            _shapes[handle] = shape;
            Events.Add(new LiveBinding<string>(handle, shape));
            return this;
        }

        public IReadOnlyList<LiveBinding<string>> Read(string action) => Events.ToArray();

        public void Erase(string action, string handle)
        {
            Erased.Add(handle);
            Events.RemoveAll(e => e.Handle == handle);
        }

        public void Add(string action, string handle)
        {
            Added.Add(handle);
            Events.Add(new LiveBinding<string>(handle, _shapes[handle]));
        }

        public string? Create(PlannedBinding planned)
        {
            Requested.Add(planned);
            if (Refuse || planned.Kind == BindingEventKind.None)
                return null;
            var handle = "created:" + planned.Kind + ":" + planned.Name;
            _shapes[handle] = new ObservedBinding(Observed(planned.Kind), KeyBindingEvents.AxisFor(planned.Name));
            return handle;
        }

        static ObservedEvent Observed(BindingEventKind kind) => kind switch
        {
            BindingEventKind.Key => KeyBindingEvents.ObservedFor("InputEventKey"),
            BindingEventKind.MouseButton => KeyBindingEvents.ObservedFor("InputEventMouseButton"),
            BindingEventKind.JoypadButton => KeyBindingEvents.ObservedFor("InputEventJoypadButton"),
            BindingEventKind.JoypadMotion => KeyBindingEvents.ObservedFor("InputEventJoypadMotion"),
            _ => ObservedEvent.Other,
        };
    }

    static FakeActionMap LivePadAndKeyboard() => new FakeActionMap()
        .With("key:W", "InputEventKey")
        .With("action:ui_accept", "InputEventAction")
        .With("pad:B", "InputEventJoypadButton")
        .With("motion:LeftY", "InputEventJoypadMotion", "LeftY")
        .With("motion:TriggerRight", "InputEventJoypadMotion", "TriggerRight");

    [Fact]
    public void A_controller_remap_erases_buttons_and_the_trigger_axis_and_keeps_the_stick()
    {
        var engine = LivePadAndKeyboard();

        new KeyBindingApplierCore<string>().Apply(
            new InputTarget("attack", Pad, true),
            new[] { new InputMapEntry("attack", Pad, "Pad Y") },
            engine);

        Assert.Equal(new[] { "pad:B", "motion:TriggerRight" }, engine.Erased);
        Assert.Equal(new[] { new PlannedBinding(BindingEventKind.JoypadButton, "Y", 0f) }, engine.Requested);
        Assert.Equal(new[] { "created:JoypadButton:Y" }, engine.Added);
        Assert.Equal(new[] { "key:W", "action:ui_accept", "motion:LeftY", "created:JoypadButton:Y" },
            engine.Events.Select(e => e.Handle));
    }

    [Fact]
    public void A_controller_axis_binding_creates_one_motion_event_for_its_own_device()
    {
        var engine = new FakeActionMap().With("pad:B", "InputEventJoypadButton");

        new KeyBindingApplierCore<string>().Apply(
            new InputTarget("attack", Pad, true),
            new[]
            {
                new InputMapEntry("attack", Pad, "Pad Axis TriggerRight"),
                new InputMapEntry("attack", Kb, "Mouse Left"),
                new InputMapEntry("iron_sights", Pad, "Pad Y"),
            },
            engine);

        Assert.Equal(new PlannedBinding(BindingEventKind.JoypadMotion, "TriggerRight", 1f), Assert.Single(engine.Requested));
        Assert.Equal("created:JoypadMotion:TriggerRight", Assert.Single(engine.Added));
        Assert.Equal(ManagedAxis.TriggerRight,
            Assert.Single(engine.Events, e => e.Handle == "created:JoypadMotion:TriggerRight").Shape.Axis);
        Assert.True(KeyBindingEvents.IsManaged(Pad, ObservedEvent.JoypadMotion, ManagedAxis.TriggerRight));
    }

    [Fact]
    public void A_reset_restores_the_originals_seen_on_the_first_apply()
    {
        var engine = new FakeActionMap()
            .With("pad:B", "InputEventJoypadButton")
            .With("motion:TriggerRight", "InputEventJoypadMotion", "TriggerRight");
        var core = new KeyBindingApplierCore<string>();

        core.Apply(new InputTarget("attack", Pad, true),
            new[] { new InputMapEntry("attack", Pad, "Pad Y") }, engine);
        Assert.Equal(new[] { "created:JoypadButton:Y" }, engine.Events.Select(e => e.Handle));

        engine.Erased.Clear();

        core.Apply(new InputTarget("attack", Pad, false),
            new[] { new InputMapEntry("attack", Pad, "Pad Axis TriggerRight") }, engine);

        Assert.Equal(new[] { "created:JoypadButton:Y" }, engine.Erased);
        Assert.Equal(new[] { "pad:B", "motion:TriggerRight" }, engine.Events.Select(e => e.Handle));
    }

    [Fact]
    public void A_second_remap_keeps_the_originals_so_a_later_reset_still_restores_them()
    {
        var engine = new FakeActionMap().With("motion:TriggerRight", "InputEventJoypadMotion", "TriggerRight");
        var core = new KeyBindingApplierCore<string>();

        core.Apply(new InputTarget("attack", Pad, true), new[] { new InputMapEntry("attack", Pad, "Pad Y") }, engine);
        core.Apply(new InputTarget("attack", Pad, true), new[] { new InputMapEntry("attack", Pad, "Pad X") }, engine);

        Assert.Equal(new[] { "motion:TriggerRight", "created:JoypadButton:Y" }, engine.Erased);

        core.Apply(new InputTarget("attack", Pad, false), Array.Empty<InputMapEntry>(), engine);

        Assert.Equal(new[] { "motion:TriggerRight" }, engine.Events.Select(e => e.Handle));
    }

    [Fact]
    public void An_untouched_action_is_left_alone_on_the_first_apply()
    {
        var engine = new FakeActionMap().With("key:W", "InputEventKey").With("pad:B", "InputEventJoypadButton");

        new KeyBindingApplierCore<string>().Apply(new InputTarget("sneak", Pad, false), Array.Empty<InputMapEntry>(), engine);

        Assert.Empty(engine.Erased);
        Assert.Empty(engine.Added);
        Assert.Equal(2, engine.Events.Count);
    }

    [Fact]
    public void A_keyboard_remap_erases_keys_and_mouse_buttons_and_leaves_pad_events_alone()
    {
        var engine = LivePadAndKeyboard();

        new KeyBindingApplierCore<string>().Apply(new InputTarget("attack", Kb, true),
            new[] { new InputMapEntry("attack", Kb, "F") }, engine);

        Assert.Equal(new[] { "key:W" }, engine.Erased);
        Assert.Equal(new[] { new PlannedBinding(BindingEventKind.Key, "F", 0f) }, engine.Requested);
        Assert.Equal(new[] { "action:ui_accept", "pad:B", "motion:LeftY", "motion:TriggerRight", "created:Key:F" },
            engine.Events.Select(e => e.Handle));
    }

    [Fact]
    public void A_binding_the_engine_cannot_build_still_erases_the_old_events_and_adds_nothing()
    {
        var engine = new FakeActionMap { Refuse = true }.With("pad:B", "InputEventJoypadButton");

        new KeyBindingApplierCore<string>().Apply(new InputTarget("sneak", Pad, true),
            new[] { new InputMapEntry("sneak", Pad, "Pad NotAButton") }, engine);

        Assert.Single(engine.Requested);
        Assert.Empty(engine.Added);
        Assert.Empty(engine.Events);
    }
}
