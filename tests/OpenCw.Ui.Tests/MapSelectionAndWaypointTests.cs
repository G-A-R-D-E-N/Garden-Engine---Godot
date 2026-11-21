using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MapSelectionAndWaypointTests
{
    static readonly MapBounds World = new(0, 0, 1000, 1000);

    static MapModel WorldModel() => new(World, 500, 500, new[]
    {
        new MapMarker("a", "Alpha", "shop", 800, 800, CanFastTravel: true),
        new MapMarker("b", "Beta", "ruin", 200, 200, CanFastTravel: true),
    }, 0.25, 8);

    static MapModel LocalModel() => new(new MapBounds(200, 200, 600, 600), 400, 400, new[]
    {
        new MapMarker("b", "Beta", "ruin", 200, 200, CanFastTravel: true),
    }, 0.5, 4);

    static MapTravelFlow Live(MapModel? local = null)
    {
        var flow = new MapTravelFlow();
        flow.SetLiveSources(WorldModel(), local);
        return flow;
    }

    [Fact]
    public void Hiding_the_type_of_the_selected_marker_drops_the_selection_so_accept_acts_on_the_map_centre()
    {
        var flow = Live();
        Assert.True(flow.Pick("a"));

        flow.Map!.SetTypeVisible("shop", false);
        Assert.Null(flow.Map.SelectedId);
        flow.Accept();

        Assert.Null(flow.Choice);
        Assert.Equal(new MapWaypoint(MapTravelFlow.LiveContext, 500, 500), flow.Waypoint);
    }

    [Fact]
    public void Hiding_another_type_or_showing_the_type_again_keeps_the_selection()
    {
        var flow = Live();
        flow.Pick("a");

        flow.Map!.SetTypeVisible("ruin", false);
        Assert.Equal("a", flow.Map.SelectedId);
        flow.Map.SetTypeVisible("ruin", true);
        Assert.Equal("a", flow.Map.SelectedId);
    }

    [Fact]
    public void A_search_that_hides_the_selected_marker_drops_the_selection()
    {
        var map = WorldModel();
        map.SelectMarker("a", false);

        map.SetSearch("beta");

        Assert.Null(map.SelectedId);
        map.SelectMarker("b", false);
        map.SetSearch("beta");
        Assert.Equal("b", map.SelectedId);
    }

    [Fact]
    public void Finding_the_player_reselects_the_marker_at_the_new_centre_instead_of_keeping_the_old_one()
    {
        var flow = Live();
        flow.Map!.SetZoom(2);
        flow.Map.SetPlayer(800, 800);
        Assert.True(flow.Pick("b"));

        Assert.True(flow.FindPlayer());

        Assert.Equal("a", flow.Map.SelectedId);
        flow.Accept();
        Assert.Equal("Travel to Alpha?", flow.Choice!.Prompt);
    }

    [Fact]
    public void Finding_the_player_away_from_every_marker_clears_the_old_selection_and_without_a_player_does_nothing()
    {
        var flow = Live();
        flow.Map!.SetZoom(2);
        Assert.True(flow.Pick("b"));
        Assert.False(flow.FindPlayer());
        Assert.Equal("b", flow.Map.SelectedId);

        flow.Map.SetPlayer(500, 500);
        Assert.True(flow.FindPlayer());

        Assert.Null(flow.Map.SelectedId);
        flow.Accept();
        Assert.Equal(new MapWaypoint(MapTravelFlow.LiveContext, 500, 500), flow.Waypoint);
    }

    [Fact]
    public void Finding_the_player_is_refused_while_a_choice_is_open()
    {
        var flow = Live();
        flow.Map!.SetPlayer(500, 500);
        flow.Accept((300, 300));
        flow.Accept((400, 400));
        Assert.NotNull(flow.Choice);

        Assert.False(flow.FindPlayer());
    }

    [Fact]
    public void A_waypoint_placed_in_the_world_view_stays_current_in_the_local_view_and_back()
    {
        var flow = Live(LocalModel());
        Assert.Equal(MapView.World, flow.View);
        flow.Accept((300, 400));
        var placed = flow.Waypoint!;
        Assert.Equal(flow.ContextId, placed.ContextId);

        Assert.True(flow.Switch());
        Assert.Equal(MapView.Local, flow.View);
        Assert.Equal(placed.ContextId, flow.ContextId);
        flow.Accept((350, 450));
        Assert.Equal("Move the marker?", flow.Choice!.Prompt);
        flow.Choose(2);

        Assert.True(flow.Switch());
        Assert.Equal(placed.ContextId, flow.ContextId);
        Assert.Equal(placed, flow.Waypoint);
    }

    [Fact]
    public void A_waypoint_restored_from_an_older_save_with_a_local_context_belongs_to_the_shared_context()
    {
        var flow = Live(LocalModel());

        flow.SetWaypointExternal(new MapWaypoint(MapTravelFlow.LegacyLocalContext, 300, 400));

        Assert.Equal(new MapWaypoint(MapTravelFlow.LiveContext, 300, 400), flow.Waypoint);
        Assert.Equal(flow.ContextId, flow.Waypoint!.ContextId);
        flow.Switch();
        Assert.Equal(flow.ContextId, flow.Waypoint.ContextId);
    }
}
