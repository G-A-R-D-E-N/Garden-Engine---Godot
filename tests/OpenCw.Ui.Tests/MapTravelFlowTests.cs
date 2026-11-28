using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MapTravelFlowTests
{
    sealed class CountingRules : IMapTravelRules
    {
        public int Calls { get; private set; }

        public TravelVerdict Verdict { get; set; } = TravelVerdict.Yes;

        public TravelVerdict CanFastTravel(MapMarker marker)
        {
            Calls++;
            return Verdict;
        }
    }

    sealed class Rig
    {
        public MapTravelFlow Flow = null!;
        public List<FastTravelRequest> Travels = new();
        public List<string> Sounds = new();
        public int Changes;
    }

    static MapModel WorldModel() => new(new MapBounds(0, 0, 1000, 1000), 500, 500, new[]
    {
        new MapMarker("far", "Far Town", "location", 800, 800, CanFastTravel: true),
        new MapMarker("blocked", "Blocked Camp", "location", 200, 800, CanFastTravel: false),
        new MapMarker("fog", "Hidden Ruin", "location", 500, 500, Discovered: false, CanFastTravel: true),
        new MapMarker("door", "Door", "door", 100, 100, CanFastTravel: true),
        new MapMarker("way", "Marker", "waypoint", 900, 100),
    }, 0.25, 8);

    static MapModel LocalModel() => new(new MapBounds(0, 0, 100, 100), 100, 100, new[]
    {
        new MapMarker("loc1", "Cache", "location", 20, 30),
    }, 0.75, 2.0);

    static Rig Make(IMapTravelRules? rules = null, bool world = true, bool local = false)
    {
        var rig = new Rig { Flow = new MapTravelFlow(rules) };
        rig.Flow.FastTravelRequested += rig.Travels.Add;
        rig.Flow.SoundRequested += rig.Sounds.Add;
        rig.Flow.Changed += () => rig.Changes++;
        rig.Flow.SetSources(world ? new MapSource(WorldModel(), "world") : null,
                            local ? new MapSource(LocalModel(), "local") : null);
        return rig;
    }

    static MapMarker Marker(string type, bool discovered = true, bool travel = true) =>
        new("m", "M", type, 1, 1, discovered, travel);

    [Theory]
    [InlineData("Quest_Door", "questdoor")]
    [InlineData("Way Point", "waypoint")]
    [InlineData("LOCATION", "location")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Marker_types_are_compared_without_case_spaces_or_underscores(string? type, string expected)
    {
        Assert.Equal(expected, MapMarkerKinds.Normalize(type));
    }

    [Theory]
    [InlineData("waypoint", true)]
    [InlineData("Way_Point", true)]
    [InlineData("location", false)]
    [InlineData(null, false)]
    public void Only_the_waypoint_type_is_a_waypoint(string? type, bool expected)
    {
        Assert.Equal(expected, MapMarkerKinds.IsWaypoint(type));
    }

    [Theory]
    [InlineData("door", true)]
    [InlineData("Quest", true)]
    [InlineData("quest door", true)]
    [InlineData("PLAYER", true)]
    [InlineData("location", false)]
    [InlineData("waypoint", false)]
    [InlineData(null, false)]
    public void Doors_quests_and_the_player_are_never_travel_targets(string? type, bool expected)
    {
        Assert.Equal(expected, MapMarkerKinds.IsNeverTravelTarget(type));
    }

    [Fact]
    public void Only_ordinary_markers_on_the_world_map_can_be_travel_targets()
    {
        Assert.True(MapTravelStructure.CanTarget(MapView.World, Marker("location")));
        Assert.False(MapTravelStructure.CanTarget(MapView.Local, Marker("location")));
        Assert.False(MapTravelStructure.CanTarget(MapView.World, Marker("waypoint")));
        Assert.False(MapTravelStructure.CanTarget(MapView.World, Marker("Quest_Door")));
        Assert.False(MapTravelStructure.CanTarget(MapView.World, Marker("player")));
    }

    [Fact]
    public void A_verdict_carries_whether_travel_is_allowed_and_an_optional_reason()
    {
        Assert.Equal(new TravelVerdict(true, null), TravelVerdict.Yes);
        Assert.Equal(new TravelVerdict(false, null), TravelVerdict.No());
        Assert.Equal("Too heavy", TravelVerdict.No("Too heavy").Reason);
        Assert.False(TravelVerdict.No("Too heavy").Allowed);
        Assert.True(new OpenMapTravelRules().CanFastTravel(Marker("location", travel: false)).Allowed);
    }

    [Theory]
    [InlineData("Too heavy", "Too heavy. Place a marker here?")]
    [InlineData("Too heavy!", "Too heavy! Place a marker here?")]
    [InlineData("  Too far?  ", "Too far? Place a marker here?")]
    [InlineData("Too far.", "Too far. Place a marker here?")]
    [InlineData("", "Cannot travel there. Place a marker here?")]
    [InlineData("   ", "Cannot travel there. Place a marker here?")]
    [InlineData(null, "Cannot travel there. Place a marker here?")]
    public void A_refusal_prompt_ends_its_reason_with_punctuation_before_the_marker_question(string? reason, string expected)
    {
        var rig = Make(new CountingRules { Verdict = TravelVerdict.No(reason) });
        rig.Flow.Pick("far");

        rig.Flow.Accept();

        var choice = rig.Flow.Choice!;
        Assert.Equal(expected, choice.Prompt);
        Assert.Equal(new[] { "YES", "NO" }, choice.Options.Select(o => o.Label));
        Assert.Equal(new[] { MapChoiceAction.PlaceMarker, MapChoiceAction.Decline }, choice.Options.Select(o => o.Action));
        Assert.Equal(1, choice.CancelIndex);
        Assert.Equal("far", choice.MarkerId);
    }

    [Fact]
    public void A_travel_offer_names_the_town_and_offers_yes_no_and_place_marker_with_no_as_the_cancel()
    {
        var rig = Make();
        rig.Flow.Pick("far");

        rig.Flow.Accept();

        var choice = rig.Flow.Choice!;
        Assert.True(rig.Flow.IsModalOpen);
        Assert.Equal("Travel to Far Town?", choice.Prompt);
        Assert.Equal(new[] { "YES", "NO", "PLACE MARKER" }, choice.Options.Select(o => o.Label));
        Assert.Equal(new[] { MapChoiceAction.Travel, MapChoiceAction.Decline, MapChoiceAction.PlaceMarker },
            choice.Options.Select(o => o.Action));
        Assert.Equal(1, choice.CancelIndex);
        Assert.Equal(("far", 800.0, 800.0), (choice.MarkerId, choice.X, choice.Y));
        Assert.Equal(0, rig.Flow.ChoiceIndex);
    }

    [Fact]
    public void Moving_inside_an_open_offer_clamps_at_both_ends_and_plays_the_focus_sound_only_when_it_moves()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();
        rig.Sounds.Clear();

        rig.Flow.MoveChoice(-1);
        Assert.Equal(0, rig.Flow.ChoiceIndex);
        Assert.Empty(rig.Sounds);

        rig.Flow.MoveChoice(5);
        Assert.Equal(2, rig.Flow.ChoiceIndex);
        rig.Flow.MoveChoice(1);
        Assert.Equal(2, rig.Flow.ChoiceIndex);
        Assert.Equal(new[] { "Focus" }, rig.Sounds);
    }

    [Fact]
    public void While_an_offer_is_open_every_action_is_taken_by_it_and_back_declines()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        Assert.True(rig.Flow.Handle(MenuInputAction.Left));
        Assert.True(rig.Flow.Handle(MenuInputAction.Down));
        Assert.Equal(1, rig.Flow.ChoiceIndex);
        Assert.True(rig.Flow.Handle(MenuInputAction.Up));
        Assert.Equal(0, rig.Flow.ChoiceIndex);
        Assert.True(rig.Flow.IsModalOpen);

        Assert.True(rig.Flow.Handle(MenuInputAction.Back));

        Assert.False(rig.Flow.IsModalOpen);
        Assert.Empty(rig.Travels);
        Assert.Contains("Cancel", rig.Sounds);
        Assert.False(rig.Flow.Back());
    }

    [Fact]
    public void Without_an_offer_only_accept_is_handled()
    {
        var rig = Make();

        Assert.False(rig.Flow.Handle(MenuInputAction.Up));
        Assert.False(rig.Flow.Handle(MenuInputAction.Back));
        Assert.True(rig.Flow.Handle(MenuInputAction.Accept));
    }

    [Fact]
    public void Accepting_again_while_an_offer_is_open_chooses_the_highlighted_option()
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        rig.Flow.Accept();

        Assert.Equal(new FastTravelRequest("far", 800, 800), Assert.Single(rig.Travels));
        Assert.False(rig.Flow.IsModalOpen);
        Assert.Contains("Confirm", rig.Sounds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Choosing_an_option_that_does_not_exist_leaves_the_offer_open(int index)
    {
        var rig = Make();
        rig.Flow.Pick("far");
        rig.Flow.Accept();

        Assert.False(rig.Flow.Choose(index));

        Assert.True(rig.Flow.IsModalOpen);
        Assert.Empty(rig.Travels);
    }

    [Fact]
    public void Picking_selects_once_rolls_over_once_and_refuses_unknown_markers_and_an_open_offer()
    {
        var rig = Make();

        Assert.True(rig.Flow.Pick("far"));
        Assert.True(rig.Flow.Pick("far"));
        Assert.Equal(1, rig.Sounds.Count(s => s == "MapRollover"));
        Assert.Equal("far", rig.Flow.Map!.SelectedId);

        Assert.False(rig.Flow.Pick("nope"));
        Assert.Equal("far", rig.Flow.Map.SelectedId);

        rig.Flow.Accept();
        Assert.False(rig.Flow.Pick("blocked"));
        Assert.Equal("far", rig.Flow.Map.SelectedId);
    }

    [Fact]
    public void The_legend_toggles_with_its_label_and_cannot_be_toggled_during_an_offer_or_without_a_map()
    {
        var rig = Make();
        Assert.False(rig.Flow.LegendVisible);
        Assert.Equal("SHOW LEGEND", rig.Flow.LabelFor(MapAction.Legend));

        Assert.True(rig.Flow.ToggleLegend());
        Assert.True(rig.Flow.LegendVisible);
        Assert.Equal("HIDE LEGEND", rig.Flow.LabelFor(MapAction.Legend));

        Assert.True(rig.Flow.ToggleLegend());
        Assert.False(rig.Flow.LegendVisible);

        rig.Flow.Pick("far");
        rig.Flow.Accept();
        Assert.False(rig.Flow.ToggleLegend());
        Assert.False(rig.Flow.LegendVisible);

        Assert.False(new MapTravelFlow().ToggleLegend());
    }

    [Fact]
    public void Action_labels_name_the_other_view_for_the_switch()
    {
        var rig = Make(local: true);

        Assert.Equal("FAST TRAVEL", rig.Flow.LabelFor(MapAction.FastTravel));
        Assert.Equal("PLACE MARKER", rig.Flow.LabelFor(MapAction.PlaceMarker));
        Assert.Equal(MapView.World, rig.Flow.View);
        Assert.Equal("LOCAL MAP", rig.Flow.LabelFor(MapAction.SwitchView));

        Assert.True(rig.Flow.Switch());

        Assert.Equal(MapView.Local, rig.Flow.View);
        Assert.Equal("WORLD MAP", rig.Flow.LabelFor(MapAction.SwitchView));
        Assert.Contains("Transition", rig.Sounds);
    }

    [Fact]
    public void Switching_needs_the_other_map_and_the_models_list_the_world_before_the_local_map()
    {
        var worldOnly = Make();
        Assert.False(worldOnly.Flow.CanSwitch);
        Assert.False(worldOnly.Flow.Switch());
        Assert.Single(worldOnly.Flow.Models);

        var both = Make(local: true);
        Assert.True(both.Flow.CanSwitch);
        Assert.Equal(2, both.Flow.Models.Count);
        Assert.Same(both.Flow.Map, both.Flow.Models[0]);
    }

    [Fact]
    public void A_missing_marker_notice_stays_through_ignored_ticks_and_clears_after_two_seconds_with_one_change()
    {
        var rig = Make();

        Assert.False(rig.Flow.FocusMarker("nope"));
        Assert.Equal(MapTravelFlow.NotFoundText, rig.Flow.Notice);
        rig.Changes = 0;

        rig.Flow.Tick(0);
        rig.Flow.Tick(-1);
        rig.Flow.Tick(double.NaN);
        rig.Flow.Tick(1.5);
        Assert.Equal("MARKER NOT FOUND", rig.Flow.Notice);
        Assert.Equal(0, rig.Changes);

        rig.Flow.Tick(0.5);
        Assert.Equal("", rig.Flow.Notice);
        Assert.Equal(1, rig.Changes);

        rig.Flow.Tick(1);
        Assert.Equal(1, rig.Changes);
    }

    [Fact]
    public void The_verdict_only_asks_the_host_for_discovered_travellable_markers_on_the_world_map()
    {
        var rules = new CountingRules();
        var rig = Make(rules, local: true);

        Assert.Equal(TravelVerdict.No(), rig.Flow.VerdictFor(WorldModel().Marker("blocked")!));
        Assert.Equal(TravelVerdict.No(), rig.Flow.VerdictFor(WorldModel().Marker("fog")!));
        Assert.Equal(TravelVerdict.No(), rig.Flow.VerdictFor(WorldModel().Marker("door")!));
        Assert.Equal(TravelVerdict.No(), rig.Flow.VerdictFor(WorldModel().Marker("way")!));
        Assert.Equal(0, rules.Calls);

        Assert.Equal(TravelVerdict.Yes, rig.Flow.VerdictFor(WorldModel().Marker("far")!));
        Assert.Equal(1, rules.Calls);

        rig.Flow.Switch();
        Assert.Equal(TravelVerdict.No(), rig.Flow.VerdictFor(Marker("location")));
        Assert.Equal(1, rules.Calls);
    }

    [Fact]
    public void A_host_refusal_reason_is_passed_through_the_verdict()
    {
        var rules = new CountingRules { Verdict = TravelVerdict.No("Overburdened") };
        var rig = Make(rules);

        var verdict = rig.Flow.VerdictFor(WorldModel().Marker("far")!);

        Assert.False(verdict.Allowed);
        Assert.Equal("Overburdened", verdict.Reason);
    }

    [Fact]
    public void A_flow_with_no_sources_offers_no_actions_and_ignores_input()
    {
        var flow = new MapTravelFlow();

        Assert.Empty(flow.Actions());
        Assert.Equal("", flow.InstructionText());
        Assert.Null(flow.Map);
        Assert.Null(flow.ContextId);
        Assert.Empty(flow.Models);
        Assert.False(flow.Pick("far"));
        flow.Accept();
        Assert.False(flow.IsModalOpen);
        Assert.True(flow.Handle(MenuInputAction.Accept));
    }
}
