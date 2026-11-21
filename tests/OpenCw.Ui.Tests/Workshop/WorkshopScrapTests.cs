using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopScrapTests
{
    const string Bench = "000010:a.esm";
    const string Recipe = "000050:a.esm";
    const string Built = "000040:a.esm";
    const string Steel = "000030:a.esm";
    const string ScrapSteel = "000032:a.esm";
    const string Tin = "000031:a.esm";
    const string Wood = "000033:a.esm";
    const string ScrapWood = "000034:a.esm";
    const string Junk = "000035:a.esm";

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
    public void ADroppedStackYieldsTheComponentsOfEveryCopy()
    {
        var rules = new WorkshopScrapRules(Data(), [Bench]);

        var one = rules.TargetFor("000901:a.esm", Junk)!;
        var four = rules.TargetFor("000901:a.esm", Junk, count: 4)!;
        var huge = rules.TargetFor("000901:a.esm", Junk, count: int.MaxValue)!;

        Assert.Equal(2, one.Yield.Single().Needed);
        Assert.Equal(8, four.Yield.Single().Needed);
        Assert.Equal(int.MaxValue, huge.Yield.Single().Needed);
    }

    static WorkshopTarget Aimable(string id, IReadOnlyList<WorkshopMaterial> yield, bool blocked = false) =>
        new(id, "Generator") { Scrappable = true, ScrapBlocked = blocked, Yield = yield };

    sealed record Rig(WorkshopSession Session, PlayerItems Items, List<string> Removed, List<string> Scrapped, List<string> Undone);

    static Rig Make(WorkshopTarget? target, bool removeWorks = true, int steel = 0, bool lyingStock = false, bool placeWorks = true)
    {
        var reader = Data();
        var items = new PlayerItems();
        if (steel > 0) items.Add(Steel, steel);
        var pool = new ComponentPool(reader, items);
        IItemStock stock = lyingStock ? new FakeStock { Counts = { [Steel] = 99, [Wood] = 99 } } : pool;
        var catalog = new RecordWorkshopCatalog(reader, stock, [Bench]);
        var removed = new List<string>();
        var undone = new List<string>();
        var session = new WorkshopSession(catalog, pool, _ => PlacementStatus.Valid, _ => placeWorks, () => target,
            id => { removed.Add(id); return removeWorks; }, undone.Add);
        var scrapped = new List<string>();
        session.Scrapped += scrapped.Add;
        return new Rig(session, items, removed, scrapped, undone);
    }

    [Fact]
    public void ScrappingTheAimedObjectRemovesItAndPaysItsPartsAsScrapItems()
    {
        var rig = Make(Aimable("Built:000001", [new WorkshopMaterial(Steel, "Steel", 5), new WorkshopMaterial(Wood, "Wood", 1)]));

        Assert.Equal(WorkshopAim.TargetObject, rig.Session.Aim);
        rig.Session.Follow(new ScrapRequested("Built:000001"));

        Assert.Equal(["Built:000001"], rig.Removed);
        Assert.Equal(["Built:000001"], rig.Scrapped);
        Assert.Equal(5, rig.Items.CountOf(ScrapSteel));
        Assert.Equal(0, rig.Items.CountOf(ScrapWood));
    }

    [Fact]
    public void AFailedRemovalPaysNothing()
    {
        var rig = Make(Aimable("Built:000001", [new WorkshopMaterial(Steel, "Steel", 5)]), removeWorks: false);

        rig.Session.Follow(new ScrapRequested("Built:000001"));

        Assert.Equal(["Built:000001"], rig.Removed);
        Assert.Empty(rig.Scrapped);
        Assert.Equal(0, rig.Items.CountOf(ScrapSteel));
    }

    [Fact]
    public void ScrapOnlyActsOnTheObjectCurrentlyAimedAt()
    {
        var rig = Make(Aimable("Built:000001", [new WorkshopMaterial(Steel, "Steel", 5)]));

        rig.Session.Follow(new ScrapRequested("Built:000099"));

        Assert.Empty(rig.Removed);
    }

    [Fact]
    public void ABlockedOrNotScrappableTargetIsNeverRemoved()
    {
        var blocked = Make(Aimable("Built:000001", [new WorkshopMaterial(Steel, "Steel", 5)], blocked: true));
        var plain = Make(new WorkshopTarget("Built:000001", "Generator"));

        blocked.Session.Follow(new ScrapRequested("Built:000001"));
        plain.Session.Follow(new ScrapRequested("Built:000001"));

        Assert.Empty(blocked.Removed);
        Assert.Empty(plain.Removed);
    }

    [Fact]
    public void WithNothingAimedTheSessionBrowses()
    {
        var rig = Make(null);

        Assert.Equal(WorkshopAim.Browsing, rig.Session.Aim);
        rig.Session.Follow(new GhostRequested(Recipe));
        Assert.Equal(WorkshopAim.Ghost, rig.Session.Aim);
    }

    [Fact]
    public void WhenTheMaterialsCannotBeSpentThePlacedObjectIsUndone()
    {
        var rig = Make(null, lyingStock: true);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Equal([Recipe], rig.Undone);
        Assert.Equal(0, rig.Items.CountOf(Steel));
    }

    [Fact]
    public void ASuccessfulPlacementIsNotUndone()
    {
        var rig = Make(null, steel: 5);
        rig.Items.Add(Wood, 1);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Empty(rig.Undone);
        Assert.Equal(0, rig.Items.CountOf(Steel));
    }

    [Fact]
    public void ARecipeObjectYieldsItsRecipeComponentsAndJunkYieldsItsMiscComponents()
    {
        var rules = new WorkshopScrapRules(Data(), [Bench]);

        var built = rules.TargetFor("000900:a.esm", Built)!;
        var junk = rules.TargetFor("000901:a.esm", Junk)!;

        Assert.True(built.Scrappable);
        Assert.Equal("Generator", built.Name);
        Assert.Equal([(Steel, 5), (Wood, 1)], built.Yield.Select(part => (part.Id, part.Needed)).ToArray());
        Assert.Equal([(Steel, 2)], junk.Yield.Select(part => (part.Id, part.Needed)).ToArray());
    }

    [Fact]
    public void ARecipeAtAChemOrWeaponBenchDoesNotMakeItsObjectScrappable()
    {
        var reader = Data();
        const string Weapon = "000070:a.esm";
        const string Pistol = "000071:a.esm";
        const string PistolRecipe = "000072:a.esm";
        reader.Add(Weapon, "KYWD", "WorkbenchWeapon");
        reader.Add(Pistol, "WEAP", "Pistol", "Pistol");
        reader.AddRecipe(PistolRecipe, Pistol, Weapon, [], (Steel, 3));

        Assert.Null(new WorkshopScrapRules(reader, [Bench]).TargetFor("000901:a.esm", Pistol));
        Assert.NotNull(new WorkshopScrapRules(reader, [Bench, Weapon]).TargetFor("000901:a.esm", Pistol));
    }

    [Fact]
    public void AnObjectWithNoRecipeAndNoJunkComponentsIsNotScrappable()
    {
        var reader = Data();
        reader.Add("000060:a.esm", "STAT", "Wall", "Wall");

        Assert.Null(new WorkshopScrapRules(reader, [Bench]).TargetFor("000901:a.esm", "000060:a.esm"));
        Assert.Null(new WorkshopScrapRules(reader, [Bench]).TargetFor("000901:a.esm", "0000FF:a.esm"));
    }

    [Fact]
    public void CreditPaysTheScrapItemOfEachComponentAndSkipsComponentsWithNone()
    {
        var reader = Data();
        var items = new PlayerItems();
        var pool = new ComponentPool(reader, items);

        pool.Credit([new WorkshopMaterial(Steel, "Steel", 3), new WorkshopMaterial(Wood, "Wood", 4)]);

        Assert.Equal(3, items.CountOf(ScrapSteel));
        Assert.Single(items.Stacks);
    }

    [Fact]
    public void BuiltObjectsRoundTripThroughASaveSectionAndKeepTheTokenCounter()
    {
        var saved = new BuiltObjectLedger();
        var first = saved.Add("000100:a.esm", Recipe, Built, 1.5, 2, -3, 0.25);
        saved.Add("000100:a.esm", Recipe, Built, 4, 5, 6, 0);

        var loaded = new BuiltObjectLedger();
        loaded.Apply(saved.Sections());

        Assert.Equal(2, loaded.All.Count);
        Assert.Equal(first, loaded.All[0]);
        Assert.Equal("Built:000003", loaded.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0).Token);
    }

    [Fact]
    public void ARemovedBuiltObjectIsGoneFromItsCellAndTheSave()
    {
        var ledger = new BuiltObjectLedger();
        var built = ledger.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0);
        ledger.Add("000200:a.esm", Recipe, Built, 0, 0, 0, 0);

        Assert.True(ledger.Remove(built.Token));
        Assert.False(ledger.Remove(built.Token));

        Assert.Empty(ledger.InCell("000100:a.esm"));
        Assert.Single(ledger.InCell("000200:A.ESM"));
    }

    [Fact]
    public void LoadingASaveWithoutTheSectionClearsBuiltObjects()
    {
        var ledger = new BuiltObjectLedger();
        ledger.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0);

        ledger.Apply(new Dictionary<string, byte[]>());

        Assert.True(ledger.IsEmpty);
        Assert.Empty(ledger.Sections());
    }

    [Fact]
    public void ReconcileFreesLiveObjectsTheSaveLacksAndSpawnsSavedOnesThatAreNotLive()
    {
        var ledger = new BuiltObjectLedger();
        var a = ledger.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0);
        var b = ledger.Add("000100:a.esm", Recipe, Built, 1, 0, 0, 0);
        var other = ledger.Add("000200:a.esm", Recipe, Built, 0, 0, 0, 0);
        ledger.Remove(b.Token);

        var plan = ledger.Reconcile("000100:A.ESM", [a.Token, b.Token], _ => false);

        Assert.Equal([b.Token], plan.Free);
        Assert.Empty(plan.Spawn);
        Assert.Equal([a], ledger.Reconcile("000100:a.esm", [], _ => false).Spawn);
        Assert.Equal([other], ledger.Reconcile("000200:a.esm", [], _ => false).Spawn);
    }

    [Fact]
    public void ReconcileNeitherKeepsNorSpawnsATakenToken()
    {
        var ledger = new BuiltObjectLedger();
        var a = ledger.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0);

        Assert.Equal([a.Token], ledger.Reconcile("000100:a.esm", [a.Token], token => token == a.Token).Free);
        Assert.Empty(ledger.Reconcile("000100:a.esm", [], token => token == a.Token).Spawn);
    }

    [Fact]
    public void ReconcileWithAnEmptyLedgerFreesEveryLiveObject()
    {
        var plan = new BuiltObjectLedger().Reconcile("000100:a.esm", ["Built:000001", "Built:000002"], _ => false);

        Assert.Equal(["Built:000001", "Built:000002"], plan.Free);
        Assert.Empty(plan.Spawn);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"token\":\"Dynamic:1\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":1e100,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":-3.4028236e38,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":1e39,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":1e39}]")]
    [InlineData("not json")]
    public void AMalformedSectionThrowsAndKeepsTheCurrentBuiltObjects(string payload)
    {
        var ledger = new BuiltObjectLedger();
        ledger.Add("000100:a.esm", Recipe, Built, 0, 0, 0, 0);

        Assert.Throws<System.FormatException>(() => ledger.Apply(new Dictionary<string, byte[]> { [BuiltObjectLedger.SectionName] = Encoding.UTF8.GetBytes(payload) }));
        Assert.Single(ledger.All);
    }

    [Fact]
    public void APoseAtTheFloatLimitLoadsAndOneJustPastItIsRefused()
    {
        const string Limit = "3.4028234663852886e38";
        var row = "[{\"token\":\"Built:000001\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":" + Limit + ",\"y\":0,\"z\":0,\"yaw\":-" + Limit + "}]";
        var ledger = new BuiltObjectLedger();

        ledger.Apply(new Dictionary<string, byte[]> { [BuiltObjectLedger.SectionName] = Encoding.UTF8.GetBytes(row) });

        Assert.Equal((double)float.MaxValue, Assert.Single(ledger.All).X);
        Assert.Equal(-(double)float.MaxValue, ledger.All[0].Yaw);
        Assert.Throws<System.FormatException>(() => ledger.Apply(new Dictionary<string, byte[]> { [BuiltObjectLedger.SectionName] = Encoding.UTF8.GetBytes(row.Replace("\"x\":" + Limit, "\"x\":3.4028236e38")) }));
        Assert.Single(ledger.All);
    }

    [Fact]
    public void TheHighestAcceptedTokenExhaustsTheCounterAndThePlacementIsRefusedInsteadOfWrapping()
    {
        const string Row = "[{\"token\":\"Built:2147483647\",\"cell\":\"c\",\"recipe\":\"r\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]";
        var ledger = new BuiltObjectLedger();
        ledger.Apply(new Dictionary<string, byte[]> { [BuiltObjectLedger.SectionName] = Encoding.UTF8.GetBytes(Row) });

        Assert.False(ledger.CanAdd);
        Assert.Throws<System.InvalidOperationException>(() => ledger.Add("c", "r", "b", 0, 0, 0, 0));
        Assert.Single(ledger.All);

        var loaded = new BuiltObjectLedger();
        loaded.Apply(ledger.Sections());
        Assert.Equal("Built:2147483647", Assert.Single(loaded.All).Token);

        var below = new BuiltObjectLedger();
        below.Apply(new Dictionary<string, byte[]> { [BuiltObjectLedger.SectionName] = Encoding.UTF8.GetBytes(Row.Replace("2147483647", "2147483646")) });
        Assert.Equal("Built:2147483647", below.Add("c", "r", "b", 0, 0, 0, 0).Token);
        Assert.False(below.CanAdd);
    }

    [Fact]
    public void ABuiltObjectYieldsTheComponentsOfTheRecipeItWasBuiltWith()
    {
        var reader = Data();
        reader.AddRecipe("000051:a.esm", Built, Bench, [], (Steel, 1));
        var rules = new WorkshopScrapRules(reader, [Bench]);

        var cheap = rules.TargetFor("Built:000001", Built, recipeId: "000051:a.esm")!;
        var world = rules.TargetFor("000900:a.esm", Built)!;

        Assert.Equal([(Steel, 1)], cheap.Yield.Select(part => (part.Id, part.Needed)).ToArray());
        Assert.Equal([(Steel, 5), (Wood, 1)], world.Yield.Select(part => (part.Id, part.Needed)).ToArray());
    }

    [Fact]
    public void ABuiltObjectWhoseRecipeCannotBeReadFallsBackToTheBaseYield()
    {
        var rules = new WorkshopScrapRules(Data(), [Bench]);

        var target = rules.TargetFor("Built:000001", Built, recipeId: "0000AA:a.esm")!;

        Assert.Equal([(Steel, 5), (Wood, 1)], target.Yield.Select(part => (part.Id, part.Needed)).ToArray());
    }

    [Fact]
    public void ABlockedTargetCarriesItsReasonAndAnUnblockedOneDoesNot()
    {
        var rules = new WorkshopScrapRules(Data(), [Bench]);

        var blocked = rules.TargetFor("000900:a.esm", Built, true, null, "Empty it first.")!;
        var open = rules.TargetFor("000900:a.esm", Built, false, null, "Empty it first.")!;

        Assert.True(blocked.ScrapBlocked);
        Assert.Equal("Empty it first.", blocked.ScrapBlockedReason);
        Assert.Null(open.ScrapBlockedReason);
    }

    [Fact]
    public void AContainerHoldsItemsByItsLedgerCopyOtherwiseByItsSeedAndAnUnreadableSeedFailsClosed()
    {
        var full = new PlayerItems();
        full.Add(Steel, 1);
        var empty = new PlayerItems();
        List<InventoryStack> none = [];
        List<InventoryStack> some = [new InventoryStack(Steel, 2)];

        Assert.True(WorkshopScrapRules.HoldsItems(full, false, () => none));
        Assert.False(WorkshopScrapRules.HoldsItems(empty, false, () => some));
        Assert.True(WorkshopScrapRules.HoldsItems(null, false, () => some));
        Assert.False(WorkshopScrapRules.HoldsItems(null, false, () => none));
        Assert.True(WorkshopScrapRules.HoldsItems(null, false, () => null));
        Assert.False(WorkshopScrapRules.HoldsItems(null, true, () => some));
    }
}
