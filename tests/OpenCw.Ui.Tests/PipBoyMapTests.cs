using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyMapTests
{
    static readonly MapBounds World = new(0, 0, 1000, 1000);

    static MapMarker[] Markers() => new[]
    {
        new MapMarker("m1", "Shelter 1", "shelter", 100, 100, CanFastTravel: true),
        new MapMarker("m2", "Corner Store", "shop", 900, 100),
        new MapMarker("m3", "Old Mill", "ruin", 500, 500, Discovered: false, CanFastTravel: true),
        new MapMarker("m4", "Water Tower", "landmark", 110, 100),
    };

    static MapModel Make() => new(World, 500, 500, Markers(), 0.25, 8);

    static string[] Ids(MapModel map) => map.VisibleMarkers.Select(m => m.Id).ToArray();

    [Fact]
    public void Starts_fitted_and_centred_and_screen_conversion_round_trips()
    {
        var map = Make();

        Assert.Equal(0.5, map.Zoom);
        Assert.Equal(500.0, map.CenterX);
        Assert.Equal(500.0, map.CenterY);
        Assert.Equal((250.0, 250.0), map.WorldToScreen(500, 500));
        Assert.Equal((0.0, 0.0), map.WorldToScreen(0, 0));
        Assert.Equal((500.0, 500.0), map.WorldToScreen(1000, 1000));

        var (wx, wy) = map.ScreenToWorld(125, 375);
        Assert.Equal(125.0, map.WorldToScreen(wx, wy).X, 6);
        Assert.Equal(375.0, map.WorldToScreen(wx, wy).Y, 6);
    }

    [Fact]
    public void Zoom_is_clamped_and_ignores_nonsense_factors()
    {
        var map = Make();

        map.ZoomBy(100);
        Assert.Equal(8.0, map.Zoom);
        map.ZoomBy(0.000001);
        Assert.Equal(0.25, map.Zoom);

        map.ZoomBy(0);
        map.ZoomBy(-3);
        map.ZoomBy(double.NaN);
        map.SetZoom(double.PositiveInfinity);
        Assert.Equal(0.25, map.Zoom);
    }

    [Fact]
    public void Zooming_around_an_anchor_keeps_the_world_point_under_it_fixed()
    {
        var map = Make();
        map.SetZoom(2);
        map.CenterOn(300, 300);
        var before = map.ScreenToWorld(100, 100);

        map.ZoomBy(2, 100, 100);

        Assert.Equal(4.0, map.Zoom);
        var after = map.ScreenToWorld(100, 100);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(262.5, map.CenterX, 6);
    }

    [Fact]
    public void Panning_is_clamped_so_the_view_never_leaves_the_world()
    {
        var map = Make();
        map.SetZoom(2);

        map.PanByWorld(-10000, 10000);

        Assert.Equal(125.0, map.CenterX);
        Assert.Equal(875.0, map.CenterY);
    }

    [Fact]
    public void Dragging_moves_the_map_with_the_pointer_so_the_centre_moves_the_other_way()
    {
        var map = Make();
        map.SetZoom(2);

        map.DragByScreen(100, -40);

        Assert.Equal(450.0, map.CenterX);
        Assert.Equal(520.0, map.CenterY);
    }

    [Fact]
    public void A_view_larger_than_the_world_stays_centred_on_that_axis()
    {
        var map = new MapModel(World, 500, 500, null, 0.05, 8);

        map.SetZoom(0.05);
        map.PanByWorld(300, 300);

        Assert.Equal(500.0, map.CenterX);
        Assert.Equal(500.0, map.CenterY);
    }

    [Fact]
    public void Resizing_the_viewport_reclamps_the_centre_and_ignores_empty_sizes()
    {
        var map = Make();
        map.SetZoom(2);
        map.PanByWorld(-10000, 0);
        Assert.Equal(125.0, map.CenterX);

        map.SetViewport(1000, 500);
        Assert.Equal(250.0, map.CenterX);

        map.SetViewport(0, 500);
        Assert.Equal(1000.0, map.ViewWidth);
    }

    [Fact]
    public void Invalid_construction_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapModel(new MapBounds(0, 0, 0, 10), 100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapModel(World, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapModel(World, 100, 100, null, 4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapModel(World, 100, 100, null, 0, 2));
    }

    [Fact]
    public void Type_filter_discovery_and_text_search_decide_which_markers_are_visible()
    {
        var map = Make();

        Assert.Equal(new[] { "m1", "m2", "m4" }, Ids(map));
        Assert.Equal(new[] { "shelter", "shop", "ruin", "landmark" }, map.Types);

        map.SetTypeVisible("shop", false);
        Assert.False(map.IsTypeVisible("shop"));
        Assert.Equal(new[] { "m1", "m4" }, Ids(map));

        map.SetSearch("tower");
        Assert.Equal(new[] { "m4" }, Ids(map));

        map.SetSearch(null);
        map.SetTypeVisible("shop", true);
        map.Upsert(new MapMarker("m3", "Old Mill", "ruin", 500, 500));
        Assert.Equal(new[] { "m1", "m2", "m3", "m4" }, Ids(map));
    }

    [Fact]
    public void Nearest_picks_the_closest_visible_marker_and_breaks_ties_by_id()
    {
        var map = Make();

        Assert.Equal("m4", map.Nearest(108, 100)?.Id);
        Assert.Equal("m1", map.Nearest(105, 100)?.Id);
        Assert.Null(map.Nearest(500, 500, 10));

        map.SetTypeVisible("landmark", false);
        Assert.Equal("m1", map.Nearest(108, 100)?.Id);
    }

    [Fact]
    public void Nearest_to_screen_uses_pixels_not_world_units()
    {
        var map = Make();

        Assert.Equal("m4", map.NearestToScreen(55, 52, 10)?.Id);
        Assert.Null(map.NearestToScreen(300, 300, 5));
        Assert.Null(map.NearestToScreen(55, 52, 1));
    }

    [Fact]
    public void Selecting_a_marker_centres_the_view_and_reveals_a_hidden_type_but_not_undiscovered_markers()
    {
        var map = Make();
        map.SetZoom(2);
        map.SetTypeVisible("shelter", false);

        Assert.True(map.Select("m1"));

        Assert.True(map.IsTypeVisible("shelter"));
        Assert.Equal("m1", map.CurrentKey);
        Assert.Equal(125.0, map.CenterX);
        Assert.Equal(125.0, map.CenterY);
        Assert.False(map.Select("m3"));
        Assert.False(map.Select("nope"));
        Assert.Equal("m1", map.SelectedId);
    }

    [Fact]
    public void Picking_a_marker_without_centring_keeps_the_view_where_it_is()
    {
        var map = Make();
        map.SetZoom(2);
        map.CenterOn(600, 600);

        Assert.True(map.SelectMarker("m1", false));

        Assert.Equal("m1", map.SelectedId);
        Assert.Equal(600.0, map.CenterX);
        Assert.Equal("Shelter 1", map.Marker("m1")?.Name);
        Assert.Null(map.Marker("nope"));
    }

    [Fact]
    public void Cycling_walks_visible_markers_by_name_and_wraps_both_ways()
    {
        var map = Make();

        Assert.True(map.Cycle(1));
        Assert.Equal("m2", map.SelectedId);
        map.Cycle(1);
        Assert.Equal("m1", map.SelectedId);
        map.Cycle(1);
        Assert.Equal("m4", map.SelectedId);
        map.Cycle(1);
        Assert.Equal("m2", map.SelectedId);
        map.Cycle(-1);
        Assert.Equal("m4", map.SelectedId);

        foreach (var type in map.Types) map.SetTypeVisible(type, false);
        Assert.False(map.Cycle(1));
    }

    [Fact]
    public void Fast_travel_is_a_plain_request_only_for_discovered_travel_enabled_markers()
    {
        var map = Make();

        Assert.Null(map.RequestFastTravel());

        map.Select("m1");
        Assert.Equal(new FastTravelRequest("m1", 100, 100), map.RequestFastTravel());

        map.Select("m2");
        Assert.Null(map.RequestFastTravel());
        Assert.Equal(new FastTravelRequest("m1", 100, 100), map.RequestFastTravel("m1"));
        Assert.Null(map.RequestFastTravel("m3"));
        Assert.Null(map.RequestFastTravel("nope"));
    }

    [Fact]
    public void Player_marker_centres_the_view_only_once_a_position_is_known()
    {
        var map = Make();
        map.SetZoom(2);

        Assert.False(map.CenterOnPlayer());
        map.SetPlayer(300, 400);

        Assert.True(map.CenterOnPlayer());
        Assert.Equal(300.0, map.CenterX);
        Assert.Equal(400.0, map.CenterY);
    }

    [Fact]
    public void Global_search_lists_only_discovered_markers_and_removing_the_selection_clears_it()
    {
        var map = Make();

        var tower = Assert.Single(map.Search(ListSearch.Tokenize("tower")));
        Assert.Equal(new PipBoySearchHit(PipBoyTab.Map, "m4", "Water Tower", "landmark"), tower);
        Assert.Empty(map.Search(ListSearch.Tokenize("mill")));

        map.Select("m4");
        Assert.True(map.Remove("m4"));
        Assert.Null(map.SelectedId);
        Assert.False(map.Remove("m4"));
    }

    static FakeRecordReader WorldFeed(int worldFlags = 0)
    {
        var records = new FakeRecordReader();
        var world = records.Add("000800:W.esm", "WRLD");
        world.Scalars[WorldMapFeed.NwCellXPath] = "-2";
        world.Scalars[WorldMapFeed.NwCellYPath] = "3";
        world.Scalars[WorldMapFeed.SeCellXPath] = "5";
        world.Scalars[WorldMapFeed.SeCellYPath] = "-4";
        world.Scalars[WorldMapFeed.WorldFlagsPath] = worldFlags.ToString();
        Marker(records, world, "000810:W.esm", "Sanctuary", 100, 200, 3);
        Marker(records, world, "000811:W.esm", "Hidden Cave", 300, -400, 2);
        Marker(records, world, "000812:W.esm", "Lookout", 500, 600, 1);
        return records;
    }

    static void Marker(FakeRecordReader records, FakeRecord world, string id, string name, double x, double y, int flags, string? editorId = null)
    {
        var marker = records.Add(id, "REFR", editorId, name);
        marker.Scalars[WorldMapFeed.PositionXPath] = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        marker.Scalars[WorldMapFeed.PositionYPath] = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        marker.Scalars[WorldMapFeed.MarkerFlagsPath] = flags.ToString();
        world.MapMarkers.Add(id);
    }

    // Regression: the map rectangle must run from the NW cell's west edge to the SE cell's east edge, with Y negated, or every marker draws mirrored or one cell off.
    [Fact]
    public void World_map_bounds_come_from_the_cell_rectangle_with_y_negated()
    {
        var data = WorldMapFeed.Read(WorldFeed(), "000800:W.esm")!;

        Assert.Equal(new MapBounds(-2 * 4096, -4 * 4096, 6 * 4096, 4 * 4096), data.Bounds);
    }

    // Regression: Visible and Can Travel To are separate bits, and a marker's FO4 Y must reach the map as negated Y.
    [Fact]
    public void World_markers_carry_name_negated_y_and_flag_bits()
    {
        var map = WorldMapFeed.Read(WorldFeed(), "000800:W.esm")!.NewModel();
        map.ShowUndiscovered = true;
        var byName = map.VisibleMarkers.ToDictionary(m => m.Name);

        Assert.Equal(3, byName.Count);
        Assert.Equal((100.0, -200.0), (byName["Sanctuary"].X, byName["Sanctuary"].Y));
        Assert.True(byName["Sanctuary"].Discovered && byName["Sanctuary"].CanFastTravel);
        Assert.True(!byName["Hidden Cave"].Discovered && byName["Hidden Cave"].CanFastTravel);
        Assert.True(byName["Lookout"].Discovered && !byName["Lookout"].CanFastTravel);
        Assert.Equal(new[] { "Lookout", "Sanctuary" }, WorldMapFeed.Read(WorldFeed(), "000800:W.esm")!.NewModel().VisibleMarkers.Select(m => m.Name).OrderBy(n => n));
    }

    // Regression: a worldspace that cannot fast travel must not offer travel on markers that carry Can Travel To.
    [Fact]
    public void A_world_that_cannot_fast_travel_clears_every_marker_travel_flag()
    {
        var data = WorldMapFeed.Read(WorldFeed(WorldMapFeed.CantFastTravelWorld), "000800:W.esm")!;

        Assert.All(data.Markers, m => Assert.False(m.CanFastTravel));
    }

    // Regression: an unknown worldspace, a missing cell rectangle or an inverted one must give no map, not a throw from MapModel.
    [Fact]
    public void A_worldspace_without_a_usable_cell_rectangle_gives_no_map()
    {
        var records = WorldFeed();

        Assert.Null(WorldMapFeed.Read(records, "009999:W.esm"));
        records.Records["000800:W.esm"].Scalars[WorldMapFeed.SeCellXPath] = "-9";
        Assert.Null(WorldMapFeed.Read(records, "000800:W.esm"));
        records.Records["000800:W.esm"].Scalars[WorldMapFeed.SeCellXPath] = "5";
        records.Records["000800:W.esm"].Scalars.Remove(WorldMapFeed.NwCellYPath);
        Assert.Null(WorldMapFeed.Read(records, "000800:W.esm"));
    }

    // Regression: a world with a rectangle but no markers must still give a map, and unplaced markers must be dropped.
    [Fact]
    public void Empty_world_gives_an_empty_map_and_unplaced_markers_are_dropped()
    {
        var records = WorldFeed();
        var world = records.Records["000800:W.esm"];
        world.MapMarkers.Clear();
        Assert.Empty(WorldMapFeed.Read(records, "000800:W.esm")!.Markers);

        Marker(records, world, "000821:W.esm", "No Position", 1, 1, 3);
        records.Records["000821:W.esm"].Scalars.Remove(WorldMapFeed.PositionYPath);

        Assert.Empty(WorldMapFeed.Read(records, "000800:W.esm")!.Markers);
    }

    // Regression: a marker whose FULL name does not resolve must stay on the map under its editor id or a fixed label, not vanish.
    [Fact]
    public void A_marker_without_a_resolvable_name_is_kept_under_a_fallback_name()
    {
        var records = WorldFeed();
        var world = records.Records["000800:W.esm"];
        world.MapMarkers.Clear();
        Marker(records, world, "000820:W.esm", "", 1, 1, 3);
        Marker(records, world, "000821:W.esm", "", 2, 2, 3, "UseLocationMarker");

        var names = WorldMapFeed.Read(records, "000800:W.esm")!.Markers.ToDictionary(m => m.Id, m => m.Name);

        Assert.Equal(WorldMapFeed.UnnamedMarker, names["000820:W.esm"]);
        Assert.Equal("UseLocationMarker", names["000821:W.esm"]);
    }

    // Regression: the whole-world map must open fitted to the view, not clipped to the default zoom floor.
    [Fact]
    public void World_map_opens_fitted_to_the_whole_rectangle()
    {
        var map = WorldMapFeed.Read(WorldFeed(), "000800:W.esm")!.NewModel();

        Assert.Equal(Math.Min(WorldMapData.ViewWidth / map.Bounds.Width, WorldMapData.ViewHeight / map.Bounds.Height), map.Zoom, 12);
        Assert.True(map.MinZoom < map.Zoom);
    }

    // Regression: with no travel executor the production rule must refuse a good target with a reason; with one it may allow only discovered travel markers.
    [Theory]
    [InlineData(false, true, true, false, MapTravelRules.NoExecutorReason)]
    [InlineData(false, true, false, false, null)]
    [InlineData(true, true, true, true, null)]
    [InlineData(true, false, true, false, null)]
    [InlineData(true, true, false, false, null)]
    public void Travel_rules_allow_only_discovered_fast_travel_markers_and_only_with_an_executor(bool executor, bool discovered, bool canTravel, bool allowed, string? reason)
    {
        var verdict = new MapTravelRules(executor).CanFastTravel(new MapMarker("m", "M", "location", 0, 0, discovered, canTravel));

        Assert.Equal(allowed, verdict.Allowed);
        Assert.Equal(reason, verdict.Reason);
    }

    // Regression: the flow must show the refusal reason and offer no travel when the production rules have no executor.
    [Fact]
    public void Production_rules_make_a_discovered_travel_marker_refuse_in_the_flow()
    {
        var map = WorldMapFeed.Read(WorldFeed(), "000800:W.esm")!.NewModel();
        var flow = new MapTravelFlow(new MapTravelRules(false));
        flow.SetSources(new MapSource(map, "world"), null);
        flow.Pick("000810:W.esm");

        flow.Accept();

        Assert.True(flow.IsModalOpen);
        Assert.Contains(MapTravelRules.NoExecutorReason, flow.Choice!.Prompt);
        Assert.False(flow.VerdictFor(map.Marker("000810:W.esm")!).Allowed);
    }
}
