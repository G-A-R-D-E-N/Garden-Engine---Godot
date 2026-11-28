using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ScrapYieldTests
{
    const string Item = "000001:Fallout4.esm";
    const string Steel = "000002:Fallout4.esm";
    const string Screws = "000003:Fallout4.esm";

    static FakeRecordReader Reader(string signature = "MISC")
    {
        var reader = new FakeRecordReader();
        reader.Add(Item, signature, "Wrench", "Wrench");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel");
        reader.Add(Screws, "CMPO", "c_Screws", "Screws");
        return reader;
    }

    static void Misc(FakeRecordReader reader, params (string Id, string Count)[] parts)
    {
        reader.Records[Item].Lists[ScrapYield.ObjectPath] = parts.Select(part => part.Id).ToArray();
        reader.Records[Item].Lists[ScrapYield.CountPath] = parts.Select(part => part.Count).ToArray();
    }

    static (string Id, string Count) Part(string id, uint count) => (id, count.ToString());

    [Fact]
    public void A_misc_item_yields_its_named_components_with_duplicates_merged()
    {
        var reader = Reader();
        Misc(reader, Part(Steel, 2), Part(Screws, 1), Part(Steel, 1));
        var yield = ScrapYield.Of(reader, Item)!;
        Assert.Equal([("Steel", 3), ("Screws", 1)], yield.Select(m => (m.Name, m.Needed)).ToArray());
    }

    [Fact]
    public void Junk_with_an_oversized_component_cannot_pay_for_crafting()
    {
        var reader = Reader();
        Misc(reader, Part(Screws, 1), Part(Steel, int.MaxValue - 1), Part(Steel, 1));
        Assert.Equal(int.MaxValue, ScrapYield.Of(reader, Item)!.Single(part => part.Id == Steel).Needed);

        Misc(reader, Part(Screws, 1), Part(Steel, int.MaxValue), Part(Steel, 1));
        var items = new PlayerItems();
        items.Add(Item, 1);
        var pool = new ComponentPool(reader, items);
        Assert.Null(ScrapYield.Of(reader, Item));
        Assert.Equal(0, pool.CountOf(Screws));
        Assert.False(pool.Consume([new(Screws, "Screws", 1)]));
        Assert.Equal(1, items.CountOf(Item));
    }

    [Fact]
    public void A_misc_item_with_no_components_yields_nothing_but_is_known()
    {
        var reader = Reader();
        Misc(reader);
        Assert.Empty(ScrapYield.Of(reader, Item)!);
    }

    [Fact]
    public void Unreadable_data_is_unknown_not_empty()
    {
        var reader = Reader();
        Assert.Null(ScrapYield.Of(reader, Item));
        Assert.Null(ScrapYield.Of(reader, "000999:Fallout4.esm"));
    }

    // Constraint: detects an unparsable count or mismatched row lengths being read as a known yield instead of unknown, or an empty link being kept as a part.
    [Fact]
    public void A_bad_count_or_mismatched_rows_are_unknown_and_an_empty_link_is_dropped()
    {
        var reader = Reader();
        reader.Records[Item].Lists[ScrapYield.ObjectPath] = [Steel, ""];
        reader.Records[Item].Lists[ScrapYield.CountPath] = ["2", "9"];
        Assert.Equal(["Steel"], ScrapYield.Of(reader, Item)!.Select(m => m.Name).ToArray());
        reader.Records[Item].Lists[ScrapYield.CountPath] = ["many", "9"];
        Assert.Null(ScrapYield.Of(reader, Item));
        reader.Records[Item].Lists[ScrapYield.CountPath] = ["2"];
        Assert.Null(ScrapYield.Of(reader, Item));
    }

    [Fact]
    public void Only_misc_items_have_a_verified_scrap_rule()
    {
        var reader = Reader("WEAP");
        Misc(reader, Part(Steel, 2));
        Assert.Null(ScrapYield.Of(reader, Item));
    }

    [Fact]
    public void Zero_count_parts_are_dropped()
    {
        var reader = Reader();
        Misc(reader, Part(Steel, 0), Part(Screws, 2));
        Assert.Equal(["Screws"], ScrapYield.Of(reader, Item)!.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void The_bench_exposes_the_breakdown()
    {
        var reader = Reader();
        Misc(reader, Part(Steel, 2));
        var bench = new RecordBenchRecipes(reader, new PlayerItems(), ["000A01:Fallout4.esm"], "Bench");
        Assert.Equal(2, bench.Scrap(Item)!.Single().Needed);
    }

    [Fact]
    public void A_bench_whose_recipes_are_all_gated_says_so()
    {
        var reader = Reader();
        reader.Add("000A01:Fallout4.esm", "KYWD", "bench");
        reader.Add("000C01:Fallout4.esm", "ALCH", "Stimpak", "Stimpak");
        var gated = new[] { new RecordCondition(new RadicleCondition(0, 0, Radicle.Interop.ConditionFunctions.Id("GetLevel")!.Value, 0, 0, 0, 0, 0, 0, null, null), null, null, null, null) };
        var record = reader.AddRecipe("000E01:Fallout4.esm", "000C01:Fallout4.esm", "000A01:Fallout4.esm", [], (Steel, 1));
        record.Recipe = record.Recipe! with { Conditions = gated };
        var bench = new RecordBenchRecipes(reader, new PlayerItems(), ["000A01:Fallout4.esm"], "Bench", null);
        Assert.Equal(0, bench.RecipeCount);
        Assert.Equal(1, bench.HiddenCount);
        Assert.Equal(RecordBenchRecipes.NothingToCraftText, bench.Notice);
    }

    [Fact]
    public void A_bench_with_no_recipes_at_all_has_no_notice()
    {
        var bench = new RecordBenchRecipes(Reader(), new PlayerItems(), ["000A01:Fallout4.esm"], "Bench");
        Assert.Null(bench.Notice);
    }

    [Fact]
    public void A_bench_with_some_recipes_listed_has_no_notice()
    {
        var reader = Reader();
        reader.Add("000A01:Fallout4.esm", "KYWD", "bench");
        reader.Add("000C01:Fallout4.esm", "ALCH", "Stimpak", "Stimpak");
        reader.AddRecipe("000E01:Fallout4.esm", "000C01:Fallout4.esm", "000A01:Fallout4.esm", [], (Steel, 1));
        var gated = new[] { new RecordCondition(new RadicleCondition(0, 0, Radicle.Interop.ConditionFunctions.Id("GetLevel")!.Value, 0, 0, 0, 0, 0, 0, null, null), null, null, null, null) };
        reader.AddRecipe("000E02:Fallout4.esm", "000C01:Fallout4.esm", "000A01:Fallout4.esm", [], (Steel, 1)).Recipe = reader.Records["000E02:Fallout4.esm"].Recipe! with { Conditions = gated };
        var bench = new RecordBenchRecipes(reader, new PlayerItems(), ["000A01:Fallout4.esm"], "Bench", null);
        Assert.Equal(1, bench.RecipeCount);
        Assert.Equal(1, bench.HiddenCount);
        Assert.Null(bench.Notice);
    }
}
