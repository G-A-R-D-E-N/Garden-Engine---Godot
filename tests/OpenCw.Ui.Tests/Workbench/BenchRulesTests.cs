using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BenchRulesTests
{
    const string IgnoreKeyword = "000100:Fallout4.esm";
    const string PieceKeyword = "000101:Fallout4.esm";
    const string ChemKeyword = "000102:Fallout4.esm";
    const string CookingKeyword = "000103:Fallout4.esm";
    const string OtherKeyword = "000104:Fallout4.esm";
    const string Torso = "000A01:Fallout4.esm";
    const string Vest = "000A02:Fallout4.esm";
    const string Mod = "000B01:Fallout4.esm";
    const string Slot = "000C01:Fallout4.esm";
    const string Steel = "000D01:Fallout4.esm";
    const string Wire = "000D02:Fallout4.esm";
    const string Recipe = "000E01:Fallout4.esm";
    const string Examine = "000F01:Fallout4.esm";
    const string ChemLoop = "000F02:Fallout4.esm";
    const string CookLoop = "000F03:Fallout4.esm";
    const string SteelSound = "000F04:Fallout4.esm";
    const string WireSound = "000F05:Fallout4.esm";
    const string Armor = "1330467393";

    static void DefaultObject(FakeRecordReader reader, string id, string editorId, string target) =>
        reader.Add(id, "DFOB", editorId).Scalars["DFOB\\Object"] = target;

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        DefaultObject(reader, "001001:Fallout4.esm", "IgnoreWorkbenchDataKeyword_DO", IgnoreKeyword);
        DefaultObject(reader, "001002:Fallout4.esm", "PowerArmorArmorKeyword_DO", PieceKeyword);
        DefaultObject(reader, "001003:Fallout4.esm", "WorkbenchChemlabKeyword_DO", ChemKeyword);
        DefaultObject(reader, "001004:Fallout4.esm", "WorkbenchCookingKeyword_DO", CookingKeyword);
        DefaultObject(reader, "001005:Fallout4.esm", "CraftingLoopSound_Examine_DO", Examine);
        DefaultObject(reader, "001006:Fallout4.esm", "CraftingLoopSound_Chemlab_DO", ChemLoop);
        DefaultObject(reader, "001007:Fallout4.esm", "CraftingLoopSound_Cooking_DO", CookLoop);
        reader.Add(Examine, "SNDR", "UIModsBuildPartLPM");
        reader.Add(ChemLoop, "SNDR", "UICookingChemsLPM");
        reader.Add(CookLoop, "SNDR", "UICookingCookLPM");
        reader.Add(SteelSound, "SNDR", "SteelCraft");
        reader.Add(WireSound, "SNDR", "WireCraft");
        reader.Add(Steel, "CMPO", "c_Steel").Links["CUSD"] = [SteelSound];
        reader.Add(Wire, "CMPO", "c_Wire").Links["CUSD"] = [WireSound];
        return reader;
    }

    static (FakeRecordReader Reader, PlayerItems Items) PowerArmorWorld()
    {
        var reader = Data();
        reader.Add(Torso, "ARMO", name: "Torso").Links["KWDA"] = [PieceKeyword];
        reader.Add(Vest, "ARMO", name: "Vest").Links["KWDA"] = [OtherKeyword];
        reader.Add(Slot, "KYWD", "ap_Torso");
        var mod = reader.Add(Mod, "OMOD", name: "Plating");
        mod.Scalars["OMOD\\FormType"] = Armor;
        mod.Scalars["OMOD\\AttachPoint"] = Slot;
        var cobj = reader.AddRecipe(Recipe, Mod, "000F01:Fallout4.esm", [], (Steel, 1));
        cobj.Recipe = cobj.Recipe! with { Bench = null };
        reader.Records[Torso].Lists["ARMO\\AttachParentSlots"] = [Slot];
        reader.Records[Vest].Lists["ARMO\\AttachParentSlots"] = [Slot];
        var items = new PlayerItems();
        items.Add(Steel, 5);
        items.Add(Torso, 1);
        items.Add(Vest, 1);
        return (reader, items);
    }

    [Fact]
    public void The_ignore_keyword_makes_a_furniture_a_non_bench()
    {
        var reader = Data();
        Assert.Null(BenchRules.EffectiveType(reader, BenchKinds.PowerArmor, [IgnoreKeyword], false));
        Assert.Null(BenchRules.EffectiveType(reader, BenchKinds.Weapons, [OtherKeyword, IgnoreKeyword], false));
        Assert.Equal(BenchKinds.PowerArmor, BenchRules.EffectiveType(reader, BenchKinds.PowerArmor, [OtherKeyword], false));
    }

    [Fact]
    public void A_power_armor_frame_is_not_a_bench_but_a_frame_flagged_armor_bench_is_unchanged()
    {
        var reader = Data();
        Assert.Null(BenchRules.EffectiveType(reader, BenchKinds.PowerArmor, [], true));
        Assert.Equal(BenchKinds.Armor, BenchRules.EffectiveType(reader, BenchKinds.Armor, [], true));
        Assert.Null(BenchRules.EffectiveType(reader, null, [], false));
    }

    [Fact]
    public void An_unresolved_ignore_object_changes_nothing()
    {
        var reader = new FakeRecordReader();
        Assert.Equal(BenchKinds.PowerArmor, BenchRules.EffectiveType(reader, BenchKinds.PowerArmor, [IgnoreKeyword], false));
    }

    [Fact]
    public void Only_a_power_armor_bench_has_a_piece_keyword_and_an_unresolved_one_fails_closed()
    {
        Assert.Equal(PieceKeyword, BenchRules.PieceKeyword(Data(), BenchKinds.PowerArmor));
        Assert.Null(BenchRules.PieceKeyword(Data(), BenchKinds.Armor));
        Assert.Null(BenchRules.PieceKeyword(Data(), null));
        Assert.Equal(string.Empty, BenchRules.PieceKeyword(new FakeRecordReader(), BenchKinds.PowerArmor));
    }

    [Fact]
    public void A_power_armor_bench_targets_only_pieces_with_the_keyword_and_mods_them()
    {
        var (reader, items) = PowerArmorWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Power Armor Station", null, BenchKinds.PowerArmor, BenchRules.PieceKeyword(reader, BenchKinds.PowerArmor));
        Assert.Equal(["Torso"], bench.Targets.Select(t => t.Name));
        var torso = bench.Targets.Single().Id;
        Assert.True(bench.Craft(torso, "ap_Torso", Recipe).Ok);
        Assert.Equal(1, items.CountOf(Torso, new ItemState(new[] { Mod })));
    }

    [Fact]
    public void An_armor_bench_still_targets_every_armor_item()
    {
        var (reader, items) = PowerArmorWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Armor Workbench", null, BenchKinds.Armor, BenchRules.PieceKeyword(reader, BenchKinds.Armor));
        Assert.Equal(["Torso", "Vest"], bench.Targets.Select(t => t.Name));
    }

    [Fact]
    public void A_power_armor_bench_with_an_unresolved_piece_keyword_lists_no_targets()
    {
        var (reader, items) = PowerArmorWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Power Armor Station", null, BenchKinds.PowerArmor, string.Empty);
        Assert.Empty(bench.Targets);
    }

    [Fact]
    public void A_robot_bench_has_a_notice_and_no_other_type_does()
    {
        Assert.Equal(BenchKinds.RobotNotice, BenchRules.Notice(BenchKinds.RobotMod));
        foreach (var other in new int?[] { null, 0, 1, 2, 5, 7, 8 }) Assert.Null(BenchRules.Notice(other));
    }

    [Theory]
    [InlineData(BenchKinds.Weapons)]
    [InlineData(BenchKinds.Armor)]
    [InlineData(BenchKinds.PowerArmor)]
    public void A_weapon_armor_or_power_armor_bench_plays_the_examine_loop(int type) =>
        Assert.Equal(["UIModsBuildPartLPM"], BenchSounds.Resolve(Data(), type, [OtherKeyword], []));

    [Fact]
    public void A_chem_or_cooking_keyword_picks_its_own_loop_before_the_type()
    {
        var reader = Data();
        Assert.Equal(["UICookingChemsLPM"], BenchSounds.Resolve(reader, BenchKinds.Weapons, [ChemKeyword], []));
        Assert.Equal(["UICookingCookLPM"], BenchSounds.Resolve(reader, null, [CookingKeyword], []));
    }

    [Fact]
    public void A_bench_that_matches_nothing_and_a_robot_bench_stay_silent()
    {
        var reader = Data();
        Assert.Empty(BenchSounds.Resolve(reader, null, [OtherKeyword], []));
        Assert.Empty(BenchSounds.Resolve(reader, 1, [], []));
        Assert.Empty(BenchSounds.Resolve(reader, BenchKinds.RobotMod, [], []));
    }

    [Fact]
    public void A_loop_object_that_does_not_resolve_to_a_sound_is_silent()
    {
        var reader = Data();
        reader.Records[Examine] = new FakeRecord { Signature = "MISC", EditorId = "NotASound" };
        Assert.Empty(BenchSounds.Resolve(reader, BenchKinds.Armor, [], []));
        Assert.Empty(BenchSounds.Resolve(new FakeRecordReader(), BenchKinds.Armor, [], []));
    }

    [Fact]
    public void The_craft_sound_can_be_turned_off()
    {
        var settings = new BenchSettings { PlayCraftSound = false };
        Assert.Empty(BenchSounds.Resolve(Data(), BenchKinds.Armor, [], [], settings));
        Assert.Equal(["SteelCraft"], BenchSounds.Resolve(Data(), BenchKinds.Armor, [], [Steel], settings));
    }

    [Fact]
    public void By_default_only_the_first_component_use_sound_plays_after_the_craft_sound()
    {
        Assert.Equal(["UIModsBuildPartLPM", "SteelCraft"], BenchSounds.Resolve(Data(), BenchKinds.Armor, [], [Steel, Wire]));
    }

    [Fact]
    public void The_component_sound_count_is_a_setting_and_repeats_and_non_sounds_are_skipped()
    {
        var reader = Data();
        var settings = new BenchSettings { MaxComponentSounds = 2 };
        Assert.Equal(["UIModsBuildPartLPM", "SteelCraft", "WireCraft"], BenchSounds.Resolve(reader, BenchKinds.Armor, [], [Steel, Steel, Wire], settings));
        reader.Records[Steel].Links["CUSD"] = [Torso];
        Assert.Equal(["UIModsBuildPartLPM", "WireCraft"], BenchSounds.Resolve(reader, BenchKinds.Armor, [], [Steel, Wire], new BenchSettings { MaxComponentSounds = 2 }));
        Assert.Equal(["UIModsBuildPartLPM"], BenchSounds.Resolve(reader, BenchKinds.Armor, [], [Steel, Wire], new BenchSettings { MaxComponentSounds = 0 }));
    }

    [Fact]
    public void A_craft_reports_its_component_ids_once_and_a_detach_or_failed_craft_reports_nothing()
    {
        var (reader, items) = PowerArmorWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Power Armor Station", null, BenchKinds.PowerArmor, PieceKeyword);
        var crafted = new List<IReadOnlyList<string>>();
        bench.Crafted += crafted.Add;
        var torso = bench.Targets.Single().Id;
        Assert.True(bench.Craft(torso, "ap_Torso", Recipe).Ok);
        Assert.Equal([[Steel]], crafted);
        var modded = bench.Targets.Single().Id;
        Assert.True(bench.Craft(modded, RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Mod).Ok);
        Assert.False(bench.Craft(bench.Targets.Single().Id, "ap_Torso", "missing").Ok);
        Assert.Single(crafted);
    }
}
