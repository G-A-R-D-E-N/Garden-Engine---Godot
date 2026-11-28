using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingInputMapSignatureTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;

    static KeyBindingModel Make(params BindableAction[] actions) => new(actions, new InMemoryFileStore());

    [Fact]
    public void The_signature_spells_out_action_device_override_flag_and_codes_in_order()
    {
        var model = Make(new BindableAction("interact", "Interact", "Gameplay", "E", "Q", "Pad X"));

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal("activate:0=E,Q,;activate:1=Pad X,;", plan.Signature);
    }

    [Fact]
    public void Entries_and_targets_follow_action_order_then_keyboard_then_controller_then_primary_then_secondary()
    {
        var model = Make(
            new BindableAction("zoom", "Zoom", "Gameplay", "Z", "X", "Pad LB", "Pad RB"),
            new BindableAction("aim", "Aim", "Gameplay", "Mouse Right"));

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal(
            new[]
            {
                new InputMapEntry("zoom", Kb, "Z"), new InputMapEntry("zoom", Kb, "X"),
                new InputMapEntry("zoom", Pad, "Pad LB"), new InputMapEntry("zoom", Pad, "Pad RB"),
                new InputMapEntry("iron_sights", Kb, "Mouse Right"),
            },
            plan.Entries.ToArray());
        Assert.Equal(
            new[]
            {
                new InputTarget("zoom", Kb, false), new InputTarget("zoom", Pad, false),
                new InputTarget("iron_sights", Kb, false), new InputTarget("iron_sights", Pad, false),
            },
            plan.Targets.ToArray());
        Assert.Equal("zoom:0=Z,X,;zoom:1=Pad LB,Pad RB,;iron_sights:0=Mouse Right,;iron_sights:1=;", plan.Signature);
    }

    [Fact]
    public void An_action_with_no_bindings_still_gets_a_target_per_device_and_no_entries()
    {
        var plan = KeyBindingInputMap.Build(Make(new BindableAction("jump", "Jump", "Gameplay")));

        Assert.Empty(plan.Entries);
        Assert.Equal(new[] { new InputTarget("jump", Kb, false), new InputTarget("jump", Pad, false) }, plan.Targets.ToArray());
        Assert.Equal("jump:0=;jump:1=;", plan.Signature);
    }

    [Fact]
    public void A_model_with_no_actions_builds_an_empty_plan()
    {
        var plan = KeyBindingInputMap.Build(Make());

        Assert.Empty(plan.Entries);
        Assert.Empty(plan.Targets);
        Assert.Equal("", plan.Signature);
    }

    [Fact]
    public void Unbinding_the_primary_slot_overrides_the_device_and_keeps_the_secondary_entry()
    {
        var model = Make(new BindableAction("interact", "Interact", "Gameplay", "E", "Q", "Pad X"));
        model.Unbind("interact", Kb, BindingSlot.Primary);

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal("activate:0!Q,;activate:1=Pad X,;", plan.Signature);
        Assert.Equal(new[] { new InputMapEntry("activate", Kb, "Q"), new InputMapEntry("activate", Pad, "Pad X") }, plan.Entries.ToArray());
        Assert.Equal(new[] { new InputTarget("activate", Kb, true), new InputTarget("activate", Pad, false) }, plan.Targets.ToArray());
    }

    [Fact]
    public void Unbinding_a_slot_that_was_already_empty_still_counts_as_default()
    {
        var model = Make(new BindableAction("jump", "Jump", "Gameplay", "Space"));
        model.Unbind("jump", Kb, BindingSlot.Secondary);

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal("jump:0=Space,;jump:1=;", plan.Signature);
        Assert.All(plan.Targets, t => Assert.False(t.Overridden));
    }

    [Fact]
    public void Aliased_actions_use_the_alias_in_entries_targets_and_signature_but_unlisted_ids_pass_through()
    {
        var model = Make(
            new BindableAction("aim", "Aim", "Gameplay", "Mouse Right"),
            new BindableAction("Interact", "Case differs", "Gameplay", "R"));

        var plan = KeyBindingInputMap.Build(model);

        Assert.Equal("iron_sights", KeyBindingInputMap.ActionNameFor("aim"));
        Assert.Equal("Interact", KeyBindingInputMap.ActionNameFor("Interact"));
        Assert.Equal("", KeyBindingInputMap.ActionNameFor(""));
        Assert.StartsWith("iron_sights:0=Mouse Right,;iron_sights:1=;Interact:0=R,;", plan.Signature);
    }
}
