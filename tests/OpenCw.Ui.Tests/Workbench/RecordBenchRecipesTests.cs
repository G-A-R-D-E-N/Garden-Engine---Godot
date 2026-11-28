using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordBenchRecipesTests
{
    const string Chem = "000A01:Fallout4.esm";
    const string Weapon = "000A02:Fallout4.esm";
    const string Stim = "000B01:Fallout4.esm";
    const string Mod = "000B02:Fallout4.esm";
    const string Other = "000B03:Fallout4.esm";
    const string Leather = "000C01:Fallout4.esm";
    const string Cat = "000D01:Fallout4.esm";

    static (RecordBenchRecipes Bench, PlayerItems Items) Make(uint count = 2)
    {
        var reader = new FakeRecordReader();
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Mod, "OMOD", name: "Long Barrel");
        reader.Add(Other, "ALCH", name: "Grenade");
        reader.Add(Leather, "MISC", name: "Leather");
        reader.Add(Cat, "KYWD", "WorkshopRecipeFilterChemsHealing");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, Chem, [Cat], (Leather, count));
        reader.AddRecipe("000E02:Fallout4.esm", Mod, Chem, [], (Leather, 1));
        reader.AddRecipe("000E03:Fallout4.esm", Other, Weapon, [], (Leather, 1));
        var items = new PlayerItems();
        return (new RecordBenchRecipes(reader, items, [Chem], "Chem Station"), items);
    }

    [Fact]
    public void OnlyRecipesOfTheBenchKeywordsAreListed()
    {
        var (bench, _) = Make();
        Assert.Equal(2, bench.RecipeCount);
        Assert.Equal(["Chems Healing"], bench.SlotsOf(RecordBenchRecipes.TargetId).Select(s => s.Id));
        Assert.Equal(["Stimpak"], bench.OptionsOf(RecordBenchRecipes.TargetId, "Chems Healing").Select(o => o.Name));
    }

    [Fact]
    public void CraftConsumesComponentsAndAddsTheCreatedItem()
    {
        var (bench, items) = Make();
        items.Add(Leather, 5);
        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf("bench", "", "000E01:Fallout4.esm"));
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(3, items.CountOf(Leather));
        Assert.Equal(1, items.CountOf(Stim));
    }

    [Fact]
    public void ShortStackRefusesAndRemovesNothing()
    {
        var (bench, items) = Make();
        items.Add(Leather, 1);
        Assert.Equal(WorkbenchOptionState.LackingMaterials, bench.StateOf("bench", "", "000E01:Fallout4.esm"));
        var result = bench.Craft("bench", "", "000E01:Fallout4.esm");
        Assert.False(result.Ok);
        Assert.Equal(RecordBenchRecipes.LackingText, result.Reason);
        Assert.Equal(1, items.CountOf(Leather));
        Assert.Equal(0, items.CountOf(Stim));
    }

    [Fact]
    public void ModRecipesAreNotCraftableFromTheBenchTargetItself()
    {
        var (bench, items) = Make();
        items.Add(Leather, 5);
        Assert.Equal(WorkbenchOptionState.Unavailable, bench.StateOf("bench", "", "000E02:Fallout4.esm"));
        Assert.Equal(RecordBenchRecipes.ModText, bench.Craft("bench", "", "000E02:Fallout4.esm").Reason);
        Assert.Equal(5, items.CountOf(Leather));
    }

    [Fact]
    public void MenuFlowCraftsThroughTheController()
    {
        var (bench, items) = Make();
        items.Add(Leather, 2);
        var controller = new WorkbenchController(bench);
        foreach (var _ in new[] { 1, 2, 3, 4 }) controller.Handle(MenuInputAction.Accept);
        Assert.Equal(1, items.CountOf(Stim));
        Assert.Equal(0, items.CountOf(Leather));
    }

    [Fact]
    public void DuplicateComponentRowsAreSummedAndCreatedCountApplies()
    {
        var reader = new FakeRecordReader();
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Leather, "MISC", name: "Leather");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, Chem, [], (Leather, 2), (Leather, 2));
        reader.Records["000E01:Fallout4.esm"].Recipe = reader.Records["000E01:Fallout4.esm"].Recipe! with { CreatedCount = 3 };
        var items = new PlayerItems();
        var bench = new RecordBenchRecipes(reader, items, [Chem.ToLowerInvariant()], "Chem Station");
        items.Add(Leather, 3);
        Assert.Equal(WorkbenchOptionState.LackingMaterials, bench.StateOf("bench", "", "000E01:Fallout4.esm"));
        Assert.False(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(3, items.CountOf(Leather));
        items.Add(Leather, 1);
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(0, items.CountOf(Leather));
        Assert.Equal(3, items.CountOf(Stim));
    }

    [Fact]
    public void UnknownOptionIsRefusedAndAnEmptyBenchHasNoRecipes()
    {
        var (bench, _) = Make();
        Assert.False(bench.Craft("bench", "", "nope").Ok);
        Assert.Equal(0, new RecordBenchRecipes(new FakeRecordReader(), new PlayerItems(), [Chem], "x").RecipeCount);
    }

    [Fact]
    public void ItemsPickedUpThroughThePlayerStoreAreCraftable()
    {
        var (bench, items) = Make();
        items.Add(Leather, 2);
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(1, items.CountOf(Stim));
    }
}
