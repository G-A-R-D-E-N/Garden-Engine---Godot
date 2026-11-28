using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudInstrumentsTests
{
    [Fact]
    public void Radiation_is_a_clamped_fraction_with_a_rounded_percent_and_bad_input_keeps_the_last_value()
    {
        var i = new HudInstruments();
        Assert.Equal(0, i.RadiationPercent);

        i.SetRadiation(0.25);
        Assert.Equal(25, i.RadiationPercent);
        i.SetRadiation(double.NaN);
        i.SetRadiation(-0.5);
        i.SetRadiation(double.PositiveInfinity);
        Assert.Equal(0.25, i.Radiation, 6);

        i.SetRadiation(3);
        Assert.Equal(1.0, i.Radiation, 6);
        Assert.Equal(100, i.RadiationPercent);
        i.SetRadiation(0);
        Assert.Equal(0, i.RadiationPercent);
    }

    [Fact]
    public void Ammo_counts_are_three_zero_padded_digits_capped_at_999()
    {
        Assert.Equal("007", HudText.Ammo(7));
        Assert.Equal("999", HudText.Ammo(1234));
        Assert.Equal("000", HudText.Ammo(-4));
        Assert.Equal("000", HudText.Ammo(0));
    }

    [Fact]
    public void Ten_effects_show_eight_and_blank_entries_and_line_breaks_are_cleaned()
    {
        var i = new HudInstruments();
        i.SetEffects(Enumerable.Range(1, 10).Select(n => $"Effect {n}").ToArray());
        Assert.Equal(8, i.Effects.Count);
        Assert.Equal("Effect 8", i.Effects[^1]);

        i.SetEffects(new[] { "", "  ", "A\nB" });
        Assert.Equal(new[] { "A B" }, i.Effects);

        i.SetEffects(null);
        Assert.Empty(i.Effects);
    }

    [Fact]
    public void Status_chips_cap_at_four_keep_insertion_order_and_replace_by_id()
    {
        var i = new HudInstruments();
        Assert.True(i.SetStatus("light", "Light on"));
        i.SetStatus("b", "B");
        i.SetStatus("c", "C");
        i.SetStatus("d", "D");

        Assert.False(i.SetStatus("e", "E"));
        Assert.True(i.SetStatus("b", "B2"));

        Assert.Equal(new[] { "light", "b", "c", "d" }, i.Chips.Select(c => c.Id).ToArray());
        Assert.Equal("B2", i.Chips[1].Text);
        Assert.True(i.ClearStatus("light"));
        Assert.False(i.ClearStatus("light"));
        Assert.True(i.SetStatus("e", "E"));
        Assert.Equal("e", i.Chips[^1].Id);
        Assert.False(i.SetStatus("", "x"));
    }

    [Fact]
    public void The_enemy_bar_fades_in_over_150_ms_and_out_over_300_ms_and_keeps_its_name_while_fading()
    {
        var i = new HudInstruments();
        i.SetEnemy("Raider", 0.5);
        Assert.Equal(0.0, i.EnemyAlpha, 6);
        i.Tick(0.075);
        Assert.Equal(0.5, i.EnemyAlpha, 6);
        i.Tick(0.1);
        Assert.Equal(1.0, i.EnemyAlpha, 6);

        i.ClearEnemy();
        i.Tick(0.15);
        Assert.Equal(0.5, i.EnemyAlpha, 6);
        Assert.Equal("Raider", i.EnemyName);
        i.Tick(0.2);
        Assert.Equal(0.0, i.EnemyAlpha, 6);
    }

    [Fact]
    public void Replacing_the_enemy_updates_in_place_without_a_fade_and_resets_details()
    {
        var i = new HudInstruments();
        i.SetEnemy("Raider", 1);
        i.SetEnemyDetails(12, true);
        i.Tick(1);

        i.SetEnemy("Raider", 0.4);
        Assert.Equal(1.0, i.EnemyAlpha, 6);
        Assert.Equal(0.4, i.EnemyHealth, 6);
        Assert.Equal(12, i.EnemyLevel);
        Assert.True(i.EnemyLegendary);

        i.SetEnemy("Mole rat", 2);
        Assert.Equal(1.0, i.EnemyAlpha, 6);
        Assert.Equal(1.0, i.EnemyHealth, 6);
        Assert.Null(i.EnemyLevel);
        Assert.False(i.EnemyLegendary);

        i.SetEnemy("Mole rat", double.NaN);
        Assert.Equal(1.0, i.EnemyHealth, 6);
    }

    [Fact]
    public void The_sneak_gap_steps_toward_the_target_and_snap_sets_it_at_once()
    {
        var i = new HudInstruments();
        i.SetSneak("HIDDEN", 100);
        Assert.Equal(0.0, i.SneakGap, 6);

        i.Tick(0.033);
        Assert.Equal(4.0, i.SneakGap, 6);
        for (int n = 0; n < 10; n++)
            i.Tick(0.033);
        Assert.Equal(41.0, i.SneakGap, 6);
        i.Tick(10);
        Assert.Equal(HudInstruments.MaxSneakGap, i.SneakGap, 6);

        i.SetSneak("DANGER", 0, snap: true);
        Assert.Equal(0.0, i.SneakGap, 6);
        Assert.Equal("DANGER", i.SneakText);
    }

    [Fact]
    public void A_small_sneak_change_uses_a_one_unit_step_and_empty_text_hides_it()
    {
        var i = new HudInstruments();
        i.SetSneak("HIDDEN", 5, snap: true);
        i.SetSneak("HIDDEN", 10);
        i.Tick(0.033);
        Assert.Equal(7.0, i.SneakGap, 6);

        i.SetSneak("", 50);
        Assert.Equal(string.Empty, i.SneakText);
        Assert.Equal(60.0, i.SneakGap, 6);
    }

    [Fact]
    public void The_critical_meter_flashes_between_full_and_40_percent_only_while_a_charge_is_ready()
    {
        var i = new HudInstruments();
        Assert.False(i.CriticalVisible);

        i.SetCritical(0.5, 0);
        i.Tick(0.1);
        Assert.Equal(1.0, i.CriticalAlpha, 6);

        i.SetCritical(1, 5);
        Assert.Equal(HudInstruments.MaxCharges, i.CriticalCharges);
        double low = 1, high = 0;
        for (int n = 0; n < 50; n++)
        {
            i.Tick(0.01);
            low = System.Math.Min(low, i.CriticalAlpha);
            high = System.Math.Max(high, i.CriticalAlpha);
        }
        Assert.InRange(low, 0.4, 0.45);
        Assert.InRange(high, 0.95, 1.0);

        i.ClearCritical();
        Assert.False(i.CriticalVisible);
        Assert.Equal(1.0, i.CriticalAlpha, 6);
    }

    [Fact]
    public void The_ap_preview_keeps_clamped_segments_and_null_clears_it()
    {
        var i = new HudInstruments();
        i.SetActionPointPreview(40, new[] { 0.2, double.NaN, 1.5 });

        Assert.Equal(40.0, i.ActionPointPreview);
        Assert.Equal(new[] { 0.2, 1.0 }, i.PreviewSegments);
        i.SetActionPointPreview(null, null);
        Assert.Null(i.ActionPointPreview);
        Assert.Empty(i.PreviewSegments);
    }

    [Fact]
    public void Hiding_a_group_hides_only_its_elements_keeps_values_and_survives_hud_hidden_toggles()
    {
        var state = new HudState();
        state.SetHealth(50, 100);
        state.Instruments.SetGroupVisible(HudGroup.Compass, false);

        Assert.False(state.IsShown(HudElement.Compass));
        Assert.True(state.IsShown(HudElement.Health));
        Assert.True(state.IsShown(HudElement.Toasts));
        state.Hidden = true;
        state.Hidden = false;
        Assert.False(state.IsShown(HudElement.Compass));
        Assert.Equal(0.5, state.HealthFraction, 6);

        state.Instruments.SetGroupVisible(HudGroup.Compass, true);
        Assert.True(state.IsShown(HudElement.Compass));
    }

    [Fact]
    public void Action_points_dim_to_half_two_seconds_after_reaching_full_and_brighten_on_change()
    {
        var state = new HudState();
        state.SetActionPoints(60, 100);
        state.Tick(5);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.SetActionPoints(100, 100);
        state.Tick(1.9);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);
        state.Tick(0.2);
        Assert.Equal(0.5, state.ActionPointsAlpha, 6);

        state.SetActionPoints(100, 100);
        Assert.Equal(0.5, state.ActionPointsAlpha, 6);
        state.SetActionPoints(90, 100);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);
    }
}
