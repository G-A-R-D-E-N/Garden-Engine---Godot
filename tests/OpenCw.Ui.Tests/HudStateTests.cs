using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudStateTests
{
    [Fact]
    public void HealthIsAClampedFraction()
    {
        var state = new HudState();
        state.SetHealth(50, 100);
        Assert.Equal(0.5, state.HealthFraction, 6);

        state.SetHealth(150, 100);
        Assert.Equal(100.0, state.HealthCurrent, 6);
        Assert.Equal(1.0, state.HealthFraction, 6);

        state.SetHealth(double.NaN, 100);
        Assert.Equal(100.0, state.HealthCurrent, 6);

        state.SetHealth(10, 0);
        Assert.Equal(0.0, state.HealthFraction, 6);
    }

    [Fact]
    public void FirstHealthValueNeverFlashes()
    {
        var state = new HudState();
        state.SetHealth(40, 100);
        Assert.Equal(0.0, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void LosingHealthFlashesAndDecays()
    {
        var state = new HudState();
        state.SetHealth(100, 100);
        state.SetHealth(90, 100);
        Assert.Equal(0.5, state.DamageFlashAlpha, 6);

        state.Tick(0.3);
        Assert.Equal(0.25, state.DamageFlashAlpha, 6);

        state.Tick(1);
        Assert.Equal(0.0, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void HealingDoesNotFlashAndABigHitFlashesHarder()
    {
        var state = new HudState();
        state.SetHealth(50, 100);
        state.SetHealth(80, 100);
        Assert.Equal(0.0, state.DamageFlashAlpha, 6);

        state.SetHealth(5, 100);
        Assert.Equal(1.0, state.DamageFlashAlpha, 6);
    }

    [Fact]
    public void ActionPointsAreClampedFractions()
    {
        var state = new HudState();
        state.SetActionPoints(30, 120);
        Assert.Equal(0.25, state.ActionPointsFraction, 6);
        state.SetActionPoints(-5, 120);
        Assert.Equal(0.0, state.ActionPointsFraction, 6);
    }

    [Fact]
    public void AmmoNeedsAWeaponAndNeverGoesNegative()
    {
        var state = new HudState();
        Assert.False(state.IsShown(HudElement.Ammo));

        state.SetAmmo(-3, 40);
        Assert.True(state.IsShown(HudElement.Ammo));
        Assert.Equal(0, state.Ammo.Clip);
        Assert.Equal(40, state.Ammo.Reserve);

        state.ClearAmmo();
        Assert.False(state.IsShown(HudElement.Ammo));
    }

    [Fact]
    public void HeadingWrapsIntoZeroToThreeSixty()
    {
        var state = new HudState();
        state.SetHeading(-90);
        Assert.Equal(270.0, state.CompassHeading, 6);
        state.SetHeading(725);
        Assert.Equal(5.0, state.CompassHeading, 6);
        state.SetHeading(double.PositiveInfinity);
        Assert.Equal(5.0, state.CompassHeading, 6);
    }

    [Fact]
    public void MarkersAreCopiedAndNonFiniteBearingsDropped()
    {
        var state = new HudState();
        var source = new List<CompassMarker>
        {
            new("a", 10, CompassMarkerKind.Location),
            new("b", double.NaN, CompassMarkerKind.Location),
        };
        state.SetMarkers(source);
        source.Clear();
        Assert.Single(state.Markers);
        Assert.Equal("a", state.Markers[0].Id);
    }

    [Fact]
    public void ObjectiveBannerRestartsOnlyWhenTheTextChanges()
    {
        var state = new HudState();
        state.SetObjective("  Find the key ");
        Assert.Equal("Find the key", state.ObjectiveText);
        Assert.Equal(1.0, state.ObjectiveEmphasis, 6);

        state.Tick(HudState.ObjectiveBannerSeconds - 0.5);
        Assert.Equal(0.5, state.ObjectiveEmphasis, 6);
        state.Tick(1);
        Assert.Equal(0.0, state.ObjectiveEmphasis, 6);

        state.SetObjective("Find the key");
        Assert.Equal(0.0, state.ObjectiveEmphasis, 6);

        state.SetObjective("Open the door");
        Assert.Equal(1.0, state.ObjectiveEmphasis, 6);

        state.SetObjective(null);
        Assert.False(state.IsShown(HudElement.Objective));
        Assert.Equal(0.0, state.ObjectiveEmphasis, 6);
    }

    [Fact]
    public void InteractionPromptClampsHoldProgressAndControlsVisibility()
    {
        var state = new HudState();
        Assert.False(state.IsShown(HudElement.Interaction));

        state.SetInteraction("Open", "Door", "E", 2);
        Assert.True(state.IsShown(HudElement.Interaction));
        Assert.Equal(1.0, state.Interaction!.Value.HoldProgress, 6);

        state.ClearInteraction();
        Assert.False(state.IsShown(HudElement.Interaction));
    }

    [Fact]
    public void InteractionPromptCarriesIconAndNotice()
    {
        var state = new HudState();
        state.SetInteraction("Open", "Door", "E", 0, "lock", "Locked");
        Assert.Equal(("lock", "Locked"), (state.Interaction!.Value.Icon, state.Interaction.Value.Notice));
        state.SetInteraction("Open", "Door", "E");
        Assert.Equal(("", ""), (state.Interaction!.Value.Icon, state.Interaction.Value.Notice));
    }

    [Fact]
    public void DefaultVisibilityFollowsContext()
    {
        var state = new HudState();
        Assert.True(state.IsShown(HudElement.Health));
        Assert.True(state.IsShown(HudElement.ActionPoints));
        Assert.True(state.IsShown(HudElement.Compass));
        Assert.True(state.IsShown(HudElement.Crosshair));
        Assert.True(state.IsShown(HudElement.Toasts));
        Assert.True(state.IsShown(HudElement.Subtitles));
        Assert.False(state.IsShown(HudElement.Ammo));
        Assert.False(state.IsShown(HudElement.Objective));

        state.Crosshair = CrosshairState.Hidden;
        Assert.False(state.IsShown(HudElement.Crosshair));
    }

    [Fact]
    public void ElementsCanBeSwitchedOffIndividually()
    {
        var state = new HudState();
        state.Settings.SetEnabled(HudElement.Compass, false);
        Assert.False(state.IsShown(HudElement.Compass));
        Assert.True(state.IsShown(HudElement.Health));
    }

    [Fact]
    public void HidingTheHudKeepsOnlySubtitles()
    {
        var state = new HudState { Hidden = true };
        foreach (var element in Enum.GetValues<HudElement>())
            Assert.Equal(element == HudElement.Subtitles, state.IsShown(element));

        state.Settings.SetEnabled(HudElement.Subtitles, false);
        Assert.False(state.IsShown(HudElement.Subtitles));
    }

    [Fact]
    public void HideTokensReleaseInAnyOrder()
    {
        var state = new HudState();
        int first = state.PushHidden();
        int second = state.PushHidden();
        Assert.True(state.Hidden);

        state.PopHidden(first);
        Assert.True(state.Hidden);
        state.PopHidden(second);
        Assert.False(state.Hidden);
    }

    [Fact]
    public void PoppingAnUnknownOrReleasedTokenChangesNothing()
    {
        var state = new HudState();
        int held = state.PushHidden();
        int other = state.PushHidden();
        state.PopHidden(other);
        state.PopHidden(other);
        state.PopHidden(0);
        state.PopHidden(999);
        Assert.True(state.Hidden);

        state.PopHidden(held);
        Assert.False(state.Hidden);
    }

    [Fact]
    public void TheManualHiddenFlagStaysIndependentOfTokens()
    {
        var state = new HudState();
        state.Hidden = true;
        int token = state.PushHidden();
        state.PopHidden(token);
        Assert.True(state.Hidden);

        state.Hidden = false;
        Assert.False(state.Hidden);
    }
}
