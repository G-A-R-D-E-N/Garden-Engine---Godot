using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyMapCameraTests
{
    static MapModel Make(params MapMarker[] markers) =>
        new(new MapBounds(0, 0, 1000, 1000), 800, 500, markers, 0.75, 5.0);

    [Fact]
    public void Zoom_t_maps_linearly_between_the_model_limits()
    {
        var map = Make();

        Assert.Equal(0.0, map.ZoomT, 9);
        map.SetZoomT(0.5);
        Assert.Equal(0.75 + 0.5 * 4.25, map.Zoom, 9);
        map.SetZoomT(5);
        Assert.Equal(5.0, map.Zoom, 9);
        Assert.Equal(1.0, map.ZoomT, 9);
        map.SetZoomT(double.NaN);
        Assert.Equal(5.0, map.Zoom, 9);
    }

    [Fact]
    public void One_wheel_notch_changes_t_by_fifteen_hundredths_and_clamps_at_the_ends()
    {
        var map = Make();
        map.SetZoomT(0.5);

        MapCamera.ZoomNotch(map, true);
        Assert.Equal(0.65, map.ZoomT, 9);
        MapCamera.ZoomNotch(map, false);
        MapCamera.ZoomNotch(map, false);
        Assert.Equal(0.35, map.ZoomT, 9);

        map.SetZoomT(0.95);
        MapCamera.ZoomNotch(map, true);
        Assert.Equal(1.0, map.ZoomT, 9);
    }

    [Fact]
    public void A_notch_around_an_anchor_keeps_the_world_point_under_it()
    {
        var map = Make();
        map.SetZoomT(0.3);
        var (wx, wy) = map.ScreenToWorld(200, 120);

        MapCamera.ZoomNotch(map, true, 200, 120);

        var (ax, ay) = map.ScreenToWorld(200, 120);
        Assert.Equal(wx, ax, 6);
        Assert.Equal(wy, ay, 6);
    }

    [Fact]
    public void The_right_stick_zoom_is_frame_rate_independent_and_honours_the_deadzone()
    {
        var one = Make();
        var two = Make();
        one.SetZoomT(0.5);
        two.SetZoomT(0.5);

        Assert.True(MapCamera.ZoomStick(one, 1.0, 1.0 / 30.0));
        MapCamera.ZoomStick(two, 1.0, 1.0 / 60.0);
        MapCamera.ZoomStick(two, 1.0, 1.0 / 60.0);

        Assert.Equal(0.55, one.ZoomT, 9);
        Assert.Equal(one.ZoomT, two.ZoomT, 9);
        Assert.False(MapCamera.ZoomStick(one, 0.1, 1.0 / 30.0));
        Assert.False(MapCamera.ZoomStick(one, double.NaN, 1.0 / 30.0));
        Assert.Equal(0.55, one.ZoomT, 9);
    }

    [Fact]
    public void An_arrow_press_pans_thirty_pixels_and_a_drag_scales_by_the_view()
    {
        var map = Make();
        map.SetZoom(2);
        var start = map.CenterX;

        MapCamera.PanArrow(map, 1, 0);
        Assert.Equal(start + 15, map.CenterX, 9);

        map.CenterOn(500, 500);
        MapCamera.Drag(map, MapView.Local, 10, 0);
        Assert.Equal(500 - 10 * 2.75 / 2, map.CenterX, 9);

        map.CenterOn(500, 500);
        MapCamera.Drag(map, MapView.World, 10, 0);
        Assert.Equal(495, map.CenterX, 9);

        MapCamera.Drag(map, MapView.World, double.NaN, 1);
        MapCamera.PanArrow(map, double.PositiveInfinity, 0);
        Assert.Equal(495, map.CenterX, 9);
    }

    [Fact]
    public void The_gutter_lets_the_view_run_six_percent_of_the_viewport_past_the_bounds()
    {
        var plain = Make();
        var padded = Make();
        padded.GutterFraction = MapModel.SpecGutter;
        plain.SetZoom(1);
        padded.SetZoom(1);

        plain.CenterOn(0, 0);
        padded.CenterOn(0, 0);

        Assert.Equal(400, plain.CenterX, 9);
        Assert.Equal(250, plain.CenterY, 9);
        Assert.Equal(400 - 0.06 * 800, padded.CenterX, 9);
        Assert.Equal(250 - 0.06 * 500, padded.CenterY, 9);
    }

    [Fact]
    public void Clamping_to_the_edge_keeps_inside_points_and_insets_thirty_at_the_top_and_ten_elsewhere()
    {
        Assert.Equal((100.0, 100.0), MapCamera.ClampToEdge(100, 100, 800, 500));

        var right = MapCamera.ClampToEdge(4900, 250, 800, 500);
        Assert.Equal(790, right.X, 9);
        Assert.Equal(250, right.Y, 9);

        var top = MapCamera.ClampToEdge(400, -1000, 800, 500);
        Assert.Equal(400, top.X, 9);
        Assert.Equal(30, top.Y, 9);

        var bottom = MapCamera.ClampToEdge(400, 5000, 800, 500);
        Assert.Equal(490, bottom.Y, 9);

        var left = MapCamera.ClampToEdge(-900, 250, 800, 500);
        Assert.Equal(10, left.X, 9);
    }

    [Fact]
    public void The_edge_point_lies_on_the_line_from_the_centre_to_the_marker()
    {
        var (x, y) = MapCamera.ClampToEdge(1600, 1250, 800, 500);

        Assert.Equal(688, x, 9);
        Assert.Equal(490, y, 9);
        Assert.Equal((1600 - 400) / (double)(1250 - 250), (x - 400) / (y - 250), 9);
    }

    [Fact]
    public void Only_quest_kinds_are_clamped_and_they_stay_pickable_on_the_edge()
    {
        var quest = new MapMarker("q", "Dig", "Quest Door", 5000, 500);
        var place = new MapMarker("p", "Far", "Location", 5000, 500);
        var map = Make(quest, place);
        map.SetZoom(1);
        map.CenterOn(500, 500);

        var clamped = map.DisplayPosition(quest);
        var plain = map.DisplayPosition(place);

        Assert.Equal(790, clamped.X, 9);
        Assert.Equal(250, clamped.Y, 9);
        Assert.Equal(4900, plain.X, 9);
        Assert.Equal("q", map.NearestToScreen(790, 250, 10)?.Id);
        Assert.Null(map.NearestToScreen(780, 100, 10));
    }

    [Fact]
    public void Picking_by_pixels_matches_the_old_world_distance_pick_for_inside_markers()
    {
        var map = Make(new MapMarker("a", "A", "Location", 500, 500), new MapMarker("b", "B", "Location", 520, 500));
        map.SetZoom(2);
        map.CenterOn(500, 500);

        Assert.Equal("a", map.NearestToScreen(400, 250, 10)?.Id);
        Assert.Equal("b", map.NearestToScreen(440, 250, 10)?.Id);
        Assert.Null(map.NearestToScreen(600, 250, 10));
    }

    [Fact]
    public void The_label_box_goes_under_the_marker_flips_above_at_the_bottom_and_stays_inside()
    {
        Assert.Equal(new MapLabelBox(350, 210), MapCamera.PlaceLabel(400, 200, 100, 20, 800, 500));
        Assert.Equal(new MapLabelBox(350, 460), MapCamera.PlaceLabel(400, 490, 100, 20, 800, 500));
        Assert.Equal(new MapLabelBox(0, 10 + 5), MapCamera.PlaceLabel(5, 5, 100, 20, 800, 500));
        Assert.Equal(new MapLabelBox(700, 210), MapCamera.PlaceLabel(790, 200, 100, 20, 800, 500));
        Assert.Equal(0, MapCamera.PlaceLabel(400, 200, 900, 20, 800, 500).X);
    }

    [Theory]
    [InlineData("Location", MapGlyph.Location)]
    [InlineData("Settlement", MapGlyph.SettlementOwned)]
    [InlineData("settlement_raider", MapGlyph.SettlementRaider)]
    [InlineData("Quest Door", MapGlyph.Quest)]
    [InlineData("Power Armor", MapGlyph.PowerArmor)]
    [InlineData("Door", MapGlyph.Door)]
    [InlineData("Waypoint", MapGlyph.Waypoint)]
    [InlineData("Type 1", MapGlyph.Location)]
    [InlineData("", MapGlyph.Location)]
    public void Each_kind_maps_to_one_glyph_class_and_unknown_kinds_are_locations(string type, MapGlyph expected)
    {
        Assert.Equal(expected, MapGlyphs.Of(new MapMarker("m", "M", type, 0, 0)));
    }

    [Fact]
    public void An_undiscovered_location_is_hollow_but_other_kinds_keep_their_class()
    {
        Assert.Equal(MapGlyph.LocationUndiscovered, MapGlyphs.Of(new MapMarker("m", "M", "Location", 0, 0, false)));
        Assert.Equal(MapGlyph.Quest, MapGlyphs.Of(new MapMarker("m", "M", "Quest", 0, 0, false)));
    }

    [Fact]
    public void Marker_scale_follows_the_spec_table_for_both_views()
    {
        Assert.Equal(2.0, MapGlyphs.Scale(MapView.Local, 0, false), 9);
        Assert.Equal(1.5, MapGlyphs.Scale(MapView.Local, 1, false), 9);
        Assert.Equal(2.0, MapGlyphs.Scale(MapView.World, 0, false), 9);
        Assert.Equal(0.5, MapGlyphs.Scale(MapView.World, 1, false), 9);
        Assert.Equal(2.5, MapGlyphs.Scale(MapView.Local, 0, true), 9);
        Assert.Equal(1.875, MapGlyphs.Scale(MapView.Local, 1, true), 9);
        Assert.Equal(0.625, MapGlyphs.Scale(MapView.World, 1, true), 9);
        Assert.Equal(2.5 + 0.5 * (0.625 - 2.5), MapGlyphs.Scale(MapView.World, 0.5, true), 9);
        Assert.Equal(0.5, MapGlyphs.Scale(MapView.World, 7, false), 9);
    }

    [Fact]
    public void The_legend_lists_each_drawn_class_once_in_a_fixed_order_with_plain_words()
    {
        var drawn = new[]
        {
            new MapMarker("a", "A", "Quest", 0, 0),
            new MapMarker("b", "B", "Location", 0, 0),
            new MapMarker("c", "C", "Quest Door", 0, 0),
            new MapMarker("d", "D", "Settlement", 0, 0),
        };

        var legend = MapGlyphs.Legend(drawn, true, false);

        Assert.Equal(new[] { MapGlyph.Location, MapGlyph.SettlementOwned, MapGlyph.Quest, MapGlyph.Waypoint }, legend.Select(e => e.Glyph));
        Assert.Equal(new[] { "LOCATION", "SETTLEMENT", "QUEST", "MARKER" }, legend.Select(e => e.Word));
        Assert.Equal(new[] { MapGlyph.Player }, MapGlyphs.Legend(System.Array.Empty<MapMarker>(), false, true).Select(e => e.Glyph));
        Assert.Empty(MapGlyphs.Legend(System.Array.Empty<MapMarker>(), false, false));
    }

    [Fact]
    public void Action_labels_are_plain_words_without_key_names()
    {
        var flow = new MapTravelFlow();
        flow.SetSources(new MapSource(Make(), "world"), new MapSource(Make(), "local"));

        Assert.Equal(new[] { "PLACE MARKER", "LOCAL MAP", "SHOW LEGEND" }, flow.Actions().Select(flow.LabelFor));
        flow.ToggleLegend();
        Assert.Equal("HIDE LEGEND", flow.LabelFor(MapAction.Legend));
        Assert.DoesNotContain(flow.Actions().Select(flow.LabelFor), label => label.StartsWith("R ") || label.StartsWith("L ") || label.StartsWith("ENTER"));
    }

    static double Measure(string text) => text.Length * 10.0;

    [Fact]
    public void Text_that_fits_is_left_alone()
    {
        Assert.Equal(new FittedText("Short", 1, false), TextFit.Fit("Short", 100, Measure));
        Assert.Equal(new FittedText(string.Empty, 1, false), TextFit.Fit(null, 100, Measure));
    }

    [Fact]
    public void Text_that_is_a_little_too_wide_shrinks_to_fit()
    {
        var fitted = TextFit.Fit("0123456789012", 100, Measure);

        Assert.Equal("0123456789012", fitted.Text);
        Assert.Equal(100.0 / 130.0, fitted.Scale, 9);
        Assert.False(fitted.Truncated);
    }

    [Fact]
    public void Text_that_would_shrink_below_the_minimum_is_cut_with_dots_at_the_minimum_scale()
    {
        var fitted = TextFit.Fit("ABCDEFGHIJKLMNOPQRST", 100, Measure);

        Assert.Equal("ABCDEFGHIJKLM...", fitted.Text);
        Assert.Equal(TextFit.MinScale, fitted.Scale, 9);
        Assert.True(fitted.Truncated);
        Assert.True(Measure(fitted.Text) * fitted.Scale <= 100);
    }

    [Fact]
    public void A_zero_width_gives_nothing_and_a_hopeless_width_gives_the_dots()
    {
        Assert.Equal(string.Empty, TextFit.Fit("Name", 0, Measure).Text);
        Assert.Equal("...", TextFit.Fit("Name", 1, Measure).Text);
    }
}
