using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TargetKeywordTests
{
    const string Rifle = "000A01:Fallout4.esm";
    const string Pistol = "000A03:Fallout4.esm";
    const string Barrel = "000B01:Fallout4.esm";
    const string BarrelSlot = "000C01:Fallout4.esm";
    const string RifleKeyword = "000AA1:Fallout4.esm";
    const string PistolKeyword = "000AA2:Fallout4.esm";
    const string OtherKeyword = "000AA3:Fallout4.esm";
    const string Steel = "000D01:Fallout4.esm";
    const string BarrelRecipe = "000E01:Fallout4.esm";
    const string Weapon = "1346454871";

    static (FakeRecordReader Reader, PlayerItems Items) World(string[]? rifleKeywords, string[]? pistolKeywords, params string[] targets)
    {
        var reader = new FakeRecordReader();
        var rifle = reader.Add(Rifle, "WEAP", name: "Rifle");
        rifle.Lists["WEAP\\AttachParentSlots"] = [BarrelSlot];
        if (rifleKeywords is not null) rifle.Lists["WEAP\\Keywords"] = rifleKeywords;
        var pistol = reader.Add(Pistol, "WEAP", name: "Pistol");
        pistol.Lists["WEAP\\AttachParentSlots"] = [BarrelSlot];
        if (pistolKeywords is not null) pistol.Lists["WEAP\\Keywords"] = pistolKeywords;
        reader.Add(Steel, "MISC", name: "Steel");
        reader.Add(BarrelSlot, "KYWD", "ap_Gun_Barrel");
        var mod = reader.Add(Barrel, "OMOD", name: "Barrel");
        mod.Scalars["OMOD\\FormType"] = Weapon;
        mod.Scalars["OMOD\\AttachPoint"] = BarrelSlot;
        if (targets.Length > 0) mod.Lists["OMOD\\TargetOMODKeywords"] = targets;
        var cobj = reader.AddRecipe(BarrelRecipe, Barrel, "000F01:Fallout4.esm", [], (Steel, 1));
        cobj.Recipe = cobj.Recipe! with { Bench = null };
        var items = new PlayerItems();
        items.Add(Steel, 5);
        items.Add(Rifle, 1);
        items.Add(Pistol, 1);
        return (reader, items);
    }

    static RecordBenchRecipes Bench(FakeRecordReader reader, PlayerItems items, ObjectModRules? rules = null) =>
        new(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons, rules: rules);

    static string Target(RecordBenchRecipes bench, string id) => bench.Targets.Single(t => t.Id.StartsWith("item:" + id)).Id;

    [Fact]
    public void A_mod_is_offered_only_for_items_that_carry_one_of_its_target_keywords()
    {
        var (reader, items) = World([RifleKeyword, OtherKeyword], [PistolKeyword], RifleKeyword);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
        var pistol = Target(bench, Pistol);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(pistol, "ap_Gun_Barrel", BarrelRecipe));
        Assert.Empty(bench.SlotsOf(pistol));
        Assert.False(bench.Craft(pistol, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(5, items.CountOf(Steel));
    }

    [Fact]
    public void One_matching_keyword_of_several_is_enough()
    {
        var (reader, items) = World([PistolKeyword], [OtherKeyword], RifleKeyword, PistolKeyword);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(Target(bench, Pistol), "ap_Gun_Barrel", BarrelRecipe));
    }

    [Fact]
    public void An_item_with_no_keywords_does_not_match_a_mod_that_names_some()
    {
        var (reader, items) = World([], [PistolKeyword], RifleKeyword);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
    }

    [Fact]
    public void Turning_the_rule_off_offers_the_mod_for_every_item_with_the_slot()
    {
        var (reader, items) = World([RifleKeyword], [PistolKeyword], RifleKeyword);
        var bench = Bench(reader, items, new ObjectModRules(MatchTargetKeywords: false));
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Pistol), "ap_Gun_Barrel", BarrelRecipe));
    }

    [Fact]
    public void A_mod_with_no_target_keywords_is_offered_for_every_item_with_the_slot()
    {
        var (reader, items) = World([RifleKeyword], [PistolKeyword]);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Pistol), "ap_Gun_Barrel", BarrelRecipe));
    }

    [Fact]
    public void An_unreadable_item_keyword_row_never_hides_the_mod()
    {
        var (reader, items) = World(null, null, RifleKeyword);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
        Assert.True(bench.Craft(Target(bench, Pistol), "ap_Gun_Barrel", BarrelRecipe).Ok);
    }

    [Fact]
    public void Keyword_comparison_ignores_plugin_name_case()
    {
        var (reader, items) = World(["000AA1:FALLOUT4.ESM"], [], RifleKeyword);
        var bench = Bench(reader, items);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
    }
}
