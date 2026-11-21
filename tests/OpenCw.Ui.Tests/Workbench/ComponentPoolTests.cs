using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ComponentPoolTests
{
    const string Steel = "000002:Fallout4.esm";
    const string Screws = "000003:Fallout4.esm";
    const string ScrapSteel = "000009:Fallout4.esm";
    const string ScrapScrews = "00000A:Fallout4.esm";
    const string Tin = "000011:Fallout4.esm";
    const string Gear = "000012:Fallout4.esm";
    const string Chem = "000A01:Fallout4.esm";
    const string Stim = "000B01:Fallout4.esm";
    const string Cat = "000D01:Fallout4.esm";

    static (string Id, uint Count) Part(string id, uint count) => (id, count);

    static void Junk(FakeRecordReader reader, string id, string name, params (string Id, uint Count)[] parts) =>
        reader.Add(id, "MISC", name, name).Parts(parts);

    static void Component(FakeRecordReader reader, string id, string name, string scrap)
    {
        reader.Add(id, "CMPO", name, name).Scalars[RecordRows.ComponentScrapItemPath] = scrap;
        reader.Add(scrap, "MISC", "Scrap" + name, "Scrap " + name);
    }

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        Component(reader, Steel, "Steel", ScrapSteel);
        Component(reader, Screws, "Screws", ScrapScrews);
        Junk(reader, Tin, "Tin can", Part(Steel, 2));
        Junk(reader, Gear, "Gear", Part(Steel, 5), Part(Screws, 1));
        return reader;
    }

    static WorkshopMaterial Need(string id, int count) => new(id, id, count);

    [Fact]
    public void CountAddsDirectUnitsAndTheComponentsOfHeldJunk()
    {
        var items = new PlayerItems();
        items.Add(Tin, 3);
        items.Add(Gear, 1);
        items.Add(Steel, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.Equal(12, pool.CountOf(Steel));
        Assert.Equal(1, pool.CountOf(Screws));
        Assert.Equal(0, pool.CountOf("0000FF:Fallout4.esm"));
    }

    [Fact]
    public void ConsumePrefersJunkWithTheFewestComponentsAndRefundsNothingWhenItFitsExactly()
    {
        var items = new PlayerItems();
        items.Add(Tin, 2);
        items.Add(Gear, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, 4)]));

        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(Gear));
        Assert.Equal(0, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void ConsumedJunkSurplusComesBackAsTheComponentsScrapItem()
    {
        var items = new PlayerItems();
        items.Add(Tin, 2);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, 3)]));

        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void SurplusOfOneComponentPaysALaterMaterialOfTheSameTransaction()
    {
        var items = new PlayerItems();
        items.Add(Gear, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, 1), Need(Screws, 1)]));

        Assert.Equal(0, items.CountOf(Gear));
        Assert.Equal(4, items.CountOf(ScrapSteel));
        Assert.Equal(0, items.CountOf(ScrapScrews));
    }

    [Fact]
    public void DirectUnitsAreSpentBeforeJunk()
    {
        var items = new PlayerItems();
        items.Add(Steel, 2);
        items.Add(Tin, 5);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, 3)]));

        Assert.Equal(0, items.CountOf(Steel));
        Assert.Equal(4, items.CountOf(Tin));
    }

    [Fact]
    public void ConsumeIsAllOrNothingAcrossMaterials()
    {
        var items = new PlayerItems();
        items.Add(Tin, 2);
        items.Add(Gear, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.False(pool.Consume([Need(Steel, 4), Need(Screws, 2)]));

        Assert.Equal(2, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(Gear));
        Assert.Equal(0, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void ABenchCraftsFromJunkAndTheRecipeShowsAsAvailable()
    {
        var reader = Data();
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Chem, "KYWD", "WorkbenchChemlab");
        reader.Add(Cat, "KYWD", "WorkshopRecipeFilterChemsHealing");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, Chem, [Cat], (Steel, 3));
        var items = new PlayerItems();
        items.Add(Tin, 2);
        var bench = new RecordBenchRecipes(reader, items, [Chem], "Chem Station");

        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf("bench", "", "000E01:Fallout4.esm"));
        Assert.Equal(4, bench.AvailableCount(Steel));
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);

        Assert.Equal(1, items.CountOf(Stim));
        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }
}
