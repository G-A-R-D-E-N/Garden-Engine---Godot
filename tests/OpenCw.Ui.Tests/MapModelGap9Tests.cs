using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MapModelGap9Tests
{
    static MapModel Make(params MapMarker[] markers) =>
        new(new MapBounds(0, 0, 1000, 1000), 800, 500, markers, 0.75, 5.0);

    static MapModel WorldModel() => new(new MapBounds(0, 0, 1000, 1000), 500, 500, new[]
    {
        new MapMarker("far", "Far Town", "location", 800, 800, CanFastTravel: true),
        new MapMarker("fog", "Hidden Ruin", "location", 500, 500, Discovered: false, CanFastTravel: true),
    }, 0.25, 8);

    static (MapTravelFlow Flow, List<string> Sounds, List<FastTravelRequest> Travels) MakeFlow()
    {
        var flow = new MapTravelFlow();
        var sounds = new List<string>();
        var travels = new List<FastTravelRequest>();
        flow.SoundRequested += sounds.Add;
        flow.FastTravelRequested += travels.Add;
        flow.SetSources(new MapSource(WorldModel(), "world"), null);
        return (flow, sounds, travels);
    }

    [Fact]
    public void Stepping_the_zoom_moves_t_by_the_delta_and_clamps_at_both_ends()
    {
        var map = Make();
        Assert.Equal(0.0, map.ZoomT, 12);

        map.StepZoomT(0.5);
        Assert.Equal(0.5, map.ZoomT, 12);
        Assert.Equal(2.875, map.Zoom, 12);

        map.StepZoomT(0.25);
        Assert.Equal(0.75, map.ZoomT, 12);
        Assert.Equal(3.9375, map.Zoom, 12);

        map.StepZoomT(-1.5);
        Assert.Equal(0.0, map.ZoomT, 12);
        Assert.Equal(0.75, map.Zoom, 12);

        map.StepZoomT(7);
        Assert.Equal(1.0, map.ZoomT, 12);
        Assert.Equal(5.0, map.Zoom, 12);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Stepping_by_a_non_finite_delta_changes_nothing(double delta)
    {
        var map = Make();
        map.StepZoomT(0.5);

        map.StepZoomT(delta);

        Assert.Equal(2.875, map.Zoom, 12);
        Assert.Equal((500.0, 500.0), (map.CenterX, map.CenterY));
    }

    [Fact]
    public void Stepping_around_an_anchor_keeps_the_world_point_under_it()
    {
        var map = Make();

        map.StepZoomT(0.5, 200, 120);

        Assert.Equal(2.875, map.Zoom, 12);
        Assert.Equal(700.0 / 3 + 200 / 2.875, map.CenterX, 9);
        Assert.Equal(980.0 / 3 + 130 / 2.875, map.CenterY, 9);
        var (wx, wy) = map.ScreenToWorld(200, 120);
        Assert.Equal(700.0 / 3, wx, 9);
        Assert.Equal(980.0 / 3, wy, 9);
    }

    [Fact]
    public void Stepping_a_map_with_a_single_zoom_level_keeps_that_level()
    {
        var map = new MapModel(new MapBounds(0, 0, 1000, 1000), 800, 500, null, 2.0, 2.0);

        map.StepZoomT(0.5);

        Assert.Equal(0.0, map.ZoomT, 12);
        Assert.Equal(2.0, map.Zoom, 12);
    }

    [Fact]
    public void Clearing_the_selection_unselects_without_moving_the_camera_or_removing_the_marker()
    {
        var map = Make(new MapMarker("a", "Alpha", "location", 100, 200, CanFastTravel: true),
                       new MapMarker("b", "Beta", "location", 700, 800));
        Assert.True(map.Select("a"));
        Assert.Equal("a", map.SelectedId);
        var centre = (map.CenterX, map.CenterY);

        map.ClearSelection();

        Assert.Null(map.SelectedId);
        Assert.Null(map.CurrentKey);
        Assert.Equal(centre, (map.CenterX, map.CenterY));
        Assert.Equal(new[] { "a", "b" }, map.VisibleMarkers.Select(m => m.Id).ToArray());
        Assert.Null(map.RequestFastTravel());
        Assert.Equal(new FastTravelRequest("a", 100, 200), map.RequestFastTravel("a"));
    }

    [Fact]
    public void Cycling_after_clearing_starts_from_the_ends_again()
    {
        var map = Make(new MapMarker("a", "Alpha", "location", 100, 200), new MapMarker("b", "Beta", "location", 700, 800));
        Assert.True(map.Select("a"));
        map.ClearSelection();

        Assert.True(map.Cycle(-1));
        Assert.Equal("b", map.SelectedId);

        map.ClearSelection();
        Assert.True(map.Cycle(1));
        Assert.Equal("a", map.SelectedId);
    }

    [Fact]
    public void The_player_position_starts_unknown_and_centring_on_it_does_nothing()
    {
        var map = Make();

        Assert.Null(map.PlayerX);
        Assert.Null(map.PlayerY);
        Assert.False(map.CenterOnPlayer());
        Assert.Equal((500.0, 500.0), (map.CenterX, map.CenterY));
    }

    [Fact]
    public void Setting_the_player_records_both_axes_without_moving_the_camera_and_the_latest_wins()
    {
        var map = Make();
        map.SetZoomT(1);

        map.SetPlayer(250, 750);

        Assert.Equal(250.0, map.PlayerX);
        Assert.Equal(750.0, map.PlayerY);
        Assert.Equal((500.0, 500.0), (map.CenterX, map.CenterY));

        map.SetPlayer(10.5, 20.25);

        Assert.Equal(10.5, map.PlayerX);
        Assert.Equal(20.25, map.PlayerY);
    }

    [Fact]
    public void Centring_on_the_player_moves_the_camera_there_within_the_clamped_range()
    {
        var map = Make();
        map.SetZoomT(1);
        map.SetPlayer(250, 750);

        Assert.True(map.CenterOnPlayer());
        Assert.Equal((250.0, 750.0), (map.CenterX, map.CenterY));

        map.SetPlayer(10, 990);

        Assert.True(map.CenterOnPlayer());
        Assert.Equal((80.0, 950.0), (map.CenterX, map.CenterY));
        Assert.Equal(10.0, map.PlayerX);
        Assert.Equal(990.0, map.PlayerY);
    }

    [Fact]
    public void An_undiscovered_travel_offer_carries_its_prompt_choices_cancel_index_target_and_generation()
    {
        var (flow, _, _) = MakeFlow();
        flow.Map!.ShowUndiscovered = true;
        Assert.True(flow.Pick("fog"));

        flow.Accept();

        var choice = flow.Choice!;
        Assert.Equal(MapTravelFlow.UndiscoveredPrompt, choice.Prompt);
        Assert.Equal("Cannot travel to an undiscovered location. Place a marker here?", choice.Prompt);
        Assert.Null(choice.Reason);
        Assert.Equal(new[]
        {
            new MapChoiceOption("YES", MapChoiceAction.PlaceMarker),
            new MapChoiceOption("NO", MapChoiceAction.Decline),
        }, choice.Options.ToArray());
        Assert.Equal(1, choice.CancelIndex);
        Assert.Equal(("fog", 500.0, 500.0, 0), (choice.MarkerId, choice.X, choice.Y, choice.Generation));
    }

    [Fact]
    public void Accepting_with_a_marker_already_set_offers_move_remove_or_cancel_at_the_cursor()
    {
        var (flow, sounds, _) = MakeFlow();
        flow.Accept((300, 400));
        Assert.Equal(new MapWaypoint("world", 300, 400), flow.Waypoint);
        Assert.Null(flow.Choice);

        flow.Accept((600, 700));

        var choice = flow.Choice!;
        Assert.Equal("Move the marker?", choice.Prompt);
        Assert.Null(choice.Reason);
        Assert.Equal(new[]
        {
            new MapChoiceOption("MOVE MARKER", MapChoiceAction.MoveMarker),
            new MapChoiceOption("REMOVE MARKER", MapChoiceAction.RemoveMarker),
            new MapChoiceOption("CANCEL", MapChoiceAction.Decline),
        }, choice.Options.ToArray());
        Assert.Equal(2, choice.CancelIndex);
        Assert.Equal((null, 600.0, 700.0, 1), (choice.MarkerId, choice.X, choice.Y, choice.Generation));
        Assert.Equal(0, flow.ChoiceIndex);

        sounds.Clear();
        Assert.True(flow.Choose(0));

        Assert.Equal(new MapWaypoint("world", 600, 700), flow.Waypoint);
        Assert.Equal(new[] { "Confirm" }, sounds.ToArray());
        Assert.Null(flow.Choice);
    }

    [Fact]
    public void Choosing_remove_clears_the_marker_and_back_on_the_offer_picks_cancel_and_keeps_it()
    {
        var (flow, sounds, _) = MakeFlow();
        flow.Accept((300, 400));
        flow.Accept((600, 700));

        Assert.True(flow.Handle(MenuInputAction.Back));

        Assert.Equal(new MapWaypoint("world", 300, 400), flow.Waypoint);
        Assert.Equal("Cancel", sounds.Last());

        flow.Accept((600, 700));
        sounds.Clear();
        Assert.True(flow.Choose(1));

        Assert.Null(flow.Waypoint);
        Assert.Equal(new[] { "Confirm" }, sounds.ToArray());
    }

    [Fact]
    public void An_offer_made_before_the_marker_changed_externally_is_dropped_without_effect()
    {
        var (flow, sounds, travels) = MakeFlow();
        Assert.True(flow.Pick("far"));
        flow.Accept();
        Assert.Equal(0, flow.Choice!.Generation);
        flow.SetWaypointExternal(new MapWaypoint("world", 1, 2));
        sounds.Clear();

        Assert.False(flow.Choose(0));

        Assert.Empty(travels);
        Assert.Empty(sounds);
        Assert.Null(flow.Choice);
        Assert.Equal(new MapWaypoint("world", 1, 2), flow.Waypoint);
    }
}
