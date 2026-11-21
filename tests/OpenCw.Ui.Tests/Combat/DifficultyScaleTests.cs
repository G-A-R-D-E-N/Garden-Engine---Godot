using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

[Collection("PlayerHealth")]
public class DifficultyScaleTests
{
    [Theory]
    [InlineData("easy", 0.5)]
    [InlineData("normal", 1)]
    [InlineData("hard", 1.5)]
    [InlineData("survival", 2)]
    [InlineData("nonsense", 1)]
    [InlineData(null, 1)]
    public void Each_difficulty_level_scales_damage_taken(string? level, double expected)
    {
        Assert.Equal(expected, DifficultyScale.DamageTaken(level));
    }

    [Fact]
    public void Every_difficulty_choice_in_the_settings_has_a_scale()
    {
        var def = System.Linq.Enumerable.Single(DefaultSettingDefs.All, d => d.Id == "gameplay.difficulty");
        foreach (var choice in def.Choices)
            Assert.True(DifficultyScale.DamageTaken(choice.Id) > 0);
    }

    [Fact]
    public void Damage_is_scaled_but_healing_is_not()
    {
        var vitals = new PlayerVitals(100, 50, 50, 50) { DamageScale = 2 };

        vitals.AddHealth(-10);
        Assert.Equal(30, vitals.Health);

        vitals.AddHealth(10);
        Assert.Equal(40, vitals.Health);
    }

    [Fact]
    public void Scaled_damage_can_kill_and_Died_fires_once()
    {
        var vitals = new PlayerVitals(100, 30, 50, 50) { DamageScale = 2 };
        var died = 0;
        vitals.Died += () => died++;

        vitals.AddHealth(-20);
        vitals.AddHealth(-20);

        Assert.True(vitals.IsDead);
        Assert.Equal(1, died);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void A_bad_scale_falls_back_to_one(double scale)
    {
        var vitals = new PlayerVitals(100, 50, 50, 50) { DamageScale = scale };

        vitals.AddHealth(-10);

        Assert.Equal(40, vitals.Health);
    }

    [Fact]
    public void The_player_scale_reaches_the_current_vitals_and_later_attached_ones()
    {
        try
        {
            var first = PlayerHealth.Attach(100, 100, 50, 50)!;
            PlayerHealth.DamageScale = 0.5;
            first.AddHealth(-20);
            Assert.Equal(90, first.Health);

            var second = PlayerHealth.Attach(100, 100, 50, 50)!;
            second.AddHealth(-20);
            Assert.Equal(90, second.Health);
        }
        finally
        {
            PlayerHealth.DamageScale = 1;
            PlayerHealth.Detach();
        }
    }

    [Fact]
    public void Hint_toasts_are_dropped_while_hints_are_off_and_other_kinds_still_show()
    {
        var queue = new ToastQueue { HintsEnabled = false };

        Assert.Equal(-1, queue.Enqueue("Press E", ToastKind.Hint));
        Assert.True(queue.Enqueue("Out of ammo", ToastKind.Warning) > 0);

        queue.Tick(0.1);
        Assert.Equal(new[] { "Out of ammo" }, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(queue.Visible, v => v.Text)));
    }

    [Fact]
    public void Turning_hints_off_clears_hints_already_queued_and_turning_them_on_allows_new_ones()
    {
        var queue = new ToastQueue();
        queue.Enqueue("Press E", ToastKind.Hint);
        queue.Enqueue("Saved", ToastKind.Info);
        queue.Tick(0.1);

        queue.HintsEnabled = false;
        Assert.DoesNotContain(queue.Visible, v => v.Kind == ToastKind.Hint);
        Assert.Contains(queue.Visible, v => v.Text == "Saved");

        queue.HintsEnabled = true;
        Assert.True(queue.Enqueue("Press E", ToastKind.Hint) > 0);
    }

    [Fact]
    public void The_settings_map_to_the_effects_the_applier_consumes()
    {
        Assert.Equal(new SetDifficulty("hard"), Assert.Single(SettingsEffects.Map("gameplay.difficulty", SettingValue.Choice("hard"))));
        Assert.Equal(new SetTutorialHints(false), Assert.Single(SettingsEffects.Map("gameplay.hints", SettingValue.Toggle(false))));
    }
}
