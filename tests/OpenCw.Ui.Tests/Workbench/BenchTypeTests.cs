using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BenchTypeTests
{
    const string Rifle = "000A01:Fallout4.esm";
    const string Vest = "000A02:Fallout4.esm";
    const string Pistol = "000A03:Fallout4.esm";
    const string Barrel = "000B01:Fallout4.esm";
    const string Sight = "000B02:Fallout4.esm";
    const string Plate = "000B03:Fallout4.esm";
    const string Grip = "000B04:Fallout4.esm";
    const string BarrelSlot = "000C01:Fallout4.esm";
    const string SightSlot = "000C02:Fallout4.esm";
    const string Steel = "000D01:Fallout4.esm";
    const string BarrelRecipe = "000E01:Fallout4.esm";
    const string SightRecipe = "000E02:Fallout4.esm";
    const string PlateRecipe = "000E03:Fallout4.esm";
    const string GripRecipe = "000E04:Fallout4.esm";
    const string BarrelLoose = "000B11:Fallout4.esm";
    const string GripLoose = "000B12:Fallout4.esm";
    const string ReceiverSlot = "000C03:Fallout4.esm";
    const string Receiver = "000B05:Fallout4.esm";
    const string ReceiverRecipe = "000E05:Fallout4.esm";
    const string SlotsPath = "OMOD\\AttachParentSlot";
    const uint BarrelSlotWord = 0xC01;
    const string Weapon = "1346454871";
    const string Armor = "1330467393";

    static void Mod(FakeRecordReader reader, string mod, string recipe, string formType, string? slot, string? bench = null)
    {
        var record = reader.Add(mod, "OMOD", name: "Mod " + mod);
        record.Scalars["OMOD\\FormType"] = formType;
        if (slot is not null) record.Scalars["OMOD\\AttachPoint"] = slot;
        var cobj = reader.AddRecipe(recipe, mod, bench ?? "000F01:Fallout4.esm", [], (Steel, 1));
        if (bench is null) cobj.Recipe = cobj.Recipe! with { Bench = null };
    }

    static void Loose(FakeRecordReader reader, string mod, string loose)
    {
        reader.Records[mod].Scalars["OMOD\\LooseMod"] = loose;
        if (!loose.StartsWith("000000")) reader.Add(loose, "MISC", name: "Loose " + mod);
    }

    static (FakeRecordReader Reader, PlayerItems Items) World()
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Rifle").Lists["WEAP\\AttachParentSlots"] = [BarrelSlot, SightSlot];
        reader.Add(Pistol, "WEAP", name: "Pistol").Lists["WEAP\\AttachParentSlots"] = [SightSlot];
        reader.Add(Vest, "ARMO", name: "Vest").Lists["ARMO\\AttachParentSlots"] = [BarrelSlot];
        reader.Add(Steel, "MISC", name: "Steel");
        reader.Add(BarrelSlot, "KYWD", "ap_Gun_Barrel");
        reader.Add(SightSlot, "KYWD", "ap_Gun_Sights");
        Mod(reader, Barrel, BarrelRecipe, Weapon, BarrelSlot);
        Mod(reader, Sight, SightRecipe, Weapon, SightSlot);
        Mod(reader, Plate, PlateRecipe, Armor, BarrelSlot);
        var items = new PlayerItems();
        items.Add(Steel, 5);
        items.Add(Rifle, 1);
        items.Add(Pistol, 1);
        items.Add(Vest, 1);
        return (reader, items);
    }

    static string Target(RecordBenchRecipes bench, string id) => bench.Targets.Single(t => t.Id.StartsWith("item:" + id)).Id;

    [Fact]
    public void Only_weapons_armor_and_power_armor_benches_name_an_item_signature()
    {
        Assert.Equal("WEAP", BenchKinds.ItemSignature(2));
        Assert.Equal("ARMO", BenchKinds.ItemSignature(7));
        Assert.Equal("ARMO", BenchKinds.ItemSignature(8));
        foreach (var other in new int?[] { null, 0, 1, 3, 4, 5, 6, 9 }) Assert.Null(BenchKinds.ItemSignature(other));
    }

    [Fact]
    public void Mod_recipes_without_a_bench_keyword_are_listed_only_at_a_weapons_or_armor_bench()
    {
        var (reader, items) = World();
        Assert.Equal(0, new RecordBenchRecipes(reader, items, ["000F01:Fallout4.esm"], "Bench").RecipeCount);
        Assert.Equal(0, new RecordBenchRecipes(reader, items, [], "Bench", null, BenchKinds.RobotMod).RecipeCount);
        Assert.Equal(2, new RecordBenchRecipes(reader, items, [], "Bench", null, BenchKinds.Weapons).RecipeCount);
        Assert.Equal(1, new RecordBenchRecipes(reader, items, [], "Bench", null, BenchKinds.Armor).RecipeCount);
    }

    [Fact]
    public void A_weapons_bench_targets_only_weapons_and_omits_the_empty_bench_target()
    {
        var (reader, items) = World();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.Equal(["Rifle", "Pistol"], bench.Targets.Select(t => t.Name));
        Assert.DoesNotContain(bench.Targets, t => t.Id == RecordBenchRecipes.TargetId);
        var armor = new RecordBenchRecipes(reader, items, [], "Armor Workbench", null, BenchKinds.Armor);
        Assert.Equal(["Vest"], armor.Targets.Select(t => t.Name));
    }

    [Fact]
    public void A_mod_is_offered_only_for_items_whose_attach_slots_include_its_attach_point()
    {
        var (reader, items) = World();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        var rifle = Target(bench, Rifle);
        var pistol = Target(bench, Pistol);
        Assert.Equal(["ap_Gun_Barrel", "ap_Gun_Sights"], bench.SlotsOf(rifle).Select(s => s.Name));
        Assert.Equal(["ap_Gun_Sights"], bench.SlotsOf(pistol).Select(s => s.Name));
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(pistol, "ap_Gun_Barrel", BarrelRecipe));
        Assert.False(bench.Craft(pistol, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(5, items.CountOf(Steel));
    }

    [Fact]
    public void Crafting_a_mod_at_a_weapons_bench_attaches_it_to_one_copy_and_spends_the_components()
    {
        var (reader, items) = World();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        var rifle = Target(bench, Rifle);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(rifle, "ap_Gun_Barrel", BarrelRecipe));
        Assert.True(bench.Craft(rifle, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(4, items.CountOf(Steel));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel })));
    }

    [Fact]
    public void An_item_whose_attach_slots_cannot_be_read_gets_no_attach_point_mods()
    {
        var (reader, items) = World();
        reader.Records[Rifle].Lists.Clear();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.Empty(bench.SlotsOf(Target(bench, Rifle)));
    }

    [Fact]
    public void A_mod_with_no_attach_point_keeps_the_type_only_check()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, null);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        var pistol = Target(bench, Pistol);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(pistol, "Other", GripRecipe));
    }

    [Fact]
    public void A_keyword_bench_keeps_its_category_slots_and_the_type_only_mod_check()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot, bench: "000F01:Fallout4.esm");
        var bench = new RecordBenchRecipes(reader, items, ["000F01:Fallout4.esm"], "Keyword Bench");
        var pistol = Target(bench, Pistol);
        Assert.Equal(1, bench.RecipeCount);
        Assert.Equal(["Other"], bench.SlotsOf(pistol).Select(s => s.Name));
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(pistol, "Other", GripRecipe));
        Assert.Equal(["Rifle", "Pistol", "Vest"], bench.Targets.Skip(1).Select(t => t.Name));
    }

    [Fact]
    public void A_second_mod_for_the_same_attach_point_replaces_the_first()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        var modded = Target(bench, Rifle);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(modded, "ap_Gun_Barrel", GripRecipe));
        Assert.True(bench.Craft(modded, "ap_Gun_Barrel", GripRecipe).Ok);
        Assert.Equal(3, items.CountOf(Steel));
        Assert.Equal(1, items.CountOf(Rifle));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Grip })));
    }

    [Fact]
    public void A_mod_for_another_attach_point_keeps_the_first()
    {
        var (reader, items) = World();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Sights", SightRecipe).Ok);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel, Sight })));
    }

    [Fact]
    public void A_mod_with_no_attach_point_replaces_nothing()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, null);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), "Other", GripRecipe).Ok);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel, Grip })));
    }

    [Fact]
    public void Removing_a_mod_returns_its_loose_item_and_a_replaced_mod_returns_its_own()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Loose(reader, Barrel, BarrelLoose);
        Loose(reader, Grip, GripLoose);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(0, items.CountOf(BarrelLoose));
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe).Ok);
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(0, items.CountOf(GripLoose));
        Assert.True(bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Grip).Ok);
        Assert.Equal(1, items.CountOf(GripLoose));
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
    }

    [Fact]
    public void A_mod_with_no_loose_item_or_a_loose_item_that_does_not_exist_returns_nothing()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Loose(reader, Barrel, "000000:Fallout4.esm");
        Loose(reader, Grip, GripLoose);
        reader.Records.Remove(GripLoose);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Barrel).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Grip).Ok);
        Assert.Equal(3, items.CountOf(Steel));
        Assert.Equal(4, items.Entries.Count);
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
    }

    [Fact]
    public void A_recipe_with_another_bench_keyword_is_not_pulled_in_by_the_bench_type()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, SightSlot, bench: "000F02:Fallout4.esm");
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(Target(bench, Pistol), "ap_Gun_Sights", GripRecipe));
        Assert.Equal(2, bench.RecipeCount);
    }

    [Fact]
    public void A_failed_consume_reverses_the_transform_and_returns_no_loose_item()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Loose(reader, Barrel, BarrelLoose);
        Loose(reader, Grip, GripLoose);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        items.Remove(Steel, 4);
        var result = bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe);
        Assert.False(result.Ok);
        Assert.Equal(RecordBenchRecipes.LackingText, result.Reason);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel })));
        Assert.Equal(0, items.CountOf(Rifle, new ItemState(new[] { Grip })));
        Assert.Equal(0, items.CountOf(BarrelLoose));
        Assert.Equal(0, items.CountOf(GripLoose));
    }

    [Fact]
    public void A_replacing_craft_returns_the_replaced_loose_mod_once()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Loose(reader, Barrel, BarrelLoose);
        Loose(reader, Grip, GripLoose);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(0, items.CountOf(BarrelLoose));
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe).Ok);
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(0, items.CountOf(GripLoose));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Grip })));
        Assert.Equal(3, items.CountOf(Steel));
    }

    [Fact]
    public void A_replacing_craft_returns_the_previous_mod_loose_item_not_the_new_one()
    {
        var (reader, items) = World();
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Loose(reader, Barrel, BarrelLoose);
        Loose(reader, Grip, GripLoose);
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe).Ok);
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(0, items.CountOf(GripLoose));
        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(1, items.CountOf(GripLoose));
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel })));
    }

    [Fact]
    public void A_failed_craft_leaves_every_item_count_unchanged()
    {
        var (reader, items) = World();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        items.Remove(Steel, 5);
        var before = (items.CountOf(Steel), items.CountOf(Rifle), items.CountOf(Pistol), items.CountOf(Vest), items.Entries.Count);
        Assert.False(bench.Craft(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(before, (items.CountOf(Steel), items.CountOf(Rifle), items.CountOf(Pistol), items.CountOf(Vest), items.Entries.Count));
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(0, items.CountOf(Rifle, new ItemState(new[] { Barrel })));
    }

    static void Offers(FakeRecordReader reader, string mod, params string[] words)
    {
        var record = reader.Records[mod];
        record.Lists[SlotsPath] = words;
        record.RawForms[BarrelSlotWord] = BarrelSlot;
    }

    static (FakeRecordReader Reader, PlayerItems Items) ReceiverWorld()
    {
        var (reader, items) = World();
        reader.Records[Rifle].Lists["WEAP\\AttachParentSlots"] = [ReceiverSlot];
        reader.Add(ReceiverSlot, "KYWD", "ap_Gun_Receiver");
        Mod(reader, Receiver, ReceiverRecipe, Weapon, ReceiverSlot);
        Offers(reader, Receiver, BarrelSlotWord.ToString());
        return (reader, items);
    }

    [Fact]
    public void A_mod_on_a_slot_only_another_mod_offers_needs_that_mod_attached()
    {
        var (reader, items) = ReceiverWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        var rifle = Target(bench, Rifle);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(rifle, "ap_Gun_Barrel", BarrelRecipe));
        Assert.False(bench.Craft(rifle, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(5, items.CountOf(Steel));
        Assert.Equal(["ap_Gun_Receiver"], bench.SlotsOf(rifle).Select(s => s.Name));
        Assert.True(bench.Craft(rifle, "ap_Gun_Receiver", ReceiverRecipe).Ok);
        var modded = Target(bench, Rifle);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf(modded, "ap_Gun_Barrel", BarrelRecipe));
        Assert.Contains("ap_Gun_Barrel", bench.SlotsOf(modded).Select(s => s.Name));
        Assert.True(bench.Craft(modded, "ap_Gun_Barrel", BarrelRecipe).Ok);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Barrel, Receiver })));
    }

    [Fact]
    public void A_mod_that_offers_its_own_attach_point_does_not_make_it_reachable_for_a_replacement()
    {
        var (reader, items) = World();
        reader.Records[Rifle].Lists["WEAP\\AttachParentSlots"] = [SightSlot];
        Mod(reader, Grip, GripRecipe, Weapon, BarrelSlot);
        Offers(reader, Barrel, BarrelSlotWord.ToString());
        Assert.True(items.Transform(Rifle, ItemState.Plain, new ItemState(new[] { Barrel })));
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", GripRecipe));
    }

    [Fact]
    public void An_unreadable_slot_row_on_an_attached_mod_adds_no_slot()
    {
        var (reader, items) = ReceiverWorld();
        Assert.True(items.Transform(Rifle, ItemState.Plain, new ItemState(new[] { Receiver })));
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        reader.Records[Receiver].Lists.Remove(SlotsPath);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf(Target(bench, Rifle), "ap_Gun_Barrel", BarrelRecipe));
        var unresolved = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        Offers(reader, Receiver, "0", "x", "2818");
        Assert.Equal(WorkbenchOptionState.Unavailable, unresolved.StateOf(Target(unresolved, Rifle), "ap_Gun_Barrel", BarrelRecipe));
    }

    const string GripLooseId = "000B13:Fallout4.esm";
    const string ReceiverLoose = "000B14:Fallout4.esm";

    static (FakeRecordReader Reader, PlayerItems Items) AttachedWorld()
    {
        var (reader, items) = ReceiverWorld();
        Loose(reader, Barrel, BarrelLoose);
        Loose(reader, Receiver, ReceiverLoose);
        Mod(reader, Grip, GripRecipe, Weapon, ReceiverSlot);
        Loose(reader, Grip, GripLooseId);
        Assert.True(items.Transform(Rifle, ItemState.Plain, new ItemState(new[] { Receiver, Barrel })));
        return (reader, items);
    }

    [Fact]
    public void Detaching_the_mod_that_offered_a_slot_drops_the_mods_on_it_and_returns_both()
    {
        var (reader, items) = AttachedWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);

        Assert.True(bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Receiver).Ok);

        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(ReceiverLoose));
        Assert.Equal(1, items.CountOf(BarrelLoose));
    }

    [Fact]
    public void Detaching_a_mod_nothing_depends_on_keeps_the_others()
    {
        var (reader, items) = AttachedWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);

        Assert.True(bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Barrel).Ok);

        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Receiver })));
        Assert.Equal(1, items.CountOf(BarrelLoose));
        Assert.Equal(0, items.CountOf(ReceiverLoose));
    }

    [Fact]
    public void Replacing_the_offering_mod_with_one_that_offers_nothing_drops_the_dependent_mod()
    {
        var (reader, items) = AttachedWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);

        Assert.True(bench.Craft(Target(bench, Rifle), "ap_Gun_Receiver", GripRecipe).Ok);

        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Grip })));
        Assert.Equal(1, items.CountOf(ReceiverLoose));
        Assert.Equal(1, items.CountOf(BarrelLoose));
    }

    [Fact]
    public void An_unreadable_slot_row_refuses_the_detach_and_changes_nothing()
    {
        var (reader, items) = AttachedWorld();
        var bench = new RecordBenchRecipes(reader, items, [], "Weapons Workbench", null, BenchKinds.Weapons);
        reader.Failing.Add(Barrel + "|OMOD\\AttachPoint");

        var result = bench.Craft(Target(bench, Rifle), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Receiver);

        Assert.False(result.Ok);
        Assert.Equal(1, items.CountOf(Rifle, new ItemState(new[] { Receiver, Barrel })));
    }

    [Fact]
    public void A_bench_change_to_the_equipped_item_moves_the_equipment_to_the_new_state_when_no_old_copy_remains()
    {
        var items = new PlayerItems();
        items.Add(Vest, 1);
        var equipment = new OpenCommonwealth.Services.Ui.PipBoy.PlayerEquipment();
        equipment.Wear(Vest, ItemState.Plain, 4);
        equipment.SetWeapon(Rifle, ItemState.Plain);
        items.Add(Rifle, 1);
        items.Transformed += (id, from, to) => { if (items.CountOf(id, from) == 0) equipment.Retarget(id, from, to); };

        items.Transform(Vest, ItemState.Plain, new ItemState(new[] { Plate }));
        items.Transform(Rifle, ItemState.Plain, new ItemState(new[] { Barrel }));
        equipment.Reconcile(items);

        Assert.Equal(new ItemState(new[] { Plate }), equipment.Armor.Single().State);
        Assert.Equal(4u, equipment.Armor.Single().Mask);
        Assert.Equal(new ItemState(new[] { Barrel }), equipment.Weapon!.State);
    }
}
