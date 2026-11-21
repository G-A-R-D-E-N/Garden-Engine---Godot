using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BindingAdoptionTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const BindingSlot P = BindingSlot.Primary;
    const BindingSlot S = BindingSlot.Secondary;

    static KeyBindingModel Model(InMemoryFileStore? store = null) => new(DefaultBindableActions.All, store ?? new InMemoryFileStore());

    static Dictionary<string, IReadOnlyList<string>> Live(params (string Id, string[] Codes)[] entries) =>
        entries.ToDictionary(e => e.Id, e => (IReadOnlyList<string>)e.Codes);

    [Fact]
    public void A_title_rebind_shows_up_in_the_pause_model_and_is_saved()
    {
        var store = new InMemoryFileStore();
        var model = Model(store);

        var count = BindingAdoption.Adopt(model, Live(("jump", new[] { "G" })));

        Assert.Equal(1, count);
        Assert.Equal("G", model.Get("jump", Kb, P));
        Assert.False(model.IsDefault("jump", Kb));
        Assert.Contains("\"G\"", store.Files[KeyBindingModel.FileName]);
    }

    [Fact]
    public void Live_events_equal_to_the_defaults_adopt_nothing_even_when_the_action_has_two_keys()
    {
        var store = new InMemoryFileStore();
        var model = Model(store);

        var count = BindingAdoption.Adopt(model, Live(("sneak", new[] { "C", "Ctrl" }), ("jump", new[] { "Space" }), ("move_forward", new[] { "W" })));

        Assert.Equal(0, count);
        Assert.False(store.Files.ContainsKey(KeyBindingModel.FileName));
    }

    [Fact]
    public void A_pause_override_that_the_title_mirror_agrees_with_is_left_alone()
    {
        var model = Model();
        model.BeginCapture("sneak", Kb, P);
        model.Capture("G");
        var changes = 0;
        model.Changed += () => changes++;

        var count = BindingAdoption.Adopt(model, Live(("sneak", new[] { "G" })));

        Assert.Equal(0, count);
        Assert.Equal("Ctrl", model.Get("sneak", Kb, S));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Adopting_replaces_the_secondary_and_takes_the_key_from_another_action()
    {
        var model = Model();

        BindingAdoption.Adopt(model, Live(("sneak", new[] { "Q" })));

        Assert.Equal("Q", model.Get("sneak", Kb, P));
        Assert.Null(model.Get("sneak", Kb, S));
        Assert.Null(model.Get("vats", Kb, P));
    }

    [Fact]
    public void A_title_swap_is_adopted_for_both_actions()
    {
        var model = Model();

        var count = BindingAdoption.Adopt(model, Live(("jump", new[] { "E" }), ("interact", new[] { "Space" })));

        Assert.Equal(2, count);
        Assert.Equal("E", model.Get("jump", Kb, P));
        Assert.Equal("Space", model.Get("interact", Kb, P));
    }

    [Fact]
    public void Actions_the_title_does_not_report_or_unbound_ones_are_ignored()
    {
        var model = Model();

        var count = BindingAdoption.Adopt(model, Live(("jump", new string[0]), ("no_such_action", new[] { "G" })));

        Assert.Equal(0, count);
        Assert.True(model.IsDefault("jump", Kb));
    }

    [Fact]
    public void An_adopted_binding_survives_a_reload_and_resets_to_the_default()
    {
        var store = new InMemoryFileStore();
        BindingAdoption.Adopt(Model(store), Live(("jump", new[] { "G" })));

        var reloaded = Model(store);
        Assert.Equal("G", reloaded.Get("jump", Kb, P));

        reloaded.Reset("jump", Kb);
        Assert.Equal("Space", reloaded.Get("jump", Kb, P));
        Assert.True(reloaded.IsDefault("jump", Kb));
    }
}
