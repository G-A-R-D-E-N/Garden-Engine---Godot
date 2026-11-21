using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ReadFailureTests
{
    const string Bench = "000010:a.esm";
    const string Recipe = "000050:a.esm";
    const string Built = "000040:a.esm";
    const string Steel = "000030:a.esm";
    const string ScrapSteel = "000032:a.esm";
    const string Wood = "000033:a.esm";
    const string Junk = "000035:a.esm";
    const string Rifle = "000A01:Fallout4.esm";
    const string Scope = "000B01:Fallout4.esm";
    const string Barrel = "000B02:Fallout4.esm";
    const string Loose = "000B03:Fallout4.esm";
    const string ShortBarrel = "000B04:Fallout4.esm";
    const string PointA = "000C01:Fallout4.esm";
    const string PointB = "000C02:Fallout4.esm";
    const string WeaponType = "1346454871";

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel").Scalars[RecordRows.ComponentScrapItemPath] = ScrapSteel;
        reader.Add(Wood, "CMPO", "c_Wood", "Wood");
        reader.Add(ScrapSteel, "MISC", "ScrapSteel", "Scrap Steel");
        reader.Add(Built, "STAT", "Gen01", "Generator");
        reader.Add(Junk, "MISC", "Junk", "Junk").Parts((Steel, 2));
        reader.AddRecipe(Recipe, Built, Bench, [], (Steel, 5), (Wood, 1));
        return reader;
    }

    [Fact]
    public void AScrapWhoseRewardLookupFailsIsRefusedBeforeTheObjectIsRemovedAndWorksOnceTheReadRecovers()
    {
        var reader = Data();
        var items = new PlayerItems();
        var pool = new ComponentPool(reader, items);
        var removed = new List<string>();
        var target = new WorkshopTarget("000901:a.esm", "Generator")
        {
            Scrappable = true,
            Yield = [new WorkshopMaterial(Steel, "Steel", 3), new WorkshopMaterial(Wood, "Wood", 4)],
        };
        var session = new WorkshopSession(new RecordWorkshopCatalog(reader, pool, [Bench]), pool, _ => PlacementStatus.Valid, _ => true, () => target,
            id => { removed.Add(id); return true; });
        reader.Failing.Add(Steel);

        session.Follow(new ScrapRequested(target.Id));

        Assert.Empty(removed);
        Assert.Empty(items.Entries);
        reader.Failing.Clear();
        session.Follow(new ScrapRequested(target.Id));
        Assert.Single(removed);
        Assert.Equal(3, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void AJunkYieldThatFailedToReadIsRetriedInTheSameSession()
    {
        var reader = Data();
        var items = new PlayerItems();
        items.Add(Junk, 1);
        var pool = new ComponentPool(reader, items);
        reader.Failing.Add(Junk);

        Assert.Equal(0, pool.CountOf(Steel));

        reader.Failing.Clear();
        Assert.Equal(2, pool.CountOf(Steel));
    }

    [Fact]
    public void ARefundMappingThatCannotBeReadFailsThePlanAndKeepsTheJunk()
    {
        var reader = Data();
        var items = new PlayerItems();
        items.Add(Junk, 1);
        var pool = new ComponentPool(reader, items);
        reader.Failing.Add(Steel);

        Assert.False(pool.Consume([new WorkshopMaterial(Steel, "Steel", 1)]));
        Assert.Equal(1, items.CountOf(Junk));

        reader.Failing.Clear();
        Assert.True(pool.Consume([new WorkshopMaterial(Steel, "Steel", 1)]));
        Assert.Equal(0, items.CountOf(Junk));
        Assert.Equal(1, items.CountOf(ScrapSteel));
    }

    [Fact]
    public void AComponentRequirementNearIntMaxTerminatesInsteadOfLooping()
    {
        var reader = Data();
        reader.Add(Junk, "MISC", "Junk", "Junk").Parts((Steel, 2_000_000_000));
        var items = new PlayerItems();
        items.Add(Junk, 1);
        var pool = new ComponentPool(reader, items);

        Assert.True(pool.Consume([new WorkshopMaterial(Steel, "Steel", 2_000_000_000)]));

        Assert.Equal(0, items.CountOf(Junk));
    }

    [Fact]
    public void AnOversizedRequirementThatJunkCannotCoverFailsInsteadOfLooping()
    {
        var reader = Data();
        reader.Add(Junk, "MISC", "Junk", "Junk").Parts((Steel, 2_000_000_000));
        var items = new PlayerItems();
        items.Add(Junk, 1);
        var pool = new ComponentPool(reader, items);

        Assert.False(pool.Consume([new WorkshopMaterial(Steel, "Steel", 2_000_000_000), new WorkshopMaterial(Steel, "Steel", 2_000_000_000)]));
        Assert.Equal(1, items.CountOf(Junk));
    }

    [Fact]
    public void RepeatedWorkshopMaterialRowsAreSummedBeforeAvailabilityAndBuildableCount()
    {
        var reader = Data();
        reader.AddRecipe(Recipe, Built, Bench, [], (Steel, 1), (Steel, 1));
        var stock = new FakeStock { Counts = { [Steel] = 1 } };
        var catalog = new RecordWorkshopCatalog(reader, stock, [Bench]);

        Assert.Equal(0, catalog.BuildableCount(Recipe));
        Assert.Equal(WorkshopItemStatus.LackingMaterials, catalog.StatusOf(Recipe));
        stock.Counts[Steel] = 2;
        Assert.Equal(1, catalog.BuildableCount(Recipe));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    [Fact]
    public void AScrapIndexBuiltAcrossAFailedScanIsNotKept()
    {
        var reader = Data();
        var rules = new WorkshopScrapRules(reader, [Bench]);
        reader.WinnersFail = true;

        Assert.Null(rules.PartsOf(Built));

        reader.WinnersFail = false;
        Assert.Equal([Steel, Wood], rules.PartsOf(Built)!.Select(part => part.Id).Order());
    }

    [Fact]
    public void AnUnreadableComponentRowMakesTheRecipeUnreadableNotCheaper()
    {
        var reader = Data();
        reader.Failing.Add(Recipe);

        Assert.Null(RecordRows.Components(reader, Recipe, RecordRows.RecipeComponentObjectPath, RecordRows.RecipeComponentCountPath));
    }

    [Fact]
    public void AnEmptyComponentLinkMakesTheRecipeUnreadableNotCheaper()
    {
        var reader = Data();
        reader.Records[Recipe].Lists[RecordRows.RecipeComponentObjectPath] = [Steel, ""];
        reader.Records[Recipe].Lists[RecordRows.RecipeComponentCountPath] = ["5", "1"];

        Assert.Null(RecordRows.Components(reader, Recipe, RecordRows.RecipeComponentObjectPath, RecordRows.RecipeComponentCountPath));
    }

    static FakeRecordReader ModData(out PlayerItems items)
    {
        var reader = new FakeRecordReader();
        reader.Add(Rifle, "WEAP", name: "Combat Rifle").Lists["WEAP\\AttachParentSlots"] = [PointB];
        reader.Add(Steel, "MISC", name: "Steel");
        reader.Add(Loose, "MISC", name: "Loose Barrel");
        var scope = reader.Add(Scope, "OMOD", name: "Scope");
        scope.Scalars["OMOD\\FormType"] = WeaponType;
        scope.Scalars["OMOD\\AttachPoint"] = PointA;
        var barrel = reader.Add(Barrel, "OMOD", name: "Long Barrel");
        barrel.Scalars["OMOD\\FormType"] = WeaponType;
        barrel.Scalars["OMOD\\AttachPoint"] = PointB;
        barrel.Scalars["OMOD\\LooseMod"] = Loose;
        reader.AddRecipe("000E01:Fallout4.esm", Scope, Bench, [], (Steel, 1));
        reader.AddRecipe("000E02:Fallout4.esm", Barrel, Bench, [], (Steel, 1));
        var shortBarrel = reader.Add(ShortBarrel, "OMOD", name: "Short Barrel");
        shortBarrel.Scalars["OMOD\\FormType"] = WeaponType;
        shortBarrel.Scalars["OMOD\\AttachPoint"] = PointB;
        reader.AddRecipe("000E03:Fallout4.esm", ShortBarrel, Bench, [], (Steel, 1));
        items = new PlayerItems();
        items.Add(Steel, 5);
        return reader;
    }

    static string T(string id, params string[] mods) => $"item:{id}#{string.Join(',', mods)}#1#0";

    [Fact]
    public void AnAttachPointThatFailedToReadWhenBuiltIsRereadAndItsReachabilityIsEnforced()
    {
        var reader = ModData(out var items);
        items.Add(Rifle, 1);
        reader.Failing.Add(Scope + "|OMOD\\AttachPoint");
        var bench = new RecordBenchRecipes(reader, items, [Bench], "Weapon Workbench", benchType: 2);
        reader.Failing.Clear();

        Assert.Equal(OpenCommonwealth.Services.Ui.Workbench.WorkbenchOptionState.Unavailable, bench.StateOf(T(Rifle), "Other", "000E01:Fallout4.esm"));
        Assert.Equal(OpenCommonwealth.Services.Ui.Workbench.WorkbenchOptionState.Available, bench.StateOf(T(Rifle), "Other", "000E02:Fallout4.esm"));
    }

    [Fact]
    public void ACraftIsRefusedWhenAnInstalledModsAttachPointCannotBeRead()
    {
        var reader = ModData(out var items);
        var attached = new ItemState([Barrel]);
        items.Add(Rifle, attached, 1);
        var bench = new RecordBenchRecipes(reader, items, [Bench], "Weapon Workbench", benchType: 2);
        reader.Failing.Add(Barrel + "|OMOD\\AttachPoint");

        var result = bench.Craft(T(Rifle, Barrel), "Other", "000E03:Fallout4.esm");

        Assert.False(result.Ok);
        Assert.Equal(RecordBenchRecipes.UnreadableText, result.Reason);
        Assert.Equal(1, items.CountOf(Rifle, attached));
        Assert.Equal(5, items.CountOf(Steel));
    }

    [Fact]
    public void DetachingAModWhoseLooseItemCannotBeReadIsRefusedWithoutChangingTheItem()
    {
        var reader = ModData(out var items);
        var attached = new ItemState([Barrel]);
        items.Add(Rifle, attached, 1);
        var bench = new RecordBenchRecipes(reader, items, [Bench], "Weapon Workbench", benchType: 2);
        reader.Failing.Add(Barrel + "|OMOD\\LooseMod");

        var refused = bench.Craft(T(Rifle, Barrel), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Barrel);

        Assert.False(refused.Ok);
        Assert.Equal(RecordBenchRecipes.UnreadableText, refused.Reason);
        Assert.Equal(1, items.CountOf(Rifle, attached));
        Assert.Equal(0, items.CountOf(Loose));

        reader.Failing.Clear();
        Assert.True(bench.Craft(T(Rifle, Barrel), RecordBenchRecipes.DetachSlot, RecordBenchRecipes.DetachPrefix + Barrel).Ok);
        Assert.Equal(1, items.CountOf(Rifle, ItemState.Plain));
        Assert.Equal(1, items.CountOf(Loose));
    }
}
