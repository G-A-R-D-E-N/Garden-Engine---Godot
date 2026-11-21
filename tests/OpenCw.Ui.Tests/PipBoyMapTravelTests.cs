using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyMapTravelTests
{
    static readonly MapBounds World = new(0, 0, 1000, 1000);

    sealed class Rules : IMapTravelRules
    {
        public Func<MapMarker, TravelVerdict> Decide { get; set; } = _ => TravelVerdict.Yes;

        public TravelVerdict CanFastTravel(MapMarker marker) => Decide(marker);
    }

    sealed class Rig
    {
        public MapTravelFlow Flow = null!;
        public List<FastTravelRequest> Travels = new();
        public List<MapWaypoint?> Waypoints = new();
        public List<string> Sounds = new();
        public List<string> Instructions = new();
    }

    static MapModel WorldModel(bool undiscovered = false) => new(World, 500, 500, new[]
    {
        new MapMarker("far", "Far Town", "location", 800, 800, CanFastTravel: true),
        new MapMarker("blocked", "Blocked Camp", "location", 200, 800, CanFastTravel: false),
        new MapMarker("fog", "Hidden Ruin", "location", 500, 500, Discovered: false, CanFastTravel: true),
        new MapMarker("door", "Door", "door", 100, 100, CanFastTravel: true),
        new MapMarker("way", "Marker", "waypoint", 900, 100),
    }, 0.25, 8) { ShowUndiscovered = undiscovered };

    static MapModel LocalModel() => new(new MapBounds(0, 0, 100, 100), 100, 100, new[]
    {
        new MapMarker("loc1", "Cache", "location", 20, 30),
    }, 0.75, 2.0);

    static Rig Make(IMapTravelRules? rules = null, bool undiscovered = false, bool world = true, bool local = false, string? identity = null)
    {
        var rig = new Rig { Flow = new MapTravelFlow(rules) };
        rig.Flow.FastTravelRequested += rig.Travels.Add;
        rig.Flow.WaypointChanged += rig.Waypoints.Add;
        rig.Flow.SoundRequested += rig.Sounds.Add;
        rig.Flow.InstructionChanged += rig.Instructions.Add;
        rig.Flow.SetSources(world ? new MapSource(WorldModel(undiscovered), "world") : null,
                            local ? new MapSource(LocalModel(), "local") : null, identity);
        return rig;
    }

    [Fact]
    public void Travel_choice_yes_raises_exactly_one_request_and_a_second_choose_does_nothing()
    {
        var rig = Make();
        rig.Flow.Pick("far");

        rig.Flow.Accept();

        Assert.Equal("Travel to Far Town?", rig.Flow.Choice!.Prompt);
        Assert.Equal(new[] { "YES", "NO", "PLACE MARKER" }, rig.Flow.Choice.Options.Select(o => o.Label));
        Assert.True(rig.Flow.Handle(MenuInputAction.Accept));
        Assert.Equal(new[] { new FastTravelRequest("far", 800, 800) }, rig.Travels);
        Assert.Contains("Confirm", rig.Sounds);
        Assert.Null(rig.Flow.Choice);
        Assert.False(rig.Flow.Choose(0));
        Assert.Single(rig.Travels);
        Assert.Empty(rig.Waypoints);
    }

    [Fact]
    public void Travel_choice_no_and_back_do_not_travel_and_place_marker_sets_the_waypoint_at_the_marker()
    {
        var rig = Make();
        rig.Flow.Pick("far");

        rig.Flow.Accept();
        rig.Flow.Handle(MenuInputAction.Down);
        rig.Flow.Handle(MenuInputAction.Accept);
        Assert.Empty(rig.Travels);
        Assert.Contains("Cancel", rig.Sounds);

        rig.Flow.Accept();
        Assert.True(rig.Flow.Back());
        Assert.Empty(rig.Travels);
        Assert.Empty(rig.Waypoints);
        Assert.False(rig.Flow.Back());

        rig.Flow.Accept();
        rig.Flow.Handle(MenuInputAction.Down);
        rig.Flow.Handle(MenuInputAction.Down);
        rig.Flow.Handle(MenuInputAction.Down);
        rig.Flow.Handle(MenuInputAction.Accept);
        Assert.Empty(rig.Travels);
        Assert.Equal(new MapWaypoint("world", 800, 800), Assert.Single(rig.Waypoints));
        Assert.Equal(new MapWaypoint("world", 800, 800), rig.Flow.Waypoint);
    }

    [Fact]
    public void An_undiscovered_marker_offers_to_place_a_marker_instead_of_travelling()
    {
        var rig = Make(undiscovered: true);

        Assert.True(rig.Flow.Pick("fog"));
        rig.Flow.Accept();

        Assert.Equal(MapTravelFlow.UndiscoveredPrompt, rig.Flow.Choice!.Prompt);
        Assert.Equal(new[] { "YES", "NO" }, rig.Flow.Choice.Options.Select(o => o.Label));
        rig.Flow.Handle(MenuInputAction.Accept);
        Assert.Empty(rig.Travels);
        Assert.Equal(new MapWaypoint("world", 500, 500), rig.Flow.Waypoint);
    }

    [Fact]
    public void A_host_travel_rule_refusal_shows_only_its_reason_with_the_marker_question_and_never_travels()
    {
        var rules = new Rules { Decide = _ => TravelVerdict.No("Too heavy to travel") };
        var rig = Make(rules);
        rig.Flow.Pick("far");

        rig.Flow.Accept();

        Assert.Equal("Too heavy to travel. " + MapTravelFlow.PlaceMarkerQuestion, rig.Flow.Choice!.Prompt);
        Assert.Null(rig.Flow.Choice.Reason);
        rig.Flow.Handle(MenuInputAction.Down);
        rig.Flow.Handle(MenuInputAction.Accept);
        Assert.Empty(rig.Travels);
        Assert.Empty(rig.Waypoints);
        Assert.False(rig.Flow.VerdictFor(WorldModel().Marker("far")!).Allowed);
    }

    [Fact]
    public void Accept_on_empty_map_places_then_moves_or_removes_the_waypoint()
    {
        var rig = Make();

        rig.Flow.Accept((300.0, 400.0));
        Assert.Equal(new MapWaypoint("world", 300, 400), rig.Flow.Waypoint);
        Assert.Null(rig.Flow.Choice);

        rig.Flow.Accept((10.0, 20.0));
        Assert.Equal("Move the marker?", rig.Flow.Choice!.Prompt);
        Assert.Equal(new[] { "MOVE MARKER", "REMOVE MARKER", "CANCEL" }, rig.Flow.Choice.Options.Select(o => o.Label));
        rig.Flow.Choose(0);
        Assert.Equal(new MapWaypoint("world", 10, 20), rig.Flow.Waypoint);

        rig.Flow.Accept((70.0, 80.0));
        Assert.True(rig.Flow.Back());
        Assert.Equal(new MapWaypoint("world", 10, 20), rig.Flow.Waypoint);
        Assert.Equal(2, rig.Waypoints.Count);

        rig.Flow.Accept((70.0, 80.0));
        rig.Flow.Choose(1);
        Assert.Null(rig.Flow.Waypoint);
        Assert.Null(rig.Waypoints.Last());
    }

    [Fact]
    public void The_waypoint_door_and_non_travel_markers_fall_through_to_placing_at_the_cursor()
    {
        var rig = Make();

        rig.Flow.Pick("way");
        rig.Flow.Accept((50.0, 60.0));
        Assert.Equal(new MapWaypoint("world", 50, 60), rig.Flow.Waypoint);

        rig.Flow.SetWaypointExternal(null);
        rig.Flow.Pick("door");
        rig.Flow.Accept((11.0, 12.0));
        Assert.Null(rig.Flow.Choice);
        Assert.Equal(new MapWaypoint("world", 11, 12), rig.Flow.Waypoint);

        rig.Flow.SetWaypointExternal(null);
        rig.Flow.Pick("blocked");
        rig.Flow.Accept((13.0, 14.0));
        Assert.Empty(rig.Travels);
        Assert.Equal(new MapWaypoint("world", 13, 14), rig.Flow.Waypoint);
        Assert.True(MapMarkerKinds.IsNeverTravelTarget("QUEST_DOOR"));
        Assert.True(MapMarkerKinds.IsNeverTravelTarget("Player"));
    }

    [Fact]
    public void Local_view_never_fast_travels_and_only_places_markers_inside_its_bounds()
    {
        var rig = Make(world: false, local: true);
        rig.Flow.Map!.SelectMarker("loc1", false);

        rig.Flow.Accept((40.0, 50.0));
        Assert.Empty(rig.Travels);
        Assert.Equal(new MapWaypoint("local", 40, 50), rig.Flow.Waypoint);

        var outside = Make(world: false, local: true);
        outside.Flow.Accept((500.0, 500.0));
        Assert.Null(outside.Flow.Waypoint);
        Assert.Empty(outside.Waypoints);
    }

    [Fact]
    public void An_open_choice_blocks_selection_and_clamps_its_cursor()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        Assert.True(rig.Flow.IsModalOpen);
        Assert.False(rig.Flow.Pick("blocked"));
        rig.Flow.UpdateSelection(5, 5);
        Assert.Equal("far", rig.Flow.Map!.SelectedId);
        rig.Flow.Handle(MenuInputAction.Up);
        Assert.Equal(0, rig.Flow.ChoiceIndex);
        for (var i = 0; i < 6; i++) rig.Flow.Handle(MenuInputAction.Down);
        Assert.Equal(2, rig.Flow.ChoiceIndex);
        Assert.True(rig.Flow.Handle(MenuInputAction.Back));
        Assert.False(rig.Flow.IsModalOpen);
        Assert.Empty(rig.Travels);
    }

    [Fact]
    public void A_choice_that_became_stale_does_nothing_and_closes()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        rig.Flow.SetWaypointExternal(new MapWaypoint("world", 1, 1));

        Assert.False(rig.Flow.Choose(0));
        Assert.Null(rig.Flow.Choice);
        Assert.Empty(rig.Travels);
    }

    [Fact]
    public void Rebinding_or_replacing_the_source_closes_the_choice_without_choosing()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();
        rig.Flow.Rebind();
        Assert.Null(rig.Flow.Choice);
        Assert.Equal("far", rig.Flow.Map!.SelectedId);

        rig.Flow.Pick("far");
        rig.Flow.Accept();
        rig.Flow.SetSources(new MapSource(WorldModel(), "world"), null);
        Assert.Null(rig.Flow.Choice);
        Assert.Empty(rig.Travels);
        Assert.Empty(rig.Waypoints);
    }

    [Fact]
    public void Default_view_is_world_when_a_world_source_exists_and_local_otherwise()
    {
        Assert.Equal(MapView.World, Make(local: true).Flow.View);
        Assert.Equal(MapView.World, Make().Flow.View);

        var interior = Make(world: false, local: true);
        Assert.Equal(MapView.Local, interior.Flow.View);
        Assert.False(interior.Flow.CanSwitch);
        Assert.False(interior.Flow.Switch());
        Assert.Null(interior.Flow.UnavailableText);

        var none = Make(world: false);
        Assert.Equal("LOCAL MAP UNAVAILABLE", none.Flow.UnavailableText);
    }

    [Fact]
    public void Losing_the_selected_source_shows_the_unavailable_text_and_refuses_actions()
    {
        var rig = Make(local: true);
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        rig.Flow.SetSources(null, new MapSource(LocalModel(), "local"));

        Assert.Null(rig.Flow.Choice);
        Assert.Equal("WORLD MAP UNAVAILABLE", rig.Flow.UnavailableText);
        Assert.False(rig.Flow.SourceAvailable);
        Assert.False(rig.Flow.ToggleLegend());
        rig.Flow.Accept((5.0, 5.0));
        Assert.Empty(rig.Travels);
        Assert.Empty(rig.Waypoints);
        Assert.Equal("R LOCAL MAP", rig.Flow.InstructionText());

        var views = new List<MapView>();
        rig.Flow.ViewChanged += views.Add;
        Assert.True(rig.Flow.Switch());
        Assert.Equal(new[] { MapView.Local }, views);
        Assert.Null(rig.Flow.UnavailableText);
    }

    [Fact]
    public void An_explicit_local_choice_survives_source_restoration_but_not_a_new_world_identity()
    {
        var rig = Make(local: true, identity: "W1");
        rig.Flow.Switch();
        Assert.Equal(MapView.Local, rig.Flow.View);

        rig.Flow.SetSources(new MapSource(WorldModel(), "world"), new MapSource(LocalModel(), "local"), "W1");
        Assert.Equal(MapView.Local, rig.Flow.View);

        rig.Flow.SetSources(new MapSource(WorldModel(), "world"), new MapSource(LocalModel(), "local"), "W2");
        Assert.Equal(MapView.World, rig.Flow.View);
    }

    [Fact]
    public void Focus_marker_switches_to_the_view_that_has_it_selects_it_and_zooms_to_the_middle()
    {
        var rig = Make(local: true);
        var views = new List<MapView>();
        rig.Flow.ViewChanged += views.Add;

        Assert.True(rig.Flow.FocusMarker("loc1"));

        Assert.Equal(MapView.Local, rig.Flow.View);
        Assert.Equal(new[] { MapView.Local }, views);
        Assert.Equal("loc1", rig.Flow.Map!.SelectedId);
        Assert.Equal(1.375, rig.Flow.Map.Zoom, 6);
        Assert.Equal(string.Empty, rig.Flow.Notice);
    }

    [Fact]
    public void Focusing_an_unknown_marker_changes_nothing_and_shows_the_notice_for_two_seconds()
    {
        var rig = Make(local: true);
        rig.Flow.Pick("far");

        Assert.False(rig.Flow.FocusMarker("nope"));
        Assert.Equal(MapView.World, rig.Flow.View);
        Assert.Equal("far", rig.Flow.Map!.SelectedId);
        Assert.Equal("MARKER NOT FOUND", rig.Flow.Notice);

        rig.Flow.Tick(1.9);
        Assert.Equal("MARKER NOT FOUND", rig.Flow.Notice);
        rig.Flow.Tick(0.2);
        Assert.Equal(string.Empty, rig.Flow.Notice);
        Assert.False(rig.Flow.FocusMarker("fog"));
    }

    [Fact]
    public void Instruction_text_lists_only_the_valid_actions_and_announces_changes()
    {
        var rig = Make(local: true);
        Assert.Equal("ENTER PLACE MARKER   R LOCAL MAP   L SHOW LEGEND", rig.Flow.InstructionText());

        rig.Flow.Pick("far");
        Assert.Equal("ENTER FAST TRAVEL   R LOCAL MAP   L SHOW LEGEND", rig.Flow.InstructionText());
        Assert.Contains("ENTER FAST TRAVEL   R LOCAL MAP   L SHOW LEGEND", rig.Instructions);

        rig.Flow.ToggleLegend();
        Assert.Equal("ENTER FAST TRAVEL   R LOCAL MAP   L HIDE LEGEND", rig.Flow.InstructionText(a => a == MapAction.Legend ? "L" : a == MapAction.SwitchView ? "R" : "ENTER"));

        rig.Flow.Pick("blocked");
        Assert.DoesNotContain(MapAction.FastTravel, rig.Flow.Actions());
        Assert.Contains(MapAction.PlaceMarker, rig.Flow.Actions());

        rig.Flow.Pick("far");
        rig.Flow.Accept();
        Assert.Equal(string.Empty, rig.Flow.InstructionText());
    }

    [Fact]
    public void Selection_follows_the_nearest_marker_within_fifty_pixels_and_rolls_over_once()
    {
        var rig = Make();

        rig.Flow.UpdateSelection(400, 400);
        Assert.Equal("far", rig.Flow.Map!.SelectedId);
        rig.Flow.UpdateSelection(405, 402);
        Assert.Equal(1, rig.Sounds.Count(s => s == "MapRollover"));

        rig.Flow.UpdateSelection(10, 300);
        Assert.Null(rig.Flow.Map.SelectedId);
    }
}
