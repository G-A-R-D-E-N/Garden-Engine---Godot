using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordWorkshopCatalogTests
{
    const string Bench = "000010:a.esm";
    const string Other = "000011:a.esm";

    static void Recipe(FakeRecordReader reader, string id, string created, string bench, string? category, params (string Part, int Count)[] parts) =>
        reader.AddRecipe(id, created, bench, category is null ? [] : [category], parts.Select(part => (part.Part, (uint)part.Count)).ToArray());

    static void Edit(FakeRecordReader reader, string id, Func<RecipeRecord, RecipeRecord> change) =>
        reader.Records[id].Recipe = change(reader.Records[id].Recipe!);

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Other, "KYWD", "WorkbenchCooking");
        reader.Add("000020:a.esm", "KYWD", "WorkshopRecipeFilterBuilding02Barn03Doors");
        reader.Add("000021:a.esm", "KYWD", "WorkshopRecipeFilterPower");
        reader.Add("000030:a.esm", "CMPO", "c_Steel", "Steel");
        reader.Add("000031:a.esm", "CMPO", "c_Wood", "Wood");
        reader.Add("000040:a.esm", "STAT", "Door01", "Barn Door");
        reader.Add("000041:a.esm", "STAT", "Gen01", "Generator");
        reader.Add("000042:a.esm", "STAT", "Stove01", "Stove");
        Recipe(reader, "000050:a.esm", "000040:a.esm", Bench, "000020:a.esm", ("000030:a.esm", 3), ("000031:a.esm", 2));
        Recipe(reader, "000051:a.esm", "000041:a.esm", Bench, "000021:a.esm", ("000030:a.esm", 5));
        Recipe(reader, "000052:a.esm", "000042:a.esm", Other, "000021:a.esm");
        Edit(reader, "000050:a.esm", cobj => cobj with { Description = "A door." });
        return reader;
    }

    [Fact]
    public void BuildsTheCategoryTreeFromKeywordEditorIds()
    {
        var catalog = new RecordWorkshopCatalog(Data(), new FakeStock(), [Bench]);

        Assert.Equal(["Building", "Power"], catalog.Roots.Select(node => node.Name));
        var barn = Assert.Single(catalog.Roots[0].Children);
        Assert.Equal("Barn", barn.Name);
        var doors = Assert.Single(barn.Children);
        Assert.Equal("Doors", doors.Name);
        var leaf = Assert.Single(doors.Children);
        Assert.Equal("000050:a.esm", leaf.Id);
        Assert.Equal("Barn Door", leaf.Name);
        Assert.Equal("A door.", leaf.Item!.Description);
        Assert.Equal(["Steel", "Wood"], leaf.Item.Materials.Select(material => material.Name));
        Assert.Equal([3, 2], leaf.Item.Materials.Select(material => material.Needed));
    }

    [Fact]
    public void IgnoresRecipesForOtherWorkbenchesOrWithoutAnObject()
    {
        var reader = Data();
        Edit(reader, "000051:a.esm", cobj => cobj with { Created = string.Empty });

        var catalog = new RecordWorkshopCatalog(reader, new FakeStock(), [Bench]);

        Assert.Equal(["Building"], catalog.Roots.Select(node => node.Name));
    }

    [Fact]
    public void RecipeWithoutACategoryLandsInOther()
    {
        var reader = Data();
        Edit(reader, "000051:a.esm", cobj => cobj with { Categories = [] });

        var catalog = new RecordWorkshopCatalog(reader, new FakeStock(), [Bench]);

        var other = Assert.Single(catalog.Roots, node => node.Name == RecordWorkshopCatalog.OtherCategory);
        Assert.Equal("000051:a.esm", Assert.Single(other.Children).Id);
    }

    [Fact]
    public void DeepestCategoryWinsWhenARecipeListsSeveral()
    {
        var reader = Data();
        reader.Add("000022:a.esm", "KYWD", "WorkshopRecipeFilterBuilding");
        Edit(reader, "000050:a.esm", cobj => cobj with { Categories = ["000022:a.esm", "000020:a.esm"] });

        var catalog = new RecordWorkshopCatalog(reader, new FakeStock(), [Bench]);

        var building = Assert.Single(catalog.Roots, node => node.Name == "Building");
        Assert.DoesNotContain(building.Children, child => child.Item is not null);
    }

    [Fact]
    public void CountsAndStatusFollowTheStock()
    {
        var stock = new FakeStock();
        stock.Counts["000030:a.esm"] = 7;
        stock.Counts["000031:a.esm"] = 2;
        var catalog = new RecordWorkshopCatalog(Data(), stock, [Bench]);

        Assert.Equal(7, catalog.AvailableCount("000030:a.esm"));
        Assert.Equal(1, catalog.BuildableCount("000050:a.esm"));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf("000050:a.esm"));
        Assert.Equal(1, catalog.BuildableCount("000051:a.esm"));

        stock.Counts["000031:a.esm"] = 1;

        Assert.Equal(0, catalog.BuildableCount("000050:a.esm"));
        Assert.Equal(WorkshopItemStatus.LackingMaterials, catalog.StatusOf("000050:a.esm"));
        Assert.Equal(0, catalog.BuildableCount("nope"));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf("nope"));
    }

    [Fact]
    public void FreeBuildNeverLacksMaterials()
    {
        var catalog = new RecordWorkshopCatalog(Data(), new FakeStock(), [Bench], freeBuild: true);

        Assert.True(catalog.FreeBuild);
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf("000050:a.esm"));
    }

    [Theory]
    [InlineData("WorkshopRecipeFilterBuilding02Barn03Doors", "Building|Barn|Doors")]
    [InlineData("WorkshopRecipeFilterPower", "Power")]
    [InlineData("WorkshopRecipeFilterCreationClub02ccVRWorkshopsMisc", "Creation Club|cc VRWorkshops Misc")]
    [InlineData("SomethingElse", "Something Else")]
    [InlineData("", "Other")]
    [InlineData(null, "Other")]
    public void CategoryPathSplitsEditorIds(string? editorId, string expected) =>
        Assert.Equal(expected, string.Join("|", RecordWorkshopCatalog.CategoryPath(editorId)));

    [Fact]
    public void UiDataSourcesResolvesWorkbenchKeywordsByEditorId()
    {
        var catalog = new UiDataSources(Data()).Workshop(new FakeStock());

        Assert.Equal(["Building", "Power"], catalog.Roots.Select(node => node.Name));
    }

    [Fact]
    public void FreeOrMaterialLessRecipesAreCappedNotIntMax()
    {
        var reader = Data();
        Edit(reader, "000051:a.esm", cobj => cobj with { Components = [] });
        Edit(reader, "000050:a.esm", cobj => cobj with { Components = [new ComponentPart("000030:a.esm", 0)] });
        var catalog = new RecordWorkshopCatalog(reader, new FakeStock(), [Bench]);

        Assert.Equal(RecordWorkshopCatalog.MaxBuildable, catalog.BuildableCount("000051:a.esm"));
        Assert.Equal(RecordWorkshopCatalog.MaxBuildable, catalog.BuildableCount("000050:a.esm"));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf("000051:a.esm"));
    }

    [Fact]
    public void HugeStockIsCapped()
    {
        var stock = new FakeStock();
        stock.Counts["000030:a.esm"] = int.MaxValue;
        stock.Counts["000031:a.esm"] = int.MaxValue;

        Assert.Equal(RecordWorkshopCatalog.MaxBuildable, new RecordWorkshopCatalog(Data(), stock, [Bench]).BuildableCount("000050:a.esm"));
    }
}
