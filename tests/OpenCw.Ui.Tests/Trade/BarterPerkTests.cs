using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using Xunit;
using RadicleConditionFunctions = Radicle.Interop.ConditionFunctions;

namespace OpenCw.Ui.Tests;

public class BarterPerkTests
{
    static readonly FormKey Haggle = new("a.esm", 0xF01);
    static readonly FormKey Markup = new("a.esm", 0xF02);
    static readonly FormKey Gate = new("a.esm", 0xF03);

    sealed class FakeFacts : IRecipeFacts
    {
        public HashSet<FormKey> Perks { get; } = new();

        public double? Level => null;
        public bool? HasPerk(FormKey perk) => Perks.Contains(perk);
        public bool? HasKeyword(FormKey keyword) => null;
        public double? Global(FormKey global) => null;
        public int ItemCount(FormKey item) => 0;
    }

    sealed class Source : IBarterPerkSource
    {
        public List<HeldPerk> Player { get; } = new();
        public List<HeldPerk> Vendor { get; } = new();
        public Dictionary<FormKey, PerkEntryPointsRecord> Perks { get; } = new();
        public FakeFacts? PlayerFacts { get; set; }
        public FakeFacts? VendorFacts { get; set; }

        public IReadOnlyList<HeldPerk> Held(BarterActor actor) => actor == BarterActor.Player ? Player : Vendor;
        public PerkEntryPointsRecord? EntryPoints(FormKey perk) => Perks.GetValueOrDefault(perk);
        public IRecipeFacts? Facts(BarterActor actor) => actor == BarterActor.Player ? PlayerFacts : VendorFacts;
    }

    static PerkEntryPointEffect Row(byte entry, byte rank, float? value, byte function = BarterPerks.MultiplyValue,
        params PerkEntryPointTab[] tabs) =>
        new(rank, 0, entry, function, value is null ? null : (byte)1, value, tabs);

    static void Add(Source source, FormKey perk, params PerkEntryPointEffect[] effects) =>
        source.Perks[perk] = new PerkEntryPointsRecord(perk, effects);

    static PerkEntryPointTab Tab(sbyte runOn, string function, FormKey? param1, float value, uint conditionRunOn = 0) =>
        new(runOn, new[]
        {
            new RecipeCondition(
                new RadicleCondition(0, BitConverter.SingleToUInt32Bits(value), RadicleConditionFunctions.Id(function)!.Value, 0, 0, 0, conditionRunOn, 0, 0, null, null),
                null, param1, null, null),
        });

