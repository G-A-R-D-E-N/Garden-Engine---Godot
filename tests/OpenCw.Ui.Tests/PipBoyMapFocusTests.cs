using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyMapFocusTests
{
    sealed class DataSource : IPipBoyTabSource
    {
        public PipBoyTab Tab => PipBoyTab.Data;

        public string? CurrentKey => null;

        public bool Select(string key) => true;

        public System.Collections.Generic.IReadOnlyList<PipBoySearchHit> Search(System.Collections.Generic.IReadOnlyList<string> tokens) =>
            System.Array.Empty<PipBoySearchHit>();
    }

    static (PipBoyModel Pip, MapTravelFlow Flow, MapFlowSource Source) Make()
    {
        var flow = new MapTravelFlow();
        flow.SetSources(new MapSource(GallerySamples.NewMap(), "world"), new MapSource(GallerySamples.NewLocalMap(), "local"));
        var source = new MapFlowSource(flow);
        var pip = new PipBoyModel();
        pip.Register(new DataSource());
        pip.Register(source);
        pip.Open(PipBoyTab.Data);
        return (pip, flow, source);
    }

    static PipBoySearchHit Hit(string key) => new(PipBoyTab.Map, key, string.Empty, string.Empty);

    [Fact]
    public void Jump_to_a_world_marker_selects_it_zooms_to_the_midpoint_and_switches_the_tab()
    {
        var (pip, flow, _) = Make();

        Assert.True(pip.JumpTo(Hit("marker_a")));

        var map = flow.Map!;
        Assert.Equal(PipBoyTab.Map, pip.Tab);
        Assert.Equal(MapView.World, flow.View);
        Assert.Equal("marker_a", map.SelectedId);
        Assert.Equal(map.MinZoom + 0.5 * (map.MaxZoom - map.MinZoom), map.Zoom, 6);
    }

    [Fact]
    public void Jump_to_a_local_marker_switches_to_the_local_view()
    {
        var (pip, flow, _) = Make();
        var changes = new System.Collections.Generic.List<MapView>();
        flow.ViewChanged += changes.Add;

        Assert.True(pip.JumpTo(Hit("marker_f")));

        Assert.Equal(MapView.Local, flow.View);
        Assert.Equal("marker_f", flow.Map!.SelectedId);
        Assert.Equal(new[] { MapView.Local }, changes);
    }

    [Fact]
    public void Jump_to_an_unknown_or_undiscovered_marker_fails_and_shows_the_notice_for_two_seconds()
    {
        var (pip, flow, _) = Make();

        Assert.False(pip.JumpTo(Hit("nope")));
        Assert.False(pip.JumpTo(Hit("marker_d")));

        Assert.Equal(PipBoyTab.Data, pip.Tab);
        Assert.Equal(MapTravelFlow.NotFoundText, flow.Notice);
        flow.Tick(1.9);
        Assert.Equal(MapTravelFlow.NotFoundText, flow.Notice);
        flow.Tick(0.2);
        Assert.Equal(string.Empty, flow.Notice);
    }

    [Fact]
    public void Search_hits_jump_through_the_flow_too()
    {
        var (pip, flow, _) = Make();
        pip.Search("cache");

        Assert.True(pip.JumpToHit(0));

        Assert.Equal(MapView.Local, flow.View);
        Assert.Equal("marker_f", flow.Map!.SelectedId);
    }

    [Fact]
    public void Restoring_a_saved_cursor_selects_quietly_without_zooming_or_switching_view()
    {
        var (_, flow, source) = Make();
        var before = flow.Map!.Zoom;
        var views = 0;
        flow.ViewChanged += _ => views++;

        Assert.True(source.Select("marker_b"));

        Assert.Equal("marker_b", flow.Map.SelectedId);
        Assert.Equal(before, flow.Map.Zoom);
        Assert.Equal(0, views);
        Assert.False(source.Select("marker_f"));
    }

    [Fact]
    public void Changing_back_to_the_map_tab_does_not_refocus_the_last_marker()
    {
        var (pip, flow, _) = Make();
        pip.JumpTo(Hit("marker_a"));
        pip.SelectTab(PipBoyTab.Data);
        flow.Map!.SetZoom(flow.Map.MinZoom);
        var zoom = flow.Map.Zoom;

        pip.SelectTab(PipBoyTab.Map);

        Assert.Equal(zoom, flow.Map.Zoom);
    }

    [Fact]
    public void Search_covers_both_views_and_leaves_undiscovered_markers_out()
    {
        var (_, _, source) = Make();

        var hits = source.Search(new[] { "cache" });

        Assert.Equal(new[] { "marker_f" }, hits.Select(h => h.Key));
        Assert.DoesNotContain(source.Search(new[] { "site" }), h => h.Key == "marker_d");
    }

    [Fact]
    public void Every_sample_map_target_resolves_to_a_discovered_sample_marker()
    {
        var known = GallerySamples.Markers().Concat(GallerySamples.LocalMarkers()).Where(m => m.Discovered).Select(m => m.Id).ToHashSet();
        var targets = GallerySamples.Quests().Select(q => q.MapTarget)
            .Concat(GallerySamples.Workshops().Select(w => w.MapTarget))
            .Where(t => t is not null);

        Assert.All(targets, target => Assert.Contains(target!, known));
        Assert.Contains("marker_f", targets);
    }

    [Fact]
    public void Rebinding_after_a_jump_keeps_the_selection_and_only_closes_an_open_choice()
    {
        var (pip, flow, _) = Make();
        pip.JumpTo(Hit("marker_a"));
        flow.Accept();
        Assert.NotNull(flow.Choice);

        flow.Rebind();

        Assert.Null(flow.Choice);
        Assert.Equal("marker_a", flow.Map!.SelectedId);
    }

    [Fact]
    public void The_shell_flow_of_two_shown_calls_after_a_jump_keeps_the_selection()
    {
        var (pip, flow, _) = Make();

        pip.JumpTo(Hit("marker_b"));
        flow.Rebind();
        flow.Rebind();

        Assert.Equal("marker_b", flow.Map!.SelectedId);
    }

    static void AssertOnScreen(MapModel map, string id)
    {
        var (x, y) = map.DisplayPosition(map.Marker(id)!);
        Assert.InRange(x, 0, map.ViewWidth);
        Assert.InRange(y, 0, map.ViewHeight);
    }

    [Fact]
    public void Jumping_while_already_on_the_map_tab_leaves_the_marker_on_screen_at_the_new_zoom()
    {
        var (pip, flow, _) = Make();
        pip.Open(PipBoyTab.Map);
        Assert.Equal(PipBoyTab.Map, pip.Tab);

        Assert.True(pip.JumpTo(Hit("marker_b")));

        var map = flow.Map!;
        Assert.Equal("marker_b", map.SelectedId);
        Assert.Equal(map.MinZoom + 0.5 * (map.MaxZoom - map.MinZoom), map.Zoom, 6);
        AssertOnScreen(map, "marker_b");
    }

    [Fact]
    public void A_jump_that_switches_view_uses_the_current_viewport_for_the_target_map()
    {
        var (pip, flow, _) = Make();
        pip.JumpTo(Hit("marker_a"));
        flow.Map!.SetViewport(1000, 700);

        Assert.True(pip.JumpTo(Hit("marker_f")));

        var map = flow.Map!;
        Assert.Equal(MapView.Local, flow.View);
        Assert.Equal(1000, map.ViewWidth);
        Assert.Equal(700, map.ViewHeight);
        AssertOnScreen(map, "marker_f");
    }

    [Fact]
    public void A_refusal_reason_gets_a_full_stop_unless_it_already_ends_in_punctuation()
    {
        var rules = new FixedRules("Too heavy");
        var flow = new MapTravelFlow(rules);
        flow.SetSources(new MapSource(GallerySamples.NewMap(), "world"), null);
        flow.Pick("marker_a");
        flow.Accept();
        Assert.Equal("Too heavy. " + MapTravelFlow.PlaceMarkerQuestion, flow.Choice!.Prompt);

        var punctuated = new MapTravelFlow(new FixedRules("Too heavy!"));
        punctuated.SetSources(new MapSource(GallerySamples.NewMap(), "world"), null);
        punctuated.Pick("marker_a");
        punctuated.Accept();
        Assert.Equal("Too heavy! " + MapTravelFlow.PlaceMarkerQuestion, punctuated.Choice!.Prompt);
    }

    sealed class FixedRules : IMapTravelRules
    {
        private readonly string _reason;

        public FixedRules(string reason) => _reason = reason;

        public TravelVerdict CanFastTravel(MapMarker marker) => TravelVerdict.No(_reason);
    }
}
