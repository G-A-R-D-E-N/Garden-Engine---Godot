using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopStashTests
{
    const string Bench = "000010:a.esm";
    const string Recipe = "000050:a.esm";
    const string Built = "000040:a.esm";
    const string Steel = "000030:a.esm";
    const string ScrapSteel = "000032:a.esm";

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel").Scalars[RecordRows.ComponentScrapItemPath] = ScrapSteel;
        reader.Add(ScrapSteel, "MISC", "ScrapSteel", "Scrap Steel");
        reader.Add(Built, "STAT", "Gen01", "Generator");
        reader.AddRecipe(Recipe, Built, Bench, [], (Steel, 5));
        return reader;
    }

    // Detects: workshop materials that never count; the pool must add the stash to the player's own stock.
    [Fact]
    public void The_stash_counts_toward_the_stock()
    {
        var items = new PlayerItems();
        items.Add(Steel, 2);
        var stash = new PlayerItems();
        stash.Add(Steel, 4);
        var pool = new ComponentPool(Data(), items, [stash]);
        Assert.Equal(6, pool.CountOf(Steel));
    }

    // Detects: a purchase that spends only the player's items; the player's own stock must be spent first and the stash must cover the rest.
    [Fact]
    public void Consuming_takes_the_players_items_first_then_the_stash()
    {
        var items = new PlayerItems();
        items.Add(Steel, 2);
        var stash = new PlayerItems();
        stash.Add(Steel, 4);
        var pool = new ComponentPool(Data(), items, [stash]);
        Assert.True(pool.Consume([new WorkshopMaterial(Steel, "Steel", 5)]));
        Assert.Equal(0, items.CountOf(Steel));
        Assert.Equal(1, stash.CountOf(Steel));
    }

    [Fact]
    public void A_shortfall_across_both_stores_spends_nothing()
    {
        var items = new PlayerItems();
        items.Add(Steel, 2);
        var stash = new PlayerItems();
        stash.Add(Steel, 2);
        var pool = new ComponentPool(Data(), items, [stash]);
        Assert.False(pool.Consume([new WorkshopMaterial(Steel, "Steel", 5)]));
        Assert.Equal(2, items.CountOf(Steel));
        Assert.Equal(2, stash.CountOf(Steel));
    }

    // Detects: a build menu that ignores the boundary; the session must report the player outside while the supplied check says so.
    [Fact]
    public void The_session_reports_the_player_outside_the_build_area()
    {
        var reader = Data();
        var pool = new ComponentPool(reader, new PlayerItems());
        var catalog = new RecordWorkshopCatalog(reader, pool, [Bench], true);
        var outside = false;
        var session = new WorkshopSession(catalog, pool, _ => PlacementStatus.OutsideArea, _ => true, outsideArea: () => outside);
        Assert.False(session.OutsideBuildArea);
        outside = true;
        Assert.True(session.OutsideBuildArea);
    }

    // Detects: a placement outside the area that is still spawned; Follow must refuse it.
    [Fact]
    public void An_outside_placement_is_never_spawned()
    {
        var reader = Data();
        var pool = new ComponentPool(reader, new PlayerItems());
        var catalog = new RecordWorkshopCatalog(reader, pool, [Bench], true);
        var spawned = new List<string>();
        var session = new WorkshopSession(catalog, pool, _ => PlacementStatus.OutsideArea, id =>
        {
            spawned.Add(id);
            return true;
        });
        session.Follow(new GhostRequested(Recipe));
        session.Follow(new PlaceRequested());
        Assert.Empty(spawned);
    }
}
