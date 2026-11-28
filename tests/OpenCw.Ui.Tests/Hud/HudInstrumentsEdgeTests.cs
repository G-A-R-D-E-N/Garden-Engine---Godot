using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudInstrumentsEdgeTests
{
    [Fact]
    public void The_secondary_count_takes_zero_and_positive_values_ignores_negatives_and_clears_on_null()
    {
        var i = new HudInstruments();
        Assert.Null(i.SecondaryCount);

        i.SetSecondaryCount(5);
        i.SetSecondaryCount(-1);
        Assert.Equal(5, i.SecondaryCount);
        i.SetSecondaryCount(0);
        Assert.Equal(0, i.SecondaryCount);
        i.SetSecondaryCount(null);
        Assert.Null(i.SecondaryCount);
    }

    [Fact]
    public void Enemy_details_drop_a_negative_level_keep_zero_and_reset_when_the_bar_is_shown_again()
    {
        var i = new HudInstruments();
        i.SetEnemy("Raider", 1);

        i.SetEnemyDetails(-3, true);
        Assert.Null(i.EnemyLevel);
        Assert.True(i.EnemyLegendary);
        i.SetEnemyDetails(0, false);
        Assert.Equal(0, i.EnemyLevel);

        i.ClearEnemy();
        i.SetEnemy("Raider", 0.5);
        Assert.Null(i.EnemyLevel);

        i.SetEnemyDetails(7, true);
        i.SetEnemy(null, -4);
        Assert.Equal("", i.EnemyName);
        Assert.Equal(0.0, i.EnemyHealth);
        Assert.Null(i.EnemyLevel);
        Assert.False(i.EnemyLegendary);
    }

    [Fact]
    public void The_critical_meter_clamps_charges_and_fraction_and_ignores_a_non_finite_fraction()
    {
        var i = new HudInstruments();
        i.SetCritical(double.NaN, 2);
        Assert.False(i.CriticalVisible);
        Assert.Equal(0, i.CriticalCharges);

        i.SetCritical(0.5, 9);
        Assert.True(i.CriticalVisible);
        Assert.Equal(0.5, i.CriticalFraction);
        Assert.Equal(HudInstruments.MaxCharges, i.CriticalCharges);
        Assert.True(i.CriticalReady);

        i.SetCritical(double.PositiveInfinity, 1);
        Assert.Equal(HudInstruments.MaxCharges, i.CriticalCharges);

        i.SetCritical(2, -4);
        Assert.Equal(1.0, i.CriticalFraction);
        Assert.Equal(0, i.CriticalCharges);
        Assert.True(i.CriticalVisible);
        Assert.False(i.CriticalReady);
        Assert.Equal(1.0, i.CriticalAlpha);

        i.ClearCritical();
        Assert.False(i.CriticalVisible);
        Assert.Equal(0.0, i.CriticalFraction);
    }

    [Fact]
    public void The_sneak_text_is_trimmed_the_target_is_clamped_to_a_percent_and_a_non_finite_level_only_updates_the_text()
    {
        var i = new HudInstruments();

        i.SetSneak("  Hidden ", 50, snap: true);
        Assert.Equal("Hidden", i.SneakText);
        Assert.Equal(60.0, i.SneakGap, 6);

        i.SetSneak("Hidden", 250);
        Assert.Equal(60.0, i.SneakGap, 6);
        i.SetSneak("Danger", double.NaN, snap: true);
        Assert.Equal("Danger", i.SneakText);
        Assert.Equal(60.0, i.SneakGap, 6);

        i.SetSneak("", 100);
        Assert.Equal(120.0, i.SneakGap, 6);
        i.SetSneak(null, -5);
        Assert.Equal("", i.SneakText);
        Assert.Equal(0.0, i.SneakGap, 6);
    }

    [Fact]
    public void A_status_chip_keeps_its_id_untouched_trims_its_text_and_rejects_blank_input()
    {
        var i = new HudInstruments();

        Assert.True(i.SetStatus("rad", "  Irradiated "));
        Assert.Equal(new HudStatusChip("rad", "Irradiated"), Assert.Single(i.Chips));
        Assert.False(i.SetStatus("rad", "   "));
        Assert.False(i.SetStatus("  ", "x"));
        Assert.Equal("Irradiated", Assert.Single(i.Chips).Text);
        Assert.False(i.ClearStatus("missing"));
    }

    [Fact]
    public void Setting_no_effects_clears_the_old_ones()
    {
        var i = new HudInstruments();
        i.SetEffects(new[] { "Well rested" });
        Assert.Equal(new[] { "Well rested" }, i.Effects);

        i.SetEffects(null);
        Assert.Empty(i.Effects);
    }

    [Fact]
    public void The_ap_preview_accepts_zero_drops_non_finite_segments_and_clears_on_a_bad_projection()
    {
        var i = new HudInstruments();

        i.SetActionPointPreview(0, new[] { 0.5, double.NaN, 2, -1, double.PositiveInfinity });
        Assert.Equal(0.0, i.ActionPointPreview);
        Assert.Equal(new[] { 0.5, 1.0, 0.0 }, i.PreviewSegments);

        i.SetActionPointPreview(3, null);
        Assert.Equal(3.0, i.ActionPointPreview);
        Assert.Empty(i.PreviewSegments);

        i.SetActionPointPreview(4, new[] { 0.25 });
        i.SetActionPointPreview(-1, new[] { 0.25 });
        Assert.Null(i.ActionPointPreview);
        Assert.Empty(i.PreviewSegments);
        i.SetActionPointPreview(double.NaN, new[] { 0.25 });
        Assert.Null(i.ActionPointPreview);
        i.SetActionPointPreview(null, null);
        Assert.Null(i.ActionPointPreview);
    }

    [Fact]
    public void Showing_a_hidden_group_once_undoes_any_number_of_hides_and_every_group_starts_visible()
    {
        var i = new HudInstruments();
        Assert.All(Enum.GetValues<HudGroup>(), g => Assert.True(i.IsGroupVisible(g)));

        i.SetGroupVisible(HudGroup.Ammo, false);
        i.SetGroupVisible(HudGroup.Ammo, false);
        Assert.False(i.IsGroupVisible(HudGroup.Ammo));
        Assert.True(i.IsGroupVisible(HudGroup.Vitals));
        i.SetGroupVisible(HudGroup.Ammo, true);
        Assert.True(i.IsGroupVisible(HudGroup.Ammo));
    }

    [Fact]
    public void Zero_negative_and_non_finite_ticks_change_nothing()
    {
        var i = new HudInstruments();
        i.SetEnemy("Raider", 1);
        i.SetSneak("Hidden", 100);
        i.Tick(0);
        i.Tick(-1);
        i.Tick(double.NaN);
        i.Tick(double.PositiveInfinity);

        Assert.Equal(0.0, i.EnemyAlpha);
        Assert.Equal(0.0, i.SneakGap);
        i.Tick(0.075);
        Assert.Equal(0.5, i.EnemyAlpha, 6);
        Assert.Equal(8.0, i.SneakGap, 6);
    }
}

