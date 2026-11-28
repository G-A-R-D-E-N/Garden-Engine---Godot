using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PickpocketSkillsTests
{
    static readonly PickpocketSettings Esm = new(
        ActorSkillBase: 20, ActorSkillMult: 1, TargetSkillBase: 20, TargetSkillMult: -0.25f,
        AmountBase: 0, AmountMult: -0.1f, WeightBase: 0, WeightMult: -4, MinChance: 0, MaxChance: 90, PlantChanceMult: 1);

    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static float? Resolve(Dictionary<FormKey, float?> stored, Dictionary<FormKey, FormKey> templates, FormKey npc) =>
        PickpocketSkills.Resolve(key => stored.GetValueOrDefault(key), key => templates.TryGetValue(key, out var template) ? template : null, npc);

    static (Dictionary<FormKey, float?> Stored, Dictionary<FormKey, FormKey> Templates) Chain(int hops)
    {
        var stored = new Dictionary<FormKey, float?> { [Key((uint)(0x100 + hops))] = 12 };
        var templates = new Dictionary<FormKey, FormKey>();
        for (var i = 0; i < hops; i++) templates[Key((uint)(0x100 + i))] = Key((uint)(0x101 + i));
        return (stored, templates);
    }

    [Fact]
    public void A_stored_value_above_zero_wins_over_the_template()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 7, [Key(2)] = 3 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2) };
        Assert.Equal<float?>(7, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void A_stored_zero_takes_the_template_value()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0, [Key(2)] = 12 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2) };
        Assert.Equal<float?>(12, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void An_absent_value_takes_the_template_value()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(2)] = 12 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2) };
        Assert.Equal<float?>(12, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void A_stored_zero_without_a_template_is_zero()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(1) };
        Assert.Equal<float?>(0, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void An_absent_value_without_a_template_is_null()
    {
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(1) };
        Assert.Null(Resolve(new Dictionary<FormKey, float?>(), templates, Key(1)));
    }

    [Fact]
    public void A_stored_zero_with_a_blocked_template_link_is_null()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0 };
        Assert.Null(Resolve(stored, new Dictionary<FormKey, FormKey>(), Key(1)));
    }

    [Fact]
    public void A_template_that_stores_nothing_is_null()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2) };
        Assert.Null(Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void A_template_linked_to_itself_ends_the_walk_at_its_own_value()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(1) };
        Assert.Equal<float?>(0, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void The_template_chain_is_followed_for_eight_hops_and_no_further()
    {
        var eight = Chain(8);
        Assert.Equal<float?>(12, Resolve(eight.Stored, eight.Templates, Key(0x100)));
        var nine = Chain(9);
        Assert.Null(Resolve(nine.Stored, nine.Templates, Key(0x100)));
    }

    [Fact]
    public void A_zero_perception_without_a_template_gives_the_hand_computed_chance()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 0 };
        var perception = Resolve(stored, new Dictionary<FormKey, FormKey> { [Key(1)] = Key(1) }, Key(1));
        Assert.Equal<float?>(0, perception);
        Assert.Equal(40, PickpocketFormula.Chance(Esm, 5, perception!.Value, 10, 1, false));
    }

    [Fact]
    public void A_stats_templated_target_answers_unproven_and_takes_the_template_stored_value()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(2)] = 6 };
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2), [Key(2)] = Key(2) };
        Assert.Equal<float?>(6, Resolve(stored, templates, Key(1)));
    }

    [Fact]
    public void A_stats_templated_target_whose_template_is_unproven_too_is_null()
    {
        var templates = new Dictionary<FormKey, FormKey> { [Key(1)] = Key(2), [Key(2)] = Key(2) };
        Assert.Null(Resolve(new Dictionary<FormKey, float?>(), templates, Key(1)));
    }

    [Fact]
    public void A_stored_perception_and_agility_give_the_hand_computed_chance()
    {
        var stored = new Dictionary<FormKey, float?> { [Key(1)] = 8 };
        var perception = Resolve(stored, new Dictionary<FormKey, FormKey> { [Key(1)] = Key(1) }, Key(1));
        Assert.Equal<float?>(8, perception);
        Assert.Equal(42, PickpocketFormula.Chance(Esm, 9, perception!.Value, 10, 1, false));
    }
}
