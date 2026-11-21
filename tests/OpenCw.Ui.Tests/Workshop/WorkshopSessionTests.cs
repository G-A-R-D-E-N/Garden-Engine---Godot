using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopSessionTests
{
    const string Bench = "000010:a.esm";
    const string Recipe = "000050:a.esm";
    const string Built = "000040:a.esm";
    const string Steel = "000030:a.esm";
    const string ScrapSteel = "000032:a.esm";
    const string Tin = "000031:a.esm";

    sealed record Rig(WorkshopSession Session, PlayerItems Items, List<string> Spawned, List<string> Placed);

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel").Scalars[RecordRows.ComponentScrapItemPath] = ScrapSteel;
        reader.Add(ScrapSteel, "MISC", "ScrapSteel", "Scrap Steel");
        reader.Add(Built, "STAT", "Gen01", "Generator");
        reader.Add(Tin, "MISC", "Tin", "Tin").Parts((Steel, 3));
        reader.AddRecipe(Recipe, Built, Bench, [], (Steel, 5));
        return reader;
    }

    static Rig Make(int tin, bool spawnWorks = true, PlacementStatus placement = PlacementStatus.Valid, bool freeBuild = false)
    {
        var reader = Data();
        var items = new PlayerItems();
        items.Add(Tin, tin);
        var pool = new ComponentPool(reader, items);
        var catalog = new RecordWorkshopCatalog(reader, pool, [Bench], freeBuild);
        var spawned = new List<string>();
        var placed = new List<string>();
        var session = new WorkshopSession(catalog, pool, _ => placement, id =>
        {
            spawned.Add(id);
            return spawnWorks;
        });
        session.Placed += placed.Add;
        return new Rig(session, items, spawned, placed);
    }

    [Fact]
    public void OnlyAContainerWhoseEditorIdIsAWorkshopWorkbenchIsTheBench()
    {
        var reader = Data();
        reader.Add("000100:a.esm", "CONT", "WorkshopWorkbench");
        reader.Add("000101:a.esm", "CONT", "WorkshopWorkbenchInterior");
        reader.Add("000102:a.esm", "CONT", "MetalChest");
        reader.Add("000103:a.esm", "FURN", "WorkshopWorkbenchFake");

        Assert.True(WorkshopSession.IsWorkshopBench(reader, "000100:a.esm"));
        Assert.True(WorkshopSession.IsWorkshopBench(reader, "000101:a.esm"));
        Assert.False(WorkshopSession.IsWorkshopBench(reader, "000102:a.esm"));
        Assert.False(WorkshopSession.IsWorkshopBench(reader, "000103:a.esm"));
        Assert.False(WorkshopSession.IsWorkshopBench(reader, "0001FF:a.esm"));
    }

    [Fact]
    public void AGhostFollowsTheCursorAndReportsThePlacementCheck()
    {
        var rig = Make(2, placement: PlacementStatus.Blocked);

        rig.Session.Follow(new GhostRequested(Recipe));

        Assert.Equal(WorkshopAim.Ghost, rig.Session.Aim);
        Assert.Equal(PlacementStatus.Blocked, rig.Session.Placement);
        rig.Session.Follow(new GhostCleared());
        Assert.Equal(WorkshopAim.Browsing, rig.Session.Aim);
        Assert.Equal(PlacementStatus.Valid, rig.Session.Placement);
    }

    [Fact]
    public void PlacingSpawnsTheObjectAndSpendsJunkComponentsWithTheSurplusRefunded()
    {
        var rig = Make(2);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Equal([Recipe], rig.Spawned);
        Assert.Equal([Recipe], rig.Placed);
        Assert.Equal(0, rig.Items.CountOf(Tin));
        Assert.Equal(1, rig.Items.CountOf(ScrapSteel));
    }

    [Fact]
    public void AFailedSpawnSpendsNothing()
    {
        var rig = Make(2, spawnWorks: false);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Equal([Recipe], rig.Spawned);
        Assert.Empty(rig.Placed);
        Assert.Equal(2, rig.Items.CountOf(Tin));
    }

    [Fact]
    public void AnUnsupportedPlacementNeverSpawnsOrSpends()
    {
        var rig = Make(2, placement: PlacementStatus.Unsupported);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Empty(rig.Spawned);
        Assert.Equal(2, rig.Items.CountOf(Tin));
    }

    [Fact]
    public void LackingMaterialsNeverSpawn()
    {
        var rig = Make(1);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Empty(rig.Spawned);
        Assert.Equal(1, rig.Items.CountOf(Tin));
    }

    [Fact]
    public void FreeBuildPlacesWithoutSpendingAnything()
    {
        var rig = Make(0, freeBuild: true);
        rig.Session.Follow(new GhostRequested(Recipe));

        rig.Session.Follow(new PlaceRequested());

        Assert.Equal([Recipe], rig.Placed);
        Assert.Equal(0, rig.Items.CountOf(ScrapSteel));
    }

    [Fact]
    public void PlaceWithoutAGhostDoesNothing()
    {
        var rig = Make(2);

        rig.Session.Follow(new PlaceRequested());

        Assert.Empty(rig.Spawned);
    }
}