    static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-5f, expected + 1e-5f);

    static readonly BarterSettings Esm = new(Max: 3, Min: 2, BuyMax: 1.2f, SellMax: 0.8f);

    [Fact]
    public void No_source_or_no_held_perk_leaves_the_price_unchanged()
    {
        Near(1f, BarterPerks.Modifier(null, false));
        Near(1f, BarterPerks.Modifier(new Source(), true));
        var source = new Source();
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f));
        Near(1f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void Buying_uses_the_buy_entry_and_selling_the_sell_entry()
    {
        var source = new Source();
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.75f), Row(BarterPerks.ModSellPrices, 0, 1.25f));

        Near(0.75f, BarterPerks.Modifier(source, false));
        Near(1.25f, BarterPerks.Modifier(source, true));
    }

    [Fact]
    public void Only_the_effect_row_matching_the_held_rank_applies()
    {
        var source = new Source();
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.9f), Row(BarterPerks.ModBuyPrices, 1, 0.8f), Row(BarterPerks.ModBuyPrices, 2, 0.7f));
        source.Player.Add(new HeldPerk(Haggle, 2));

        Near(0.8f, BarterPerks.Modifier(source, false));

        source.Player[0] = new HeldPerk(Haggle, 3);
        Near(0.7f, BarterPerks.Modifier(source, false));

        source.Player[0] = new HeldPerk(Haggle, 9);
        Near(1f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void A_held_rank_below_one_reads_the_first_effect_row()
    {
        var source = new Source();
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.9f));
        source.Vendor.Add(new HeldPerk(Haggle, 0));

        Near(0.9f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void Player_and_vendor_perks_multiply()
    {
        var source = new Source();
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f));
        Add(source, Markup, Row(BarterPerks.ModBuyPrices, 0, 1.25f));
        source.Player.Add(new HeldPerk(Haggle, 1));
        source.Vendor.Add(new HeldPerk(Markup, 1));

        Near(0.625f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void Only_multiply_rows_with_a_finite_positive_value_apply()
    {
        var source = new Source();
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle,
            Row(BarterPerks.ModBuyPrices, 0, 2f, function: 1),
            Row(BarterPerks.ModBuyPrices, 0, 2f, function: 2),
            Row(BarterPerks.ModBuyPrices, 0, null),
            Row(BarterPerks.ModBuyPrices, 0, float.NaN),
            Row(BarterPerks.ModBuyPrices, 0, 0f),
            Row(BarterPerks.ModBuyPrices, 0, -1f),
            Row(BarterPerks.ModBuyPrices, 0, 0.5f));

        Near(0.5f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void A_perk_the_source_cannot_read_is_skipped()
    {
        var source = new Source();
        source.Player.Add(new HeldPerk(Haggle, 1));

        Near(1f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void A_tab_whose_conditions_are_false_for_the_holder_skips_the_row_and_a_true_one_keeps_it()
    {
        var source = new Source { PlayerFacts = new FakeFacts() };
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f, tabs: [Tab(0, "HasPerk", Gate, 1f)]));

        Near(1f, BarterPerks.Modifier(source, false));

        source.PlayerFacts.Perks.Add(Gate);
        Near(0.5f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void Without_facts_a_tabbed_row_applies_to_every_tab()
    {
        var source = new Source();
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f, tabs: [Tab(0, "HasPerk", Gate, 1f)]));

        Near(0.5f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void A_condition_the_facts_cannot_decide_keeps_the_row()
    {
        var source = new Source { PlayerFacts = new FakeFacts() };
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f, tabs: [Tab(0, "HasPerk", Gate, 1f, conditionRunOn: 1)]));

        Near(0.5f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void A_run_on_target_tab_is_judged_with_the_other_actor_s_facts()
    {
        var source = new Source { PlayerFacts = new FakeFacts(), VendorFacts = new FakeFacts() };
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f, tabs: [Tab(BarterPerks.RunOnTarget, "HasPerk", Gate, 1f)]));

        Near(1f, BarterPerks.Modifier(source, false));

        source.VendorFacts.Perks.Add(Gate);
        Near(0.5f, BarterPerks.Modifier(source, false));
    }

    [Fact]
    public void Every_tab_must_pass()
    {
        var source = new Source { PlayerFacts = new FakeFacts() };
        source.PlayerFacts.Perks.Add(Gate);
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.5f, tabs: [Tab(0, "HasPerk", Gate, 1f), Tab(0, "HasPerk", Markup, 1f)]));

        Near(1f, BarterPerks.Modifier(source, false));
    }

    [Theory]
    [InlineData(0.4f, 1.2f)]
    [InlineData(0.8f, 2.0f)]
    public void The_buy_floor_applies_after_the_perk(float perk, float expected) =>
        Near(expected, BarterFormula.Multiplier(Esm, 5f, false, perk));

    [Theory]
    [InlineData(1.25f, 0.5f)]
    [InlineData(3f, 0.8f)]
    public void The_sell_cap_applies_after_the_perk(float perk, float expected) =>
        Near(expected, BarterFormula.Multiplier(Esm, 5f, true, perk));

    [Fact]
    public void The_pricing_function_carries_each_side_s_perk_modifier()
    {
        var source = new Source();
        source.Player.Add(new HeldPerk(Haggle, 1));
        Add(source, Haggle, Row(BarterPerks.ModBuyPrices, 0, 0.8f), Row(BarterPerks.ModSellPrices, 0, 1.25f));

        var prices = BarterPricing.For(Esm, 5f, source);

        Near(2.0f, (float)prices(BarterSide.Vendor));
        Near(0.5f, (float)prices(BarterSide.Player));
        Assert.Equal(1.0, BarterPricing.For(Esm, null, source)(BarterSide.Vendor));
    }
}
