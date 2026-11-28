using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class CompassGap9Tests
{
    private static readonly CompassMarker[] None = [];

    private static CompassMarker Marker(string id, double bearing, bool pinned = false, CompassMarkerKind kind = CompassMarkerKind.Location) =>
        new(id, bearing, kind, pinned);

    [Fact]
    public void Defaults_are_a_half_turn_window_with_four_degrees_of_hysteresis()
    {
        var model = new CompassModel();

        Assert.Equal(180.0, model.Span);
        Assert.Equal(4.0, model.Hysteresis);
        Assert.Empty(model.Ticks);
        Assert.Empty(model.Markers);
    }

    [Fact]
    public void The_constructor_takes_the_edge_values_and_rejects_nan()
    {
        var full = new CompassModel(360, 0);
        Assert.Equal(360.0, full.Span);
        Assert.Equal(0.0, full.Hysteresis);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CompassModel(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompassModel(180, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompassModel(-90));
    }

    [Fact]
    public void Facing_north_lists_every_tick_in_order_with_exact_position_label_alpha_and_major()
    {
        var model = new CompassModel();
        model.Update(0, None);

        var expected = new (double Position, string? Label, double Alpha, bool Major)[]
        {
            (0, "N", 1, true),
            (1.0 / 6, null, 1, false),
            (2.0 / 6, null, 1, false),
            (3.0 / 6, "NE", 1, true),
            (4.0 / 6, null, 1, false),
            (5.0 / 6, null, (1 - 5.0 / 6) / 0.25, false),
            (1, "E", 0, true),
            (-1, "W", 0, true),
            (-5.0 / 6, null, (1 - 5.0 / 6) / 0.25, false),
            (-4.0 / 6, null, 1, false),
            (-3.0 / 6, "NW", 1, true),
            (-2.0 / 6, null, 1, false),
            (-1.0 / 6, null, 1, false),
        };

        Assert.Equal(expected.Length, model.Ticks.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Position, model.Ticks[i].Position, 9);
            Assert.Equal(expected[i].Label, model.Ticks[i].Label);
            Assert.Equal(expected[i].Alpha, model.Ticks[i].Alpha, 9);
            Assert.Equal(expected[i].Major, model.Ticks[i].Major);
        }
    }

    [Fact]
    public void A_narrow_span_keeps_only_the_ticks_inside_it_and_stretches_their_positions()
    {
        var model = new CompassModel(90);
        model.Update(0, None);

        Assert.Equal(
            new (string? Label, bool Major)[] { ("N", true), (null, false), (null, false), ("NE", true), ("NW", true), (null, false), (null, false) },
            model.Ticks.Select(t => (t.Label, t.Major)).ToArray());
        Assert.Equal(
            new[] { 0.0, 1.0 / 3, 2.0 / 3, 1.0, -1.0, -2.0 / 3, -1.0 / 3 },
            model.Ticks.Select(t => t.Position).ToArray(),
            new ToleranceComparer());
    }

    [Fact]
    public void A_full_turn_span_shows_all_twenty_four_ticks_with_the_rear_one_at_minus_one()
    {
        var model = new CompassModel(360);
        model.Update(0, None);

        Assert.Equal(24, model.Ticks.Count);
        var rear = model.Ticks.Single(t => t.Label == "S");
        Assert.Equal(-1.0, rear.Position, 9);
        Assert.Equal(0.0, rear.Alpha, 9);
    }

    [Fact]
    public void Update_replaces_the_previous_ticks_and_markers()
    {
        var model = new CompassModel();
        model.Update(0, [Marker("m", 10)]);
        model.Update(90, None);

        Assert.Empty(model.Markers);
        Assert.Equal(-1.0, model.Ticks.Single(t => t.Label == "N").Position, 9);
        Assert.Equal(13, model.Ticks.Count);
    }

    [Fact]
    public void Marker_views_keep_id_kind_and_input_order_with_exact_position_and_alpha()
    {
        var model = new CompassModel();
        model.Update(0, [
            Marker("q", 45, kind: CompassMarkerKind.Objective),
            Marker("h", -45, kind: CompassMarkerKind.Hostile),
            Marker("f", 0, kind: CompassMarkerKind.Friendly)]);

        Assert.Equal(
            new[]
            {
                new CompassMarkerView("q", CompassMarkerKind.Objective, 0.5, 1, false),
                new CompassMarkerView("h", CompassMarkerKind.Hostile, -0.5, 1, false),
                new CompassMarkerView("f", CompassMarkerKind.Friendly, 0, 1, false),
            },
            model.Markers.ToArray());
    }

    [Fact]
    public void A_marker_held_by_hysteresis_sits_on_the_edge_fully_faded_on_both_sides()
    {
        var model = new CompassModel(180, 4);
        model.Update(0, [Marker("r", 89), Marker("l", -89)]);

        model.Update(0, [Marker("r", 94), Marker("l", -94)]);

        Assert.Equal(
            new[]
            {
                new CompassMarkerView("r", CompassMarkerKind.Location, 1, 0, false),
                new CompassMarkerView("l", CompassMarkerKind.Location, -1, 0, false),
            },
            model.Markers.ToArray());
    }

    [Fact]
    public void Hysteresis_width_sets_how_far_past_the_edge_a_visible_marker_is_held()
    {
        var model = new CompassModel(180, 10);
        model.Update(0, [Marker("m", 90)]);
        Assert.Single(model.Markers);

        model.Update(0, [Marker("m", 100)]);
        Assert.Single(model.Markers);

        model.Update(0, [Marker("m", 100.5)]);
        Assert.Empty(model.Markers);

        var none = new CompassModel(180, 0);
        none.Update(0, [Marker("m", 90)]);
        none.Update(0, [Marker("m", 90.5)]);
        Assert.Empty(none.Markers);
    }

    [Fact]
    public void A_marker_dropped_from_the_input_loses_its_held_state()
    {
        var model = new CompassModel(180, 4);
        model.Update(0, [Marker("m", 89)]);
        model.Update(0, None);

        model.Update(0, [Marker("m", 92)]);

        Assert.Empty(model.Markers);
    }

    [Fact]
    public void A_pinned_marker_that_comes_back_in_range_is_a_normal_marker_again()
    {
        var model = new CompassModel(180, 4);
        model.Update(0, [Marker("m", 120, pinned: true)]);
        Assert.Equal(new CompassMarkerView("m", CompassMarkerKind.Location, 1, 1, true), model.Markers.Single());

        model.Update(0, [Marker("m", 45, pinned: true)]);

        Assert.Equal(new CompassMarkerView("m", CompassMarkerKind.Location, 0.5, 1, false), model.Markers.Single());
    }

    [Fact]
    public void A_marker_exactly_behind_with_no_history_pins_to_the_left()
    {
        var model = new CompassModel();
        model.Update(0, [Marker("m", 180, pinned: true)]);

        Assert.Equal(new CompassMarkerView("m", CompassMarkerKind.Location, -1, 1, true), model.Markers.Single());
    }

    [Fact]
    public void Hysteresis_width_also_sets_how_close_to_the_back_a_pinned_side_sticks()
    {
        var wide = new CompassModel(180, 10);
        wide.Update(0, [Marker("m", 179, pinned: true)]);
        wide.Update(0, [Marker("m", 185, pinned: true)]);
        Assert.Equal(1.0, wide.Markers.Single().Position, 9);

        var narrow = new CompassModel(180, 4);
        narrow.Update(0, [Marker("m", 179, pinned: true)]);
        narrow.Update(0, [Marker("m", 185, pinned: true)]);
        Assert.Equal(-1.0, narrow.Markers.Single().Position, 9);
    }

    [Fact]
    public void A_pinned_side_is_forgotten_after_the_marker_leaves_the_input()
    {
        var model = new CompassModel(180, 10);
        model.Update(0, [Marker("m", 179, pinned: true)]);
        model.Update(0, None);

        model.Update(0, [Marker("m", 185, pinned: true)]);

        Assert.Equal(-1.0, model.Markers.Single().Position, 9);
    }

    [Fact]
    public void An_infinite_heading_reads_as_north()
    {
        var model = new CompassModel();
        model.Update(double.PositiveInfinity, [Marker("m", 45)]);

        Assert.Equal(0.5, model.Markers.Single().Position, 9);
        Assert.Equal(0.0, model.Ticks.Single(t => t.Label == "N").Position, 9);
    }

    private sealed class ToleranceComparer : System.Collections.Generic.IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-9;

        public int GetHashCode(double obj) => 0;
    }
}