public sealed class ScopeOverlayEdgeTests
{
    private static ScopeOverlayModel Shown()
    {
        var model = new ScopeOverlayModel();
        model.Apply(new ScopeState(ScopeStyle.Crosshair, 4));
        return model;
    }

    [Fact]
    public void Options_default_to_an_untinted_record_with_distances_and_a_null_assignment_restores_the_default()
    {
        var model = new ScopeOverlayModel();
        Assert.Equal(new ScopeOptions(), model.Options);

        model.Options = new ScopeOptions(0x112233FFu, false);
        Assert.Equal(0x112233FFu, model.ReticleRgba(HudPalette.Standard));
        Assert.False(model.Options.ShowReconDistances);

        model.Options = null!;
        Assert.Equal(new ScopeOptions(), model.Options);
        Assert.Equal(HudPalette.Standard.Accent, model.ReticleRgba(HudPalette.Standard));
    }

    [Fact]
    public void An_equal_options_record_keeps_the_cached_layout_and_a_different_one_rebuilds_it()
    {
        var model = Shown();
        var first = model.Layout(1920, 1080);

        model.Options = new ScopeOptions();
        Assert.Same(first, model.Layout(1920, 1080));
        model.Options = new ScopeOptions(0xAABBCCDDu);
        Assert.NotSame(first, model.Layout(1920, 1080));
    }

