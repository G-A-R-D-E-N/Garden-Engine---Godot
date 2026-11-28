using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using Xunit;
using FormKey = Radicle.Interop.FormKey;
using RadicleConditionFunctions = Radicle.Interop.ConditionFunctions;

namespace OpenCw.Ui.Tests;

public class RecipeConditionTests
{
    const string Bench = "000A01:Fallout4.esm";
    const string Recipe = "000E01:Fallout4.esm";
    const string Stim = "000C01:Fallout4.esm";
    const string Leather = "000B01:Fallout4.esm";

    sealed class Facts : IRecipeFacts
    {
        public double? Level { get; set; }
        public HashSet<FormKey> Perks { get; } = new();
        public int Items { get; set; }

        public bool? HasPerk(FormKey perk) => Perks.Contains(perk);
        public bool? HasKeyword(FormKey keyword) => null;
        public double? Global(FormKey global) => null;
        public int ItemCount(FormKey item) => Items;
    }

    static FormKey Perk => FakeRecordReader.Key("000F01:Fallout4.esm");

    static RecordCondition Row(string function, FormKey? param1, float value, byte flags = 0, uint runOn = 0, uint reference = 0) =>
        new(new RadicleCondition(flags, BitConverter.SingleToUInt32Bits(value), RadicleConditionFunctions.Id(function)!.Value, 0, 0, 0, runOn, reference, 0, null, null),
            null, param1, null, null);

    static RecordBenchRecipes Bench1(IReadOnlyList<RecordCondition>? conditions, IRecipeFacts? facts)
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "bench");
        reader.Add(Leather, "MISC", "Leather", "Leather");
        reader.Add(Stim, "ALCH", "Stimpak", "Stimpak");
        var record = reader.AddRecipe(Recipe, Stim, Bench, [], (Leather, 1));
        record.Recipe = record.Recipe! with { Conditions = conditions };
        return new RecordBenchRecipes(reader, new PlayerItems(), [Bench], "Chem Station", facts);
    }

    [Fact]
    public void A_recipe_without_conditions_is_listed()
    {
        Assert.Equal(1, Bench1([], new Facts()).RecipeCount);
        Assert.Equal(1, Bench1([], null).RecipeCount);
    }

    [Fact]
    public void A_perk_condition_lists_the_recipe_only_for_a_player_with_the_perk()
    {
        var rows = new[] { Row("HasPerk", Perk, 1f) };
        var without = new Facts();
        var with = new Facts();
        with.Perks.Add(Perk);
        Assert.Equal(0, Bench1(rows, without).RecipeCount);
        Assert.Equal(1, Bench1(rows, with).RecipeCount);
    }

    [Fact]
    public void A_level_condition_compares_against_the_player_level()
    {
        var rows = new[] { Row("GetLevel", null, 5f, flags: 0x60) };
        Assert.Equal(0, Bench1(rows, new Facts { Level = 4 }).RecipeCount);
        Assert.Equal(1, Bench1(rows, new Facts { Level = 5 }).RecipeCount);
    }

    [Fact]
    public void Unreadable_or_unanswerable_conditions_hide_the_recipe()
    {
        var perkRow = new[] { Row("HasPerk", Perk, 1f) };
        Assert.Equal(0, Bench1(null, new Facts()).RecipeCount);
        Assert.Equal(0, Bench1(perkRow, null).RecipeCount);
        Assert.Equal(0, Bench1([Row("GetLevel", null, 0f, flags: 0x60)], new Facts()).RecipeCount);
        Assert.Equal(0, Bench1([Row("HasPerk", null, 1f)], new Facts()).RecipeCount);
        Assert.Equal(0, Bench1([Row("HasPerk", Perk, 1f, runOn: 1)], new Facts()).RecipeCount);
    }

    [Fact]
    public void A_perk_row_written_against_the_player_reference_is_evaluated_for_the_player()
    {
        var rows = new[] { Row("HasPerk", Perk, 1f, runOn: 2, reference: 0x14) };
        var with = new Facts();
        with.Perks.Add(Perk);
        Assert.Equal(0, Bench1(rows, new Facts()).RecipeCount);
        Assert.Equal(1, Bench1(rows, with).RecipeCount);
    }

    [Fact]
    public void A_row_against_any_other_reference_or_target_stays_hidden_even_for_a_player_with_the_perk()
    {
        var with = new Facts();
        with.Perks.Add(Perk);
        Assert.Equal(0, Bench1([Row("HasPerk", Perk, 1f, runOn: 2, reference: 0x15)], with).RecipeCount);
        Assert.Equal(0, Bench1([Row("HasPerk", Perk, 1f, runOn: 2, reference: 0)], with).RecipeCount);
        Assert.Equal(0, Bench1([Row("HasPerk", Perk, 1f, runOn: 1, reference: 0x14)], with).RecipeCount);
        Assert.Equal(0, Bench1([Row("HasPerk", Perk, 1f, runOn: 3)], with).RecipeCount);
    }

    [Fact]
    public void The_player_level_source_feeds_a_level_condition()
    {
        var rows = new[] { Row("GetLevel", null, 5f, flags: 0x60) };
        Assert.Equal(0, Bench1(rows, new PlayerRecipeFacts(new PlayerItems())).RecipeCount);
        Assert.Equal(0, Bench1(rows, new PlayerRecipeFacts(new PlayerItems(), () => 4)).RecipeCount);
        Assert.Equal(1, Bench1(rows, new PlayerRecipeFacts(new PlayerItems(), () => 5)).RecipeCount);
    }

    [Fact]
    public void An_item_count_condition_reads_the_player_stock()
    {
        var rows = new[] { Row("GetItemCount", FakeRecordReader.Key(Leather), 1f, flags: 0x60) };
        Assert.Equal(0, Bench1(rows, new Facts { Items = 0 }).RecipeCount);
        Assert.Equal(1, Bench1(rows, new Facts { Items = 1 }).RecipeCount);
        var items = new PlayerItems();
        Assert.Equal(0, Bench1(rows, new PlayerRecipeFacts(items)).RecipeCount);
        items.Add(Leather, 1);
        Assert.Equal(1, Bench1(rows, new PlayerRecipeFacts(items)).RecipeCount);
    }
}
