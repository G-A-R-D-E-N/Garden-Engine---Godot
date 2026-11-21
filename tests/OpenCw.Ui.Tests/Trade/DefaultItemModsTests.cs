using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DefaultItemModsTests
{
    const string Rifle = "000A01:Fallout4.esm";
    const string Vest = "000A02:Fallout4.esm";
    const string ModA = "000B01:Fallout4.esm";
    const string ModB = "000B02:Fallout4.esm";
    const string ModC = "000B03:Fallout4.esm";
    const string ModD = "000B04:Fallout4.esm";
    const string NotAMod = "000B05:Fallout4.esm";
    const string BarrelSlot = "000C01:Fallout4.esm";
    const string Steel = "000D01:Fallout4.esm";
    const string BarrelRecipe = "000E01:Fallout4.esm";
    const string DefaultLoose = "000B11:Fallout4.esm";
    const string Weapon = "1346454871";

    static FakeRecordReader Reader(string[] flags, string[] counts, string[] words)
    {
        var reader = new FakeRecordReader();
        var rifle = reader.Add(Rifle, "WEAP", name: "Rifle");
        rifle.Lists["WEAP\\ObjectTemplateDefault"] = flags;
        rifle.Lists["WEAP\\ObjectTemplateIncludeCount"] = counts;
        rifle.Lists["WEAP\\ObjectTemplateIncludeMod"] = words;
        rifle.RawForms[0xB01] = ModA;
        rifle.RawForms[0xB02] = ModB;
        rifle.RawForms[0xB03] = ModC;
        rifle.RawForms[0xB04] = ModD;
        rifle.RawForms[0xB05] = NotAMod;
        foreach (var mod in new[] { ModA, ModB, ModC, ModD }) reader.Add(mod, "OMOD");
        reader.Add(NotAMod, "MISC");
        return reader;
    }

    static string[] Mods(ItemState state) => state.Mods.ToArray();

    [Fact]
    public void The_first_default_combination_supplies_the_includes_after_skipping_earlier_combinations()
    {
        var reader = Reader(["0", "1", "1"], ["1", "2", "1"], ["2819", "2817", "2818", "2819"]);
        Assert.Equal([ModA, ModB], Mods(new DefaultItemMods().For(reader, Rifle)));
    }

    [Fact]
    public void A_default_combination_at_index_zero_takes_its_own_includes()
    {
        var reader = Reader(["1", "0"], ["2", "1"], ["2817", "2818", "2819"]);
        Assert.Equal([ModA, ModB], Mods(new DefaultItemMods().For(reader, Rifle)));
    }

    [Fact]
    public void The_enum_text_true_counts_as_a_default_flag()
    {
        var reader = Reader(["False", "True"], ["1", "1"], ["2817", "2818"]);
        Assert.Equal([ModB], Mods(new DefaultItemMods().For(reader, Rifle)));
    }

    [Fact]
    public void An_include_that_is_not_an_omod_or_does_not_resolve_is_dropped_and_duplicates_collapse()
    {
        var reader = Reader(["1"], ["4"], ["2821", "2817", "2817", "99"]);
        Assert.Equal([ModA], Mods(new DefaultItemMods().For(reader, Rifle)));
    }

    [Fact]
    public void No_default_combination_gives_a_plain_item()
    {
        var reader = Reader(["0", "0"], ["1", "1"], ["2817", "2818"]);
        Assert.True(new DefaultItemMods().For(reader, Rifle).IsPlain);
    }

    [Fact]
    public void A_default_combination_with_no_includes_gives_a_plain_item()
    {
        var reader = Reader(["1", "0"], ["0", "1"], ["2817"]);
        Assert.True(new DefaultItemMods().For(reader, Rifle).IsPlain);
    }

    [Fact]
    public void Unreadable_or_inconsistent_rows_give_a_plain_item()
    {
        Assert.True(new DefaultItemMods().For(Reader(["1"], ["1", "1"], ["2817", "2818"]), Rifle).IsPlain);
        Assert.True(new DefaultItemMods().For(Reader(["1"], ["x"], ["2817"]), Rifle).IsPlain);
        Assert.True(new DefaultItemMods().For(Reader(["1"], ["3"], ["2817"]), Rifle).IsPlain);
        var missing = Reader(["1"], ["1"], ["2817"]);
        missing.Records[Rifle].Lists.Remove("WEAP\\ObjectTemplateIncludeMod");
        Assert.True(new DefaultItemMods().For(missing, Rifle).IsPlain);
        Assert.True(new DefaultItemMods().For(new FakeRecordReader(), Rifle).IsPlain);
    }

    [Fact]
    public void Armor_reads_the_armor_rows_and_other_records_stay_plain()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var vest = reader.Add(Vest, "ARMO");
        vest.Lists["ARMO\\ObjectTemplateDefault"] = ["1"];
        vest.Lists["ARMO\\ObjectTemplateIncludeCount"] = ["1"];
        vest.Lists["ARMO\\ObjectTemplateIncludeMod"] = ["2819"];
        vest.RawForms[0xB03] = ModC;
        Assert.Equal([ModC], Mods(new DefaultItemMods().For(reader, Vest)));
        Assert.True(new DefaultItemMods().For(reader, Steel).IsPlain);
        Assert.True(new DefaultItemMods().For(reader, ModA).IsPlain);
    }

    [Fact]
    public void Turning_the_rule_off_gives_a_plain_item()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        Assert.True(new DefaultItemMods(new ObjectModRules(ApplyDefaultMods: false)).For(reader, Rifle).IsPlain);
    }

    [Fact]
    public void A_result_is_cached_per_id()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var defaults = new DefaultItemMods();
        var first = defaults.For(reader, Rifle);
        reader.Records[Rifle].Lists["WEAP\\ObjectTemplateIncludeMod"] = ["2818"];
        Assert.Same(first, defaults.For(reader, Rifle));
    }

    [Fact]
    public void Add_without_a_state_takes_the_hook_and_every_explicit_path_does_not()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var defaults = new DefaultItemMods();
        var items = new PlayerItems { DefaultState = id => defaults.For(reader, id) };
        var modded = new ItemState([ModA]);
        items.Add(Rifle, 2);
        items.Add(Steel, 3);
        Assert.Equal(2, items.CountOf(Rifle, modded));
        Assert.Equal(3, items.CountOf(Steel, ItemState.Plain));
        items.Add(Rifle, ItemState.Plain, 1);
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.True(items.Transform(Rifle, modded, ItemState.Plain));
        Assert.Equal(2, items.CountOf(Rifle, ItemState.Plain));
        var target = new PlayerItems { DefaultState = _ => modded };
        Assert.True(items.MoveTo(target, Rifle, 3));
        Assert.Equal(1, target.CountOf(Rifle, modded));
        Assert.Equal(2, target.CountOf(Rifle, ItemState.Plain));
        items.Add(Rifle, 0);
        Assert.Equal(0, items.CountOf(Rifle));
    }

    [Fact]
    public void Only_base_stacks_frozen_into_the_ledger_take_the_hook()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var defaults = new DefaultItemMods();
        var ledger = new ContainerLedger { DefaultState = id => defaults.For(reader, id) };
        var chest = ledger.Freeze("000F10:Fallout4.esm", [new InventoryStack(Rifle, 2), new InventoryStack(Steel, 4)]);
        Assert.Equal(2, chest.CountOf(Rifle, new ItemState([ModA])));
        Assert.Equal(4, chest.CountOf(Steel, ItemState.Plain));
        var kept = ledger.Freeze("000F11:Fallout4.esm", [new ItemEntry(Rifle, ItemState.Plain, 1)]);
        Assert.Equal(1, kept.CountOf(Rifle, ItemState.Plain));
    }

    [Fact]
    public void A_stateless_add_to_a_held_container_takes_the_current_ledger_hook_and_explicit_states_do_not()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var defaults = new DefaultItemMods();
        var ledger = new ContainerLedger();
        var chest = ledger.Freeze("000F12:Fallout4.esm", [new InventoryStack(Steel, 1)]);
        chest.Add(Rifle, 1);
        Assert.Equal(1, chest.CountOf(Rifle, ItemState.Plain));
        ledger.DefaultState = id => defaults.For(reader, id);
        chest.Add(Rifle, 2);
        Assert.Equal(2, chest.CountOf(Rifle, new ItemState([ModA])));
        chest.Add(Rifle, ItemState.Plain, 1);
        Assert.Equal(2, chest.CountOf(Rifle, ItemState.Plain));
        ledger.Reload(new List<(string Ref, List<ItemEntry> Stacks)> { ("000F12:Fallout4.esm", new List<ItemEntry> { new(Steel, ItemState.Plain, 1) }) });
        Assert.Same(chest, ledger.Find("000F12:Fallout4.esm"));
        chest.Add(Rifle, 1);
        Assert.Equal(1, chest.CountOf(Rifle, new ItemState([ModA])));
    }

    [Fact]
    public void A_craft_on_the_attach_point_of_a_default_mod_replaces_it_and_returns_its_loose_item()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        reader.Records[Rifle].Lists["WEAP\\AttachParentSlots"] = [BarrelSlot];
        reader.Add(BarrelSlot, "KYWD", "ap_Gun_Barrel");
        reader.Add(Steel, "MISC", name: "Steel");
        reader.Add(DefaultLoose, "MISC", name: "Loose A");
        reader.Records[ModA].Scalars["OMOD\\FormType"] = Weapon;
        reader.Records[ModA].Scalars["OMOD\\AttachPoint"] = BarrelSlot;
        reader.Records[ModA].Scalars["OMOD\\LooseMod"] = DefaultLoose;
        reader.Records[ModB].Scalars["OMOD\\FormType"] = Weapon;
        reader.Records[ModB].Scalars["OMOD\\AttachPoint"] = BarrelSlot;
        var cobj = reader.AddRecipe(BarrelRecipe, ModB, "000F01:Fallout4.esm", [], (Steel, 1));
        cobj.Recipe = cobj.Recipe! with { Bench = null };
        var defaults = new DefaultItemMods();
        var items = new PlayerItems { DefaultState = id => defaults.For(reader, id) };
        items.Add(Steel, 2);
        items.Add(Rifle, 1);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        var target = bench.Targets.Single(t => t.Id.StartsWith("item:" + Rifle)).Id;
        Assert.Equal(["detach:" + ModA], bench.OptionsOf(target, RecordBenchRecipes.DetachSlot).Select(o => o.Id));
        Assert.True(bench.Craft(target, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState([ModB])));
        Assert.Equal(1, items.CountOf(DefaultLoose));
    }

    [Fact]
    public void A_world_pickup_takes_the_hook_and_a_dropped_ref_never_does()
    {
        var reader = Reader(["1"], ["1"], ["2817"]);
        var defaults = new DefaultItemMods();
        var items = new PlayerItems { DefaultState = id => defaults.For(reader, id) };
        var states = new DroppedItemStates();
        var pickup = new ItemPickup(items, _ => true, _ => { }, dropped: states);
        pickup.Activate(new ActivationTarget("000F20:Fallout4.esm", Rifle, "Weapon", new HashSet<string>(), 1));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState([ModA])));
        var withMod = states.Remember(new ItemState([ModB]));
        var plain = states.Remember(ItemState.Plain);
        pickup.Activate(new ActivationTarget(withMod, Rifle, "Weapon", new HashSet<string>(), 1));
        pickup.Activate(new ActivationTarget(plain, Rifle, "Weapon", new HashSet<string>(), 1));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState([ModB])));
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState([ModA])));
    }
}