    [Fact]
    public void Hiding_fades_out_over_a_tenth_of_a_second_and_keeps_the_layout_for_the_fade()
    {
        var model = Shown();
        model.Tick(0.15);
        Assert.Equal(1.0, model.Alpha, 6);

        model.Apply(null);
        Assert.False(model.Visible);
        model.Tick(0.05);
        Assert.Equal(0.5, model.Alpha, 6);
        model.Tick(double.NaN);
        model.Tick(double.PositiveInfinity);
        model.Tick(-1);
        Assert.Equal(0.5, model.Alpha, 6);
        Assert.Equal(1920, model.Layout(1920, 1080)!.Width);
        model.Tick(0.1);
        Assert.Equal(0.0, model.Alpha, 6);
    }

    [Fact]
    public void Syncing_a_null_source_throws_and_a_model_without_state_reports_the_crosshair_defaults()
    {
        var model = new ScopeOverlayModel();

        Assert.Throws<ArgumentNullException>(() => model.Sync(null!));
        Assert.Equal(ScopeStyle.Crosshair, model.Style);
        Assert.Equal(1.0, model.ReticleScale);
        Assert.Equal(0.0, model.Alpha);
    }

    [Theory]
    [InlineData(double.NaN)]
    public void A_nan_reticle_scale_falls_back_to_one(double scale)
    {
        var model = new ScopeOverlayModel();
        model.Apply(new ScopeState(ScopeStyle.Dot, 4, ReticleScale: scale));

        Assert.Equal(1.0, model.ReticleScale);
        Assert.Equal(ScopeStyle.Dot, model.Style);
    }

    [Theory]
    [InlineData("abcdef", 25, "...")]
    [InlineData("abcdef", 40, "a...")]
    [InlineData("ab cd ef", 60, "ab...")]
    [InlineData("abcdef", 60, "abcdef")]
    [InlineData("", 0, "")]
    public void Ellipsize_trims_the_cut_before_the_dots_and_falls_back_to_the_dots_alone(string text, double width, string expected)
    {
        Assert.Equal(expected, ScopeOverlayModel.Ellipsize(text, width, s => s.Length * 10));
    }

    [Theory]
    [InlineData(4.0, 8.0, 2.0, "x4.0", 0.0)]
    [InlineData(4.0, 0.0, 8.0, "x4.0", 0.0)]
    [InlineData(12.0, 2.0, 8.0, "x2.0 - x8.0", 1.0)]
    [InlineData(1.0, 2.0, 8.0, "x2.0 - x8.0", 0.0)]
    [InlineData(5.0, 2.0, 8.0, "x2.0 - x8.0", 0.5)]
    [InlineData(0.0, 2.0, 8.0, "", 0.0)]
    [InlineData(double.NaN, 2.0, 8.0, "", 0.0)]
    public void The_zoom_readout_needs_a_valid_range_for_the_marker_and_clamps_it(double zoom, double min, double max, string text, double marker)
    {
        var model = new ScopeOverlayModel();
        model.Apply(new ScopeState(ScopeStyle.Crosshair, zoom, min, max));

        var geometry = model.Layout(1920, 1080)!;

        Assert.Equal(text, geometry.ZoomText);
        Assert.Equal(marker, geometry.ZoomMarker, 6);
    }

    [Theory]
    [InlineData(12.5, "13 m")]
    [InlineData(12.4, "12 m")]
    [InlineData(0.0, "0 m")]
    [InlineData(-1.0, "")]
    [InlineData(double.NaN, "")]
    public void A_distance_label_rounds_half_away_from_zero_and_hides_for_a_negative_or_bad_distance(double distance, string text)
    {
        var model = new ScopeOverlayModel();
        model.Apply(new ScopeState(ScopeStyle.Crosshair, 4, Recon: new[] { new ReconMark("m", 0.5, 0.5, distance) }));

        var mark = Assert.Single(model.Layout(1920, 1080)!.Marks);

        Assert.Equal(text, mark.DistanceText);
    }

