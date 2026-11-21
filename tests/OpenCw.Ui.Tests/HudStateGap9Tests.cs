using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudStateGap9Tests
{
    [Fact]
    public void AmmoStartsEmptyAndHoldsTheExactReadout()
    {
        var state = new HudState();
        Assert.Equal(new AmmoReadout(false, 0, 0, false), state.Ammo);

        state.SetAmmo(30, 90, true);
        Assert.Equal(new AmmoReadout(true, 30, 90, true), state.Ammo);

        state.SetAmmo(-1, -2);
        Assert.Equal(new AmmoReadout(true, 0, 0, false), state.Ammo);

        state.ClearAmmo();
        Assert.Equal(new AmmoReadout(false, 0, 0, false), state.Ammo);
    }

    [Fact]
    public void TheInteractionPromptHoldsTheExactTextAndClampedProgress()
    {
        var state = new HudState();
        Assert.Null(state.Interaction);

        state.SetInteraction("Open", "Door", "E", 0.25);
        Assert.Equal(new InteractionPrompt("Open", "Door", "E", 0.25), state.Interaction);

        state.SetInteraction("Take", "Key", "F");
        Assert.Equal(new InteractionPrompt("Take", "Key", "F", 0), state.Interaction);

        state.SetInteraction("Take", "Key", "F", -3);
        Assert.Equal(0.0, state.Interaction!.Value.HoldProgress, 6);

        state.SetInteraction("Take", "Key", "F", double.NaN);
        Assert.Equal(0.0, state.Interaction!.Value.HoldProgress, 6);

        state.SetInteraction("Take", "Key", "F", double.PositiveInfinity);
        Assert.Equal(0.0, state.Interaction!.Value.HoldProgress, 6);

        state.ClearInteraction();
        Assert.Null(state.Interaction);
    }

    [Fact]
    public void HealthMaxFollowsTheLastFiniteReadingAndNeverGoesNegative()
    {
        var state = new HudState();
        Assert.Equal(1.0, state.HealthMax, 6);
        Assert.Equal(1.0, state.HealthCurrent, 6);

        state.SetHealth(50, 120);
        Assert.Equal(120.0, state.HealthMax, 6);
        Assert.Equal(50.0, state.HealthCurrent, 6);

        state.SetHealth(20, double.NaN);
        Assert.Equal(120.0, state.HealthMax, 6);
        Assert.Equal(50.0, state.HealthCurrent, 6);

        state.SetHealth(20, double.PositiveInfinity);
        Assert.Equal(120.0, state.HealthMax, 6);

        state.SetHealth(40, -5);
        Assert.Equal(0.0, state.HealthMax, 6);
        Assert.Equal(0.0, state.HealthCurrent, 6);
        Assert.Equal(0.0, state.HealthFraction, 6);
    }

    [Fact]
    public void ActionPointsMaxFollowsTheLastFiniteReadingAndNeverGoesNegative()
    {
        var state = new HudState();
        Assert.Equal(1.0, state.ActionPointsMax, 6);
        Assert.Equal(1.0, state.ActionPoints, 6);

        state.SetActionPoints(60, 90);
        Assert.Equal(90.0, state.ActionPointsMax, 6);
        Assert.Equal(60.0, state.ActionPoints, 6);

        state.SetActionPoints(5, double.NaN);
        Assert.Equal(90.0, state.ActionPointsMax, 6);
        Assert.Equal(60.0, state.ActionPoints, 6);

        state.SetActionPoints(100, 90);
        Assert.Equal(90.0, state.ActionPoints, 6);

        state.SetActionPoints(10, -1);
        Assert.Equal(0.0, state.ActionPointsMax, 6);
        Assert.Equal(0.0, state.ActionPoints, 6);
        Assert.Equal(0.0, state.ActionPointsFraction, 6);
    }

    [Fact]
    public void ActionPointsDimOnlyWhenFullAndIdleForTheDelay()
    {
        var state = new HudState();
        state.SetActionPoints(90, 90);
        state.Tick(HudState.ActionPointsDimDelaySeconds - 0.1);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.Tick(0.1);
        Assert.Equal(HudState.ActionPointsDimAlpha, state.ActionPointsAlpha, 6);

        state.SetActionPoints(89, 90);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.Tick(10);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.SetActionPoints(90, 90);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);
        state.Tick(HudState.ActionPointsDimDelaySeconds);
        Assert.Equal(HudState.ActionPointsDimAlpha, state.ActionPointsAlpha, 6);

        state.SetActionPoints(90, 90);
        Assert.Equal(HudState.ActionPointsDimAlpha, state.ActionPointsAlpha, 6);

        state.SetActionPoints(0, 0);
        state.Tick(10);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);
    }

    [Fact]
    public void ShrinkingTheMaximumToTheCurrentValueRestartsTheDimDelay()
    {
        var state = new HudState();
        state.SetActionPoints(50, 100);
        state.Tick(10);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.SetActionPoints(50, 50);
        Assert.Equal(1.0, state.ActionPointsAlpha, 6);

        state.Tick(HudState.ActionPointsDimDelaySeconds);
        Assert.Equal(HudState.ActionPointsDimAlpha, state.ActionPointsAlpha, 6);
    }

    [Theory]
    [InlineData(0.0, 0.3)]
    [InlineData(0.05, 0.4)]
    [InlineData(0.1, 0.5)]
    [InlineData(0.35, 1.0)]
    [InlineData(5.0, 1.0)]
    [InlineData(-4.0, 0.3)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.3)]
    public void AFlashStartsAtTheIntensityForTheFractionLost(double fraction, double alpha)
    {
        var state = new HudState();

        state.FlashDamage(fraction);

        Assert.Equal(alpha, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void AFlashFadesLinearlyOverTheFlashDuration()
    {
        var state = new HudState();
        state.FlashDamage(0.35);

        state.Tick(HudState.DamageFlashSeconds / 2);
        Assert.Equal(0.5, state.DamageFlashAlpha, 6);

        state.Tick(HudState.DamageFlashSeconds / 2);
        Assert.Equal(0.0, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void AWeakerFlashNeverLowersTheAlphaOfTheOneStillShowing()
    {
        var state = new HudState();
        state.FlashDamage(0.35);
        state.Tick(0.3);
        Assert.Equal(0.5, state.DamageFlashAlpha, 6);

        state.FlashDamage(0);

        Assert.Equal(0.5, state.DamageFlashAlpha, 6);
        state.Tick(0.3);
        Assert.Equal(0.25, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void ANonNumberDamageFlashIsIgnoredAndCannotPoisonLaterFlashes()
    {
        var state = new HudState();

        state.FlashDamage(double.NaN);
        Assert.Equal(0.0, state.DamageFlashAlpha, 6);

        state.FlashDamage(0.1);
        Assert.Equal(0.5, state.DamageFlashAlpha, 6);

        state.FlashDamage(double.NaN);
        Assert.Equal(0.5, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void ADropInHealthFlashesByTheFractionOfTheOldMaximum()
    {
        var state = new HudState();
        state.SetHealth(200, 200);

        state.SetHealth(150, 200);

        Assert.Equal(0.8, state.DamageFlashAlpha, 6);
    }
}
