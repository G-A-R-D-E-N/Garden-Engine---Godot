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
    const string Wrench = "000013:Fallout4.esm";
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
    public void AvailableComponentsSaturateAcrossLargeJunkStacks()
    {
        var reader = Data();
        var items = new PlayerItems();
        foreach (var id in new[] { Tin, Gear, "000013:Fallout4.esm" })
        {
            Junk(reader, id, "Bulk steel", Part(Steel, 2_000_000_000));
            items.Add(id, int.MaxValue);
        }

        Assert.Equal(int.MaxValue, new ComponentPool(reader, items).CountOf(Steel));
    }

    [Fact]
    public void RepeatedRequirementsChargeTheFullTotalAcrossDirectItemsAndJunk()
    {
        var items = new PlayerItems();
        items.Add(Steel, int.MaxValue);
        var pool = new ComponentPool(Data(), items);
        WorkshopMaterial[] needs = [Need(Steel, int.MaxValue), Need(Steel, 1)];

        Assert.False(pool.Consume(needs));
        Assert.Equal(int.MaxValue, items.CountOf(Steel));

        items.Add(Tin, 1);
        Assert.True(pool.Consume(needs));
        Assert.Equal(0, items.CountOf(Steel));
        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void BulkJunkPaysTheEntireRequirementAndReturnsTheRemainder()
    {
        var items = new PlayerItems();
        items.Add(Tin, int.MaxValue);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, int.MaxValue)]));
        Assert.Equal(int.MaxValue / 2, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void RefundThatWouldOverflowHeldScrapLeavesTheJunkIntact()
    {
        var items = new PlayerItems();
        items.Add(ScrapSteel, int.MaxValue);
        items.Add(Tin, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.False(pool.Consume([Need(Steel, 1)]));
        Assert.Equal(1, items.CountOf(Tin));
        Assert.Equal(int.MaxValue, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void RepeatedRequirementsSpendAcrossStatesWhenTheirCombinedCountExceedsIntMax()
    {
        var items = new PlayerItems();
        var modified = new ItemState([Gear]);
        items.Add(Tin, modified, 3);
        items.Add(Tin, int.MaxValue);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Tin, int.MaxValue), Need(Tin, 1)]));
        Assert.Equal(0, items.CountOf(Tin, ItemState.Plain));
        Assert.Equal(2, items.CountOf(Tin, modified));
    }

    [Fact]
    public void SpendingScrapInTheSameRecipeMakesRoomForItsRefund()
    {
        var items = new PlayerItems();
        items.Add(ScrapSteel, int.MaxValue);
        items.Add(Tin, 1);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.Consume([Need(Steel, 1), Need(ScrapSteel, 1)]));
        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(int.MaxValue, items.CountOf(ScrapSteel));
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

    [Fact]
    public void ASearchFindsThePlanWhenGreedyScrapsTheItemALaterMaterialNeedsDirectly()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, 3));
        var items = new PlayerItems();
        items.Add(Wrench, 1);
        items.Add(Tin, 1);
        var pool = new ComponentPool(reader, items);
        WorkshopMaterial[] needs = [Need(Steel, 1), Need(Wrench, 1)];

        Assert.True(pool.CanConsume(needs));
        Assert.True(pool.Consume(needs));

        Assert.Equal(0, items.CountOf(Wrench));
        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void ASearchScrapsFewerUnitsOfEachJunkWhenLaterMaterialsNeedTheRest()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, 3));
        var items = new PlayerItems();
        items.Add(Wrench, 2);
        items.Add(Tin, 2);
        var pool = new ComponentPool(reader, items);
        WorkshopMaterial[] needs = [Need(Steel, 5), Need(Wrench, 1), Need(Tin, 1)];

        Assert.True(pool.CanConsume(needs));
        Assert.True(pool.Consume(needs));

        Assert.Equal(0, items.CountOf(Wrench));
        Assert.Equal(0, items.CountOf(Tin));
        Assert.Equal(0, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void ASearchFindsThePlanAcrossManyInterchangeableJunkTypesWithinItsBudget()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, 3));
        var items = new PlayerItems();
        items.Add(Wrench, 1);
        for (var i = 0; i < 11; i++)
        {
            var id = $"0001{i:X2}:Fallout4.esm";
            Junk(reader, id, "Scrap " + i, Part(Steel, 2));
            items.Add(id, 1);
        }
        var pool = new ComponentPool(reader, items);
        WorkshopMaterial[] needs = [Need(Steel, 13), Need(Wrench, 1)];

        Assert.True(pool.CanConsume(needs));
        Assert.True(pool.Consume(needs));

        Assert.Equal(0, items.CountOf(Wrench));
        Assert.Equal(1, items.CountOf(ScrapSteel));
        Assert.Equal(4, Enumerable.Range(0, 11).Sum(i => items.CountOf($"0001{i:X2}:Fallout4.esm")));
    }

    [Fact]
    public void ASearchStillRefusesWhenNoCombinationSatisfiesEveryMaterial()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, 3));
        var items = new PlayerItems();
        items.Add(Wrench, 1);
        var pool = new ComponentPool(reader, items);
        WorkshopMaterial[] needs = [Need(Steel, 1), Need(Wrench, 1)];

        Assert.False(pool.CanConsume(needs));
        Assert.False(pool.Consume(needs));
        Assert.Equal(1, items.CountOf(Wrench));
    }

    [Fact]
    public void AFullPlayerRefundStackProtectsJunkInTheStash()
    {
        var held = new PlayerItems();
        held.Add(ScrapSteel, int.MaxValue);
        var stash = new PlayerItems();
        stash.Add(Tin, 1);
        var pool = new ComponentPool(Data(), held, [stash]);

        Assert.False(pool.CanConsume([Need(Steel, 1)]));
        Assert.False(pool.Consume([Need(Steel, 1)]));
        Assert.Equal(1, stash.CountOf(Tin));
        Assert.True(pool.Consume([Need(Steel, 1), Need(ScrapSteel, 1)]));
        Assert.Equal(int.MaxValue, held.CountOf(ScrapSteel));
    }

    [Fact]
    public void HugeJunkYieldsRefundEveryUnitAfterPayingTheRecipe()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, int.MaxValue - 1));
        var held = new PlayerItems();
        held.Add(Wrench, 2);
        var pool = new ComponentPool(reader, held);

        Assert.True(pool.Consume([Need(Steel, int.MaxValue)]));
        Assert.Equal(int.MaxValue - 2, held.CountOf(ScrapSteel));
    }

    [Fact]
    public void AnOversizeJunkRecordCannotBeConsumedForATruncatedYield()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, uint.MaxValue));
        var held = new PlayerItems();
        held.Add(Wrench, 1);
        var pool = new ComponentPool(reader, held);

        Assert.False(pool.Consume([Need(Steel, 1)]));
        Assert.Equal(1, held.CountOf(Wrench));
    }

    [Fact]
    public void ABenchCannotCraftAnOversizeRecipeAtATruncatedPrice()
    {
        var reader = Data();
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Chem, "KYWD", "WorkbenchChemlab");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, Chem, [], (Steel, int.MaxValue), (Steel, 1));
        var held = new PlayerItems();
        held.Add(Steel, int.MaxValue);
        var bench = new RecordBenchRecipes(reader, held, [Chem], "Chem Station");

        Assert.False(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(int.MaxValue, held.CountOf(Steel));
        Assert.Equal(0, held.CountOf(Stim));
    }

    [Fact]
    public void WorkshopKeepsOversizeMaterialDisplayButRefusesPlacementAndScrap()
    {
        const string recipe = "000E01:Fallout4.esm";
        var reader = Data();
        reader.AddRecipe(recipe, Stim, Chem, [], (Steel, int.MaxValue), (Steel, 1));
        var held = new PlayerItems();
        held.Add(Steel, int.MaxValue);
        var catalog = new RecordWorkshopCatalog(reader, new ComponentPool(reader, held), [Chem]);
        var rules = new WorkshopScrapRules(reader, [Chem]);

        Assert.Equal(0, catalog.BuildableCount(recipe));
        Assert.Equal(WorkshopItemStatus.LackingMaterials, catalog.StatusOf(recipe));
        Assert.Null(rules.TargetFor("target", Stim));
    }

    [Fact]
    public void CraftOutputAndScrapRefundMustFitTogether()
    {
        var reader = Data();
        reader.AddRecipe("000E01:Fallout4.esm", ScrapSteel, Chem, [], (Steel, 1));
        var held = new PlayerItems();
        held.Add(ScrapSteel, int.MaxValue - 1);
        held.Add(Tin, 1);
        var bench = new RecordBenchRecipes(reader, held, [Chem], "Chem Station");

        Assert.False(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(int.MaxValue - 1, held.CountOf(ScrapSteel));
        Assert.Equal(1, held.CountOf(Tin));

        Junk(reader, Wrench, "Wrench", Part(Steel, 1));
        held.Add(Wrench, 1);
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
        Assert.Equal(int.MaxValue, held.CountOf(ScrapSteel));
        Assert.Equal(1, held.CountOf(Tin));
        Assert.Equal(0, held.CountOf(Wrench));
    }

    [Fact]
    public void WorkshopBuildCountUsesTheSameSeparatePaymentsAsPlacement()
    {
        var reader = Data();
        const string recipe = "000E01:Fallout4.esm";
        reader.AddRecipe(recipe, Stim, Chem, [], (Steel, 1));
        var held = new PlayerItems();
        held.Add(Tin, 3);
        held.Add(ScrapSteel, int.MaxValue);
        var catalog = new RecordWorkshopCatalog(reader, new ComponentPool(reader, held), [Chem]);

        Assert.Equal(0, catalog.BuildableCount(recipe));
        held.Remove(ScrapSteel, 1);
        Assert.Equal(1, catalog.BuildableCount(recipe));
        Assert.Equal(3, held.CountOf(Tin));
        Assert.Equal(int.MaxValue - 1, held.CountOf(ScrapSteel));
    }

    [Fact]
    public void WorkshopCountsRepeatedExactJunkPaymentsAcrossPlayerAndStash()
    {
        var reader = Data();
        const string recipe = "000E01:Fallout4.esm";
        reader.AddRecipe(recipe, Stim, Chem, [], (Steel, 2));
        var held = new PlayerItems();
        held.Add(Tin, 400);
        var stash = new PlayerItems();
        stash.Add(Tin, 598);
        var catalog = new RecordWorkshopCatalog(reader, new ComponentPool(reader, held, [stash]), [Chem]);

        Assert.Equal(998, catalog.BuildableCount(recipe));
        stash.Add(Tin, 1);
        Assert.Equal(999, catalog.BuildableCount(recipe));
        Assert.Equal(400, held.CountOf(Tin));
        Assert.Equal(599, stash.CountOf(Tin));
    }

    [Fact]
    public void WorkshopCountMatchesSeparatePaymentsWhenThePreferredDonorRunsOut()
    {
        var reader = Data();
        const string recipe = "000E01:Fallout4.esm";
        Junk(reader, Wrench, "Wrench", Part(Steel, 4));
        reader.AddRecipe(recipe, Stim, Chem, [], (Steel, 4));
        var held = new PlayerItems();
        held.Add(Wrench, 2);
        held.Add(Tin, 4);
        var stash = new PlayerItems();
        stash.Add(Wrench, 1);
        stash.Add(Tin, 3);
        var pool = new ComponentPool(reader, held, [stash]);
        var catalog = new RecordWorkshopCatalog(reader, pool, [Chem]);
        WorkshopMaterial[] cost = [Need(Steel, 4)];

        var displayed = catalog.BuildableCount(recipe);
        var placed = 0;
        while (pool.Consume(cost))
        {
            placed++;
            if (placed == 3)
            {
                Assert.Equal(0, held.CountOf(Wrench) + stash.CountOf(Wrench));
                Assert.Equal(7, held.CountOf(Tin) + stash.CountOf(Tin));
            }
        }

        Assert.Equal(6, placed);
        Assert.Equal(placed, displayed);
        Assert.Equal(1, stash.CountOf(Tin));
    }

    [Fact]
    public void WorkshopBatchCanWithdrawMoreThanOneIntAcrossPlayerAndStash()
    {
        var reader = Data();
        const string recipe = "000E01:Fallout4.esm";
        const int cost = 1_500_000_000;
        reader.AddRecipe(recipe, Stim, Chem, [], (Steel, cost));
        var held = new PlayerItems();
        held.Add(Steel, 2_100_000_000);
        var stash = new PlayerItems();
        stash.Add(Steel, 2_100_000_000);
        var pool = new ComponentPool(reader, held, [stash]);
        var catalog = new RecordWorkshopCatalog(reader, pool, [Chem]);

        Assert.Equal(2, catalog.BuildableCount(recipe));
        Assert.True(pool.Consume([Need(Steel, cost)]));
        Assert.True(pool.Consume([Need(Steel, cost)]));
        Assert.False(pool.Consume([Need(Steel, cost)]));
        Assert.Equal(0, held.CountOf(Steel));
        Assert.Equal(1_200_000_000, stash.CountOf(Steel));
    }

    [Fact]
    public void CanConsumeSpendsNothing()
    {
        var items = new PlayerItems();
        items.Add(Tin, 2);
        var pool = new ComponentPool(Data(), items);

        Assert.True(pool.CanConsume([Need(Steel, 3)]));

        Assert.Equal(2, items.CountOf(Tin));
    }

    [Fact]
    public void ABenchRecipeShowsAvailableOnlyWhenConsumeCanSatisfyIt()
    {
        var reader = Data();
        Junk(reader, Wrench, "Wrench", Part(Steel, 3));
        reader.Add(Stim, "ALCH", name: "Stimpak");
        reader.Add(Chem, "KYWD", "WorkbenchChemlab");
        reader.Add(Cat, "KYWD", "WorkshopRecipeFilterChemsHealing");
        reader.AddRecipe("000E01:Fallout4.esm", Stim, Chem, [Cat], (Steel, 1), (Wrench, 1));
        var items = new PlayerItems();
        items.Add(Wrench, 1);
        var bench = new RecordBenchRecipes(reader, items, [Chem], "Chem Station");

        Assert.Equal(WorkbenchOptionState.LackingMaterials, bench.StateOf("bench", "", "000E01:Fallout4.esm"));

        items.Add(Tin, 1);

        Assert.Equal(WorkbenchOptionState.Available, bench.StateOf("bench", "", "000E01:Fallout4.esm"));
        Assert.True(bench.Craft("bench", "", "000E01:Fallout4.esm").Ok);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TransientReadFailureKeepsStashJunkAndAllowsRetry(bool failYield)
    {
        var reader = Data();
        var held = new PlayerItems();
        var stash = new PlayerItems();
        stash.Add(Tin, 1);
        var pool = new ComponentPool(reader, held, [stash]);
        reader.Failing.Add(failYield ? Tin + "|" + ScrapYield.CountPath : Steel + "|" + RecordRows.ComponentScrapItemPath);

        Assert.False(pool.Consume([Need(Steel, 1)]));
        Assert.Equal(1, stash.CountOf(Tin));
        Assert.Empty(held.Entries);

        reader.Failing.Clear();
        Assert.True(pool.Consume([Need(Steel, 1)]));
        Assert.Empty(stash.Entries);
        Assert.Equal(1, held.CountOf(ScrapSteel));
    }

    [Fact]
    public void AnUnreadableJunkRecordIsSkippedAsADonorInsteadOfBlockingTheCraft()
    {
        var reader = Data();
        var items = new PlayerItems();
        items.Add(Tin, 1);
        items.Add(Gear, 1);
        var pool = new ComponentPool(reader, items);
        reader.Failing.Add(Tin + "|" + ScrapYield.CountPath);

        Assert.True(pool.CanConsume([Need(Steel, 1)]));
        Assert.Equal(1, pool.CountPlacements([Need(Steel, 1)], 5));
        Assert.True(pool.Consume([Need(Steel, 1)]));

        Assert.Equal(1, items.CountOf(Tin));
        Assert.Equal(0, items.CountOf(Gear));
        Assert.Equal(4, items.CountOf(ScrapSteel));
    }
}