    [Fact]
    public void A_mark_without_a_distance_has_no_label()
    {
        var model = new ScopeOverlayModel();
        model.Apply(new ScopeState(ScopeStyle.Crosshair, 4, Recon: new[] { new ReconMark("m", 0.5, 0.5) }));

        Assert.Equal("", Assert.Single(model.Layout(1920, 1080)!.Marks).DistanceText);
    }
}

public sealed class SubtitleEdgeTests
{
    private static double Measure(string text) => text.Length * 10;

    [Fact]
    public void A_zero_fade_time_shows_every_line_at_full_alpha_from_the_first_frame()
    {
        var queue = new SubtitleQueue(Measure, 100) { FadeSeconds = 0 };

        queue.Add("hello", seconds: 2);

        Assert.Equal(1.0, Assert.Single(queue.Visible).Alpha);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void A_bad_requested_duration_falls_back_to_the_reading_time_with_its_floor(double seconds)
    {
        var queue = new SubtitleQueue(Measure, 100);

        queue.Add("hello", seconds: seconds);
        queue.Tick(1.19);
        Assert.Single(queue.Visible);
        queue.Tick(0.02);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void A_very_short_requested_duration_is_raised_to_half_a_second()
    {
        var queue = new SubtitleQueue(Measure, 100);

        queue.Add("hi", seconds: 0.1);
        queue.Tick(0.49);
        Assert.Single(queue.Visible);
        queue.Tick(0.02);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void The_speaker_is_trimmed_and_a_missing_speaker_is_empty()
    {
        var queue = new SubtitleQueue(Measure, 100, maxActive: 2);

        queue.Add("one", "  Piper  ");
        queue.Add("two");

        Assert.Equal(new[] { "Piper", "" }, queue.Visible.Select(v => v.Speaker));
    }
}

public sealed class LoadingEdgeTests
{
    [Fact]
    public void A_negative_or_non_finite_minimum_display_time_means_no_minimum()
    {
        foreach (var minimum in new[] { -5.0, double.NaN, double.PositiveInfinity })
        {
            var finished = 0;
            var model = new LoadingModel(minimum);
            model.Begin("Loading", onFinished: () => finished++);

            model.Complete();

            Assert.Equal(LoadingPhase.Finished, model.Phase);
            Assert.Equal(1, finished);
        }
    }

    [Fact]
    public void A_non_finite_tip_period_stops_the_rotation()
    {
        var model = new LoadingModel(1.5, double.NaN);
        model.Begin("Loading", new[] { "first", "second" });

        model.Tick(100);

        Assert.Equal("first", model.Tip);
    }

    [Fact]
    public void A_nan_progress_is_ignored_and_infinity_counts_as_complete()
    {
        var model = new LoadingModel();
        model.Begin("Loading");
        model.SetProgress(0.5);

        model.SetProgress(double.NaN);
        Assert.Equal(0.5, model.Progress);
        model.SetProgress(double.PositiveInfinity);
        Assert.Equal(1.0, model.Progress);
    }

    [Fact]
    public void Progress_complete_and_ticks_do_nothing_before_a_load_begins()
    {
        var model = new LoadingModel();

        model.SetProgress(0.5);
        model.Complete();
        model.Tick(5);

        Assert.Equal(LoadingPhase.Idle, model.Phase);
        Assert.Equal(0.0, model.Progress);
        Assert.Equal(0.0, model.Elapsed);
        Assert.False(model.WorkComplete);
    }

    [Fact]
    public void A_first_tip_past_the_end_wraps_modulo_the_tip_count()
    {
        var model = new LoadingModel();

        model.Begin("Loading", new[] { "a", "b", "c" }, firstTip: 5);

        Assert.Equal("c", model.Tip);
    }
}
