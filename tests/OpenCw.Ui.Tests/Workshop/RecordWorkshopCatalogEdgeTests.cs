using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordWorkshopCatalogEdgeTests
{
    const string Bench = "000010:a.esm";
    const string Building = "000020:a.esm";
    const string Barn = "000021:a.esm";
    const string Zeta = "000022:a.esm";
    const string Armor = "000023:a.esm";
    const string Steel = "000030:a.esm";
    const string Wood = "000031:a.esm";

    static FakeRecordReader Reader()
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Building, "KYWD", "WorkshopRecipeFilterBuilding");
        reader.Add(Barn, "KYWD", "WorkshopRecipeFilterBuilding02Barn");
        reader.Add(Zeta, "KYWD", "WorkshopRecipeFilterZeta");
        reader.Add(Armor, "KYWD", "WorkshopRecipeFilterArmor");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel");
        reader.Add(Wood, "CMPO", "c_Wood", "Wood");
        return reader;
    }

    static void Add(FakeRecordReader reader, string recipe, string created, string? createdName, string category, params (string Part, uint Count)[] parts)
    {
        reader.Add(created, "STAT", null, createdName);
        reader.AddRecipe(recipe, created, Bench, [category], parts);
    }

    static RecordWorkshopCatalog Catalog(FakeRecordReader reader, FakeStock? stock = null) => new(reader, stock ?? new FakeStock(), [Bench]);

    [Fact]
    public void A_leaf_is_named_by_the_created_name_then_its_editor_id_then_the_recipe_editor_id_then_the_recipe_id()
    {
        var reader = Reader();
        Add(reader, "000050:a.esm", "000040:a.esm", "Named", Zeta);
        Add(reader, "000051:a.esm", "000041:a.esm", null, Zeta);
        reader.Records["000041:a.esm"] = new FakeRecord { Signature = "STAT", EditorId = "Edited" };
        Add(reader, "000052:a.esm", "000042:a.esm", null, Zeta);
        reader.Records.Remove("000042:a.esm");
        Add(reader, "000053:a.esm", "000043:a.esm", null, Zeta);
        reader.Records.Remove("000043:a.esm");
        reader.Records["000053:a.esm"] = new FakeRecord { Signature = "COBJ", Recipe = reader.Records["000053:a.esm"].Recipe };

        var leaves = Catalog(reader).Roots.Single().Children;

        Assert.Equal(new[] { "000053:a.esm", "Edited", "Named", "co_000052:a.esm" }, leaves.Select(leaf => leaf.Name));
    }

    [Fact]
    public void A_material_is_named_by_its_name_then_its_editor_id_then_its_id_and_its_count_is_capped_at_int_max()
    {
        var reader = Reader();
        reader.Add("000032:a.esm", "CMPO", "c_Glue");
        Add(reader, "000050:a.esm", "000040:a.esm", "Thing", Zeta, (Steel, 2), ("000032:a.esm", uint.MaxValue), ("000033:a.esm", 1));

        var materials = Catalog(reader).Roots.Single().Children.Single().Item!.Materials;

        Assert.Equal(new[] { "Steel", "c_Glue", "000033:a.esm" }, materials.Select(material => material.Name));
        Assert.Equal(new[] { 2, int.MaxValue, 1 }, materials.Select(material => material.Needed));
    }

    [Fact]
    public void Categories_come_before_leaves_names_sort_ordinally_and_category_nodes_are_identified_by_their_path()
    {
        var reader = Reader();
        Add(reader, "000050:a.esm", "000040:a.esm", "Zed", Building);
        Add(reader, "000051:a.esm", "000041:a.esm", "Ant", Building);
        Add(reader, "000052:a.esm", "000042:a.esm", "Hay", Barn);
        Add(reader, "000053:a.esm", "000043:a.esm", "Plate", Zeta);
        Add(reader, "000054:a.esm", "000044:a.esm", "Vest", Armor);

        var roots = Catalog(reader).Roots;
        var building = roots.Single(node => node.Name == "Building");

        Assert.Equal(new[] { "Armor", "Building", "Zeta" }, roots.Select(node => node.Name));
        Assert.Equal("/Building", building.Id);
        Assert.Equal(new[] { "Barn", "Ant", "Zed" }, building.Children.Select(child => child.Name));
        Assert.Equal(new[] { true, false, false }, building.Children.Select(child => child.IsCategory));
        Assert.Equal("/Building/Barn", building.Children[0].Id);
        Assert.Equal("000052:a.esm", building.Children[0].Children.Single().Id);
        Assert.Equal("000051:a.esm", building.Children[1].Id);
    }

    [Fact]
    public void Buildable_count_is_the_smallest_whole_multiple_over_the_materials_that_are_needed()
    {
        var reader = Reader();
        Add(reader, "000050:a.esm", "000040:a.esm", "Thing", Zeta, (Steel, 3), (Wood, 2), ("000032:a.esm", 0));
        var stock = new FakeStock();
        stock.Counts[Steel] = 10;
        stock.Counts[Wood] = 5;

        var catalog = Catalog(reader, stock);

        Assert.Equal(2, catalog.BuildableCount("000050:a.esm"));
        stock.Counts[Wood] = 6;
        Assert.Equal(3, catalog.BuildableCount("000050:a.esm"));
        stock.Counts[Steel] = 2;
        Assert.Equal(0, catalog.BuildableCount("000050:a.esm"));
    }

    [Fact]
    public void A_material_exactly_at_the_needed_count_is_not_lacking_and_one_less_is()
    {
        var reader = Reader();
        Add(reader, "000050:a.esm", "000040:a.esm", "Thing", Zeta, (Steel, 3));
        var stock = new FakeStock();
        stock.Counts[Steel] = 3;
        var catalog = Catalog(reader, stock);
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf("000050:a.esm"));

        stock.Counts[Steel] = 2;

        Assert.Equal(WorkshopItemStatus.LackingMaterials, catalog.StatusOf("000050:a.esm"));
    }

    [Fact]
    public void The_workbench_keyword_is_matched_without_regard_to_case_and_an_empty_list_matches_nothing()
    {
        var reader = Reader();
        Add(reader, "000050:a.esm", "000040:a.esm", "Thing", Zeta);

        Assert.Single(new RecordWorkshopCatalog(reader, new FakeStock(), [Bench.ToUpperInvariant()]).Roots);
        Assert.Empty(new RecordWorkshopCatalog(reader, new FakeStock(), []).Roots);
    }

    [Fact]
    public void The_catalog_needs_its_reader_stock_and_keywords()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordWorkshopCatalog(null!, new FakeStock(), [Bench]));
        Assert.Throws<ArgumentNullException>(() => new RecordWorkshopCatalog(Reader(), null!, [Bench]));
        Assert.Throws<ArgumentNullException>(() => new RecordWorkshopCatalog(Reader(), new FakeStock(), null!));
    }

    [Theory]
    [InlineData("   ", "Other")]
    [InlineData("WorkshopRecipeFilter", "Other")]
    [InlineData("WorkshopRecipeFilter0102", "Other")]
    [InlineData("Power02", "Power")]
    [InlineData("WorkshopRecipeFilterHTMLParser", "HTMLParser")]
    public void The_category_path_falls_back_to_other_when_no_name_is_left(string editorId, string expected)
    {
        Assert.Equal(expected, string.Join("|", RecordWorkshopCatalog.CategoryPath(editorId)));
    }
}
