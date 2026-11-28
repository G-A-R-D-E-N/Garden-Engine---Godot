using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ScopeOverlayTests
{
    private static ScopeOverlayModel Shown(ScopeState? state = null)
    {
        var model = new ScopeOverlayModel();
        model.Apply(state ?? new ScopeState(ScopeStyle.Crosshair, 4));
        return model;
    }

    private static double Area(IReadOnlyList<ScopePoint> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }

    private static ScopeGeometry Geo(ScopeOverlayModel model, double w = 1920, double h = 1080) => model.Layout(w, h)!;

    private static ReticleShape[] Lines(ScopeGeometry geo) => geo.Reticle.Where(s => s.Kind == ReticleShapeKind.Line).ToArray();

    [Fact]
    public void Null_hides_and_a_state_shows_and_nothing_is_laid_out_before_a_state()
    {
        var model = new ScopeOverlayModel();
        Assert.False(model.Visible);
        Assert.Null(model.Layout(1920, 1080));

        model.Apply(new ScopeState(ScopeStyle.Dot, 4));
        Assert.True(model.Visible);
        Assert.Equal(1920.0, model.Layout(1920, 1080)!.Width);
        Assert.Equal(1080.0, model.Layout(1920, 1080)!.Height);
        Assert.Equal(960.0, model.Layout(1920, 1080)!.CentreX);
        Assert.Equal(540.0, model.Layout(1920, 1080)!.CentreY);

        model.Apply(null);
        Assert.False(model.Visible);
    }

    [Fact]
    public void Bad_viewport_sizes_lay_out_nothing()
    {
        var model = Shown();

        Assert.Null(model.Layout(0, 1080));
        Assert.Null(model.Layout(1920, -1));
        Assert.Null(model.Layout(double.NaN, 1080));
        Assert.Null(model.Layout(1920, double.PositiveInfinity));
    }

    [Theory]
    [InlineData(1920, 1080, 972)]
    [InlineData(3440, 1440, 1296)]
    [InlineData(5120, 1440, 1296)]
    [InlineData(1080, 1920, 972)]
    public void The_lens_is_a_circle_centred_with_ninety_percent_of_the_smaller_side(int width, int height, int diameter)
    {
        var geo = Geo(Shown(), width, height);

        Assert.Equal(diameter, geo.Radius * 2, 6);
        Assert.Equal(width / 2, geo.CentreX, 6);
        Assert.Equal(height / 2, geo.CentreY, 6);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    [InlineData(5120, 1440)]
    [InlineData(1080, 1920)]
    [InlineData(800, 800)]
    public void The_mask_and_the_lens_tile_the_whole_viewport_on_any_aspect(int width, int height)
    {
        var geo = Geo(Shown(), width, height);
        double n = ScopeOverlayModel.ArcSegments;
        double lens = 4 * 0.5 * geo.Radius * geo.Radius * n * Math.Sin(Math.PI / (2 * n));
        double mask = geo.Mask.Sum(Area);

        Assert.Equal(width * height, mask + lens, 2);
    }

    [Fact]
    public void The_mask_has_side_bars_on_a_wide_viewport_and_the_corner_pieces_never_cover_the_lens_centre()
    {
        var geo = Geo(Shown(), 3440, 1440);

        Assert.Equal(8, geo.Mask.Count);
        Assert.All(geo.Mask, polygon => Assert.DoesNotContain(polygon, p => Math.Abs(p.X - geo.CentreX) < 1e-9 && Math.Abs(p.Y - geo.CentreY) < 1e-9));
        var left = geo.Mask[0];
        Assert.Equal(0, left.Min(p => p.X), 6);
        Assert.Equal(geo.CentreX - geo.Radius, left.Max(p => p.X), 6);
    }

    [Fact]
    public void The_soft_edge_sits_inside_the_lens_and_fades_inwards()
    {
        var geo = Geo(Shown());

        Assert.Equal(ScopeOverlayModel.EdgeSteps, geo.EdgeRings.Count);
        Assert.All(geo.EdgeRings, ring => Assert.InRange(ring.Radius, geo.Radius - 6, geo.Radius));
        Assert.True(geo.EdgeRings[0].Alpha > geo.EdgeRings[^1].Alpha);
        Assert.True(geo.EdgeRings[0].Radius > geo.EdgeRings[^1].Radius);
    }

    [Fact]
    public void Fading_in_takes_150_ms_and_a_second_state_while_visible_does_not_restart_it()
    {
        var model = Shown();
        Assert.Equal(0, model.Alpha, 6);
        model.Tick(0.075);
        Assert.Equal(0.5, model.Alpha, 6);

        model.Apply(new ScopeState(ScopeStyle.Duplex, 8));
        Assert.Equal(0.5, model.Alpha, 6);
        model.Tick(0.075);
        Assert.Equal(1, model.Alpha, 6);

        model.Apply(null);
        model.Tick(0.05);
        Assert.Equal(0.5, model.Alpha, 6);
        model.Tick(0.05);
        Assert.Equal(0, model.Alpha, 6);
    }

    [Fact]
    public void The_crosshair_is_four_arms_with_a_gap_reaching_seventy_percent_of_the_lens()
    {
        var geo = Geo(Shown());
        var lines = Lines(geo);

        Assert.Equal(4, geo.Reticle.Count);
        Assert.Equal(4, lines.Length);
        Assert.All(lines, l => Assert.Equal(2, l.Thickness, 6));
        var left = lines[0];
        Assert.Equal(geo.CentreX - 0.7 * geo.Radius, left.X1, 6);
        Assert.Equal(geo.CentreX - 6, left.X2, 6);
        Assert.Equal(geo.CentreY, left.Y1, 6);
    }

    [Fact]
    public void The_duplex_has_thin_inner_and_thick_outer_arms_and_a_centre_dot()
    {
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Duplex, 4)));
        var lines = Lines(geo);

        Assert.Equal(8, lines.Length);
        Assert.Equal(4, lines.Count(l => l.Thickness == 2));
        Assert.Equal(4, lines.Count(l => l.Thickness == 4));
        var dot = Assert.Single(geo.Reticle, s => s.Kind == ReticleShapeKind.Dot);
        Assert.Equal(geo.CentreX, dot.X1, 6);
        Assert.Equal(2, dot.X2, 6);
    }

    [Fact]
    public void The_dot_style_is_a_four_unit_dot_with_a_faint_ring()
    {
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Dot, 4)));

        Assert.Equal(2, geo.Reticle.Count);
        Assert.Equal(ReticleShapeKind.Dot, geo.Reticle[0].Kind);
        Assert.Equal(2, geo.Reticle[0].X2, 6);
        Assert.Equal(ReticleShapeKind.Ring, geo.Reticle[1].Kind);
        Assert.True(geo.Reticle[1].Alpha < 1);
    }

    [Fact]
    public void The_ladder_is_a_vertical_line_with_five_ticks_24_units_apart_and_the_centre_tick_on_the_centre()
    {
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Ladder, 4)));
        var lines = Lines(geo);

        Assert.Equal(6, lines.Length);
        var ticks = lines.Skip(1).ToArray();
        Assert.Equal(new[] { -48.0, -24, 0, 24, 48 }, ticks.Select(t => t.Y1 - geo.CentreY).ToArray());
        Assert.Equal(12, ticks[2].X2 - geo.CentreX, 6);
        Assert.Equal(8, ticks[0].X2 - geo.CentreX, 6);
    }

    [Fact]
    public void An_unknown_style_falls_back_to_the_crosshair()
    {
        var model = Shown(new ScopeState((ScopeStyle)42, 4));

        Assert.Equal(ScopeStyle.Crosshair, model.Style);
        Assert.Equal(4, Lines(Geo(model)).Length);
    }

    [Theory]
    [InlineData(3.0, 2.0)]
    [InlineData(0.1, 0.5)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 2.0)]
    [InlineData(double.NegativeInfinity, 0.5)]
    [InlineData(1.25, 1.25)]
    public void The_reticle_scale_clamps_and_scales_every_dimension_with_a_one_pixel_floor(double requested, double effective)
    {
        var model = Shown(new ScopeState(ScopeStyle.Crosshair, 4, ReticleScale: requested));
        var left = Lines(Geo(model))[0];

        Assert.Equal(effective, model.ReticleScale, 6);
        Assert.Equal(Math.Max(1, 2 * effective), left.Thickness, 6);
        Assert.Equal(Geo(model).CentreX - 6 * effective, left.X2, 6);
    }

    [Fact]
    public void Sizes_scale_with_the_viewport_height()
    {
        var geo = Geo(Shown(), 1280, 720);

        Assert.Equal(720.0 / 1080, geo.Unit, 6);
        Assert.Equal(Math.Max(1, 2 * 720.0 / 1080), Lines(geo)[0].Thickness, 6);
    }

    [Fact]
    public void The_zoom_readout_shows_one_decimal_and_hides_for_bad_values()
    {
        Assert.Equal("x4.0", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 4))).ZoomText);
        Assert.Equal("x2.5", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 2.46))).ZoomText);
        Assert.Equal("", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 0))).ZoomText);
        Assert.Equal("", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, -1))).ZoomText);
        Assert.Equal("", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, double.NaN))).ZoomText);
    }

    [Fact]
    public void A_variable_scope_shows_both_ends_and_marks_the_current_zoom()
    {
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 6, 4, 8)));
        Assert.Equal("x4.0 - x8.0", geo.ZoomText);
        Assert.Equal(0.5, geo.ZoomMarker, 6);

        Assert.Equal(1, Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 12, 4, 8))).ZoomMarker, 6);
        Assert.Equal("x4.0", Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 4, 4, 4))).ZoomText);
    }

    [Fact]
    public void Seventeen_recon_marks_draw_sixteen()
    {
        var marks = Enumerable.Range(0, 17).Select(i => new ReconMark("m" + i, 0.5, 0.5)).ToArray();
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 4, Recon: marks)));

        Assert.Equal(16, geo.Marks.Count);
        Assert.Equal("m15", geo.Marks[^1].Id);
    }

    [Fact]
    public void Marks_map_into_the_lens_square_and_the_ones_outside_the_circle_or_not_finite_are_skipped()
    {
        var marks = new[]
        {
            new ReconMark("corner", 0, 0),
            new ReconMark("centre", 0.5, 0.5),
            new ReconMark("upper", 0.5, 0.25),
            new ReconMark("nan", double.NaN, 0.5),
            new ReconMark("far", 4, 4),
        };
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 4, Recon: marks)));

        Assert.Equal(new[] { "centre", "upper" }, geo.Marks.Select(m => m.Id).ToArray());
        Assert.Equal(geo.CentreX, geo.Marks[0].X, 6);
        Assert.Equal(geo.CentreY - geo.Radius / 2, geo.Marks[1].Y, 6);
    }

    [Fact]
    public void Hostile_marks_keep_their_flag_and_use_the_warning_colour()
    {
        var model = Shown(new ScopeState(ScopeStyle.Crosshair, 4, Recon: new[]
        {
            new ReconMark("a", 0.5, 0.5, Hostile: true),
            new ReconMark("b", 0.4, 0.5),
        }));
        var geo = Geo(model);

        Assert.Equal(new[] { true, false }, geo.Marks.Select(m => m.Hostile).ToArray());
        Assert.Equal(HudPalette.Standard.Warning, model.MarkRgba(true, HudPalette.Standard));
        Assert.Equal(model.ReticleRgba(HudPalette.Standard), model.MarkRgba(false, HudPalette.Standard));
    }

    [Fact]
    public void Distances_show_only_on_tall_viewports_and_when_the_option_is_on()
    {
        var marks = new[]
        {
            new ReconMark("a", 0.5, 0.5, 42.4),
            new ReconMark("b", 0.4, 0.5, -3),
            new ReconMark("c", 0.6, 0.5, double.NaN),
            new ReconMark("d", 0.5, 0.4),
        };
        var model = Shown(new ScopeState(ScopeStyle.Crosshair, 4, Recon: marks));

        Assert.Equal(new[] { "42 m", "", "", "" }, Geo(model).Marks.Select(m => m.DistanceText).ToArray());
        Assert.All(Geo(model, 1280, 720).Marks, m => Assert.Equal("", m.DistanceText));

        model.Options = new ScopeOptions(ShowReconDistances: false);
        Assert.All(Geo(model).Marks, m => Assert.Equal("", m.DistanceText));
    }

    [Fact]
    public void An_equal_state_changes_nothing_and_a_resize_or_new_state_builds_a_new_layout()
    {
        var model = Shown(new ScopeState(ScopeStyle.Duplex, 4, Recon: new[] { new ReconMark("a", 0.5, 0.5) }));
        var first = Geo(model);

        model.Apply(new ScopeState(ScopeStyle.Duplex, 4, Recon: new[] { new ReconMark("a", 0.5, 0.5) }));
        Assert.Same(first, model.Layout(1920, 1080));

        Assert.NotSame(first, model.Layout(1280, 720));
        model.Apply(new ScopeState(ScopeStyle.Duplex, 5));
        Assert.NotSame(first, model.Layout(1920, 1080));
    }

    [Fact]
    public void The_label_is_kept_as_supplied_with_a_width_of_sixty_percent_of_the_lens()
    {
        var geo = Geo(Shown(new ScopeState(ScopeStyle.Crosshair, 4, Label: " Sample [b]rifle ")));

        Assert.Equal(" Sample [b]rifle ", geo.Label);
        Assert.Equal(0.6 * 972, geo.LabelMaxWidth, 6);
    }

    [Fact]
    public void Ellipsize_cuts_long_text_to_the_width_and_leaves_short_text_alone()
    {
        static double Measure(string text) => text.Length * 10;

        Assert.Equal("abcdefghij", ScopeOverlayModel.Ellipsize("abcdefghij", 100, Measure));
        Assert.Equal("ab...", ScopeOverlayModel.Ellipsize("abcdefghij", 55, Measure));
        Assert.Equal("...", ScopeOverlayModel.Ellipsize("abcdefghij", 20, Measure));
        Assert.Equal("", ScopeOverlayModel.Ellipsize("", 5, Measure));
    }

    [Fact]
    public void The_reticle_tint_option_replaces_the_palette_colour()
    {
        var model = Shown();
        Assert.Equal(HudPalette.Standard.Accent, model.ReticleRgba(HudPalette.Standard));

        model.Options = new ScopeOptions(Tint: 0x112233FF);
        Assert.Equal(0x112233FFu, model.ReticleRgba(HudPalette.Standard));
        Assert.Equal(0x112233FFu, model.MarkRgba(false, HudPalette.ColourBlindSafe));
    }

    [Fact]
    public void A_source_feeds_the_model()
    {
        var source = new InMemoryScopeSource();
        var model = new ScopeOverlayModel();

        source.Set(new ScopeState(ScopeStyle.Ladder, 4));
        model.Sync(source);
        Assert.True(model.Visible);
        Assert.Equal(ScopeStyle.Ladder, model.Style);

        source.Set(null);
        model.Sync(source);
        Assert.False(model.Visible);
    }

    [Fact]
    public void The_hud_state_ticks_the_scope_and_hides_it_with_the_hud()
    {
        var state = new HudState();
        state.Scope.Apply(new ScopeState(ScopeStyle.Crosshair, 4));
        Assert.False(state.IsScopeDrawn);

        state.Tick(0.2);
        Assert.True(state.IsScopeDrawn);

        state.Hidden = true;
        Assert.False(state.IsScopeDrawn);
        state.Hidden = false;

        state.Scope.Apply(null);
        state.Tick(0.2);
        Assert.False(state.IsScopeDrawn);
    }
}
