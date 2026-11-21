using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudLiveTests
{
    [Fact]
    public void SneakStaysClearUntilTheNpcsNoticeAndFollowsAwareness()
    {
        Assert.Equal(("", 0.0), HudLive.Sneak(false, true, 1));
        Assert.Equal((HudLive.Hidden, 0.0), HudLive.Sneak(true, false, 0));
        Assert.Equal((HudLive.Caution, 50.0), HudLive.Sneak(true, false, 0.5));
        Assert.Equal((HudLive.Detected, 100.0), HudLive.Sneak(true, true, 0.1));
        Assert.Equal((HudLive.Detected, 100.0), HudLive.Sneak(true, false, 1));
        Assert.Equal((HudLive.Hidden, 0.0), HudLive.Sneak(true, false, double.NaN));
    }

    [Fact]
    public void TheSneakReadoutReachesTheInstrumentsAndClearsOnStanding()
    {
        var instruments = new HudInstruments();
        HudLive.ApplySneak(instruments, true, false, 0.5);
        Assert.Equal(HudLive.Caution, instruments.SneakText);
        HudLive.ApplySneak(instruments, false, false, 0.5);
        Assert.Equal("", instruments.SneakText);
    }

    [Fact]
    public void HealthStaysHiddenUntilItIsKnownThenFollowsAndClears()
    {
        var state = new HudState { RequireHealthValue = true };
        Assert.False(state.IsShown(HudElement.Health));
        HudLive.ApplyHealth(state, 40, 100);
        Assert.True(state.IsShown(HudElement.Health));
        Assert.Equal(0.4, state.HealthFraction, 6);
        HudLive.ApplyHealth(state, null, null);
        Assert.False(state.IsShown(HudElement.Health));
        HudLive.ApplyHealth(state, 10, 0);
        Assert.False(state.IsShown(HudElement.Health));
    }

    [Fact]
    public void ApplyRadiationFollowsTheFractionAndClearsWhenUnknown()
    {
        var instruments = new HudInstruments();
        HudLive.ApplyRadiation(instruments, 0.25);
        Assert.Equal(25, instruments.RadiationPercent);
        HudLive.ApplyRadiation(instruments, null);
        Assert.Equal(0, instruments.RadiationPercent);
        HudLive.ApplyRadiation(instruments, 0.5);
        HudLive.ApplyRadiation(instruments, double.NaN);
        Assert.Equal(0, instruments.RadiationPercent);
    }

    [Fact]
    public void HealthIsShownByDefaultWhenNotRequired()
    {
        Assert.True(new HudState().IsShown(HudElement.Health));
    }

    [Fact]
    public void TheEnemyBarShowsOnlyAKnownLivingTargetAndClearsWhenLost()
    {
        var instruments = new HudInstruments();
        Assert.True(HudLive.ApplyEnemy(instruments, "Raider", 30, 60, 4));
        Assert.Equal("Raider", instruments.EnemyName);
        Assert.Equal(0.5, instruments.EnemyHealth, 6);
        Assert.Equal(4, instruments.EnemyLevel);
        instruments.Tick(1);
        Assert.True(instruments.EnemyAlpha > 0);
        Assert.False(HudLive.ApplyEnemy(instruments, "Raider", null, 60, 4));
        Assert.False(HudLive.ApplyEnemy(instruments, "Raider", 0, 60, 4));
        Assert.False(HudLive.ApplyEnemy(instruments, "Raider", 30, 0, 4));
        Assert.False(HudLive.ApplyEnemy(instruments, "", 30, 60, 4));
        instruments.Tick(1);
        Assert.Equal(0, instruments.EnemyAlpha, 6);
    }

    [Fact]
    public void SwitchingTargetsReplacesTheNameAndDetails()
    {
        var instruments = new HudInstruments();
        HudLive.ApplyEnemy(instruments, "Raider", 30, 60, 4);
        HudLive.ApplyEnemy(instruments, "Ghoul", 10, 20, null);
        Assert.Equal("Ghoul", instruments.EnemyName);
        Assert.Null(instruments.EnemyLevel);
    }

    [Fact]
    public void AnAppliedBuffShowsInTheStatusAndHudListsAndExpires()
    {
        var vitals = new PlayerVitals(100, 100, 60, 60);
        var source = new LivePlayerStatus(vitals: vitals);
        Assert.Empty(source.Effects);
        Assert.Empty(LiveBuffEffects.HudLines(vitals));
        vitals.AddBuff(VitalsEffects.HealthValue, 50, 30);
        var group = Assert.Single(source.Effects);
        Assert.Equal(LiveBuffEffects.SourceName, group.Name);
        var line = Assert.Single(group.Lines);
        Assert.Equal("Max Health", line.Name);
        Assert.Equal(50, line.Value);
        Assert.Equal(30, line.Duration);
        Assert.Equal("Max Health +50 30s", Assert.Single(LiveBuffEffects.HudLines(vitals)));
        Assert.True(new PlayerStatusModel(source).HasEffects);
        vitals.Tick(31);
        Assert.Empty(source.Effects);
        Assert.Empty(LiveBuffEffects.HudLines(vitals));
        Assert.False(new PlayerStatusModel(source).HasEffects);
    }

    [Fact]
    public void BuffModifiersLandOnTheMatchingSpecialRowOnly()
    {
        var vitals = new PlayerVitals(100, 100, 60, 60);
        vitals.AddBuff("Strength", 2, 60);
        var rows = new[] { new SpecialEntry("Strength", "Strength", 5), new SpecialEntry("Luck", "Luck", 4) };
        var shown = LiveBuffEffects.WithModifiers(rows, vitals);
        Assert.Equal(2, shown.Single(row => row.Id == "Strength").Modifier);
        Assert.Equal(0, shown.Single(row => row.Id == "Luck").Modifier);
        Assert.Same(rows, LiveBuffEffects.WithModifiers(rows, null));
    }

    [Theory]
    [InlineData(0, -10, 0)]
    [InlineData(10, 0, 90)]
    [InlineData(0, 10, 180)]
    [InlineData(-10, 0, 270)]
    public void BearingsAreDegreesClockwiseFromNorthWithNorthAsMinusZ(double dx, double dz, double expected)
    {
        Assert.Equal(expected, HudLive.Bearing(dx, dz), 6);
    }

    [Fact]
    public void MarkersKeepTheNearestActorsAndClearWhenNoneRemain()
    {
        var state = new HudState();
        var actors = Enumerable.Range(1, 12).Select(i => ("npc" + i, (double)i, 0.0, CompassMarkerKind.Hostile)).ToList();
        HudLive.ApplyMarkers(state, actors);
        Assert.Equal(HudLive.MaxMarkers, state.Markers.Count);
        Assert.Equal("npc1", state.Markers[0].Id);
        Assert.All(state.Markers, m => Assert.Equal(90, m.Bearing, 6));
        HudLive.ApplyMarkers(state, new List<(string, double, double, CompassMarkerKind)> { ("ally", 0, -5, CompassMarkerKind.Friendly), ("bad", double.NaN, 0, CompassMarkerKind.Hostile) });
        var only = Assert.Single(state.Markers);
        Assert.Equal(CompassMarkerKind.Friendly, only.Kind);
        HudLive.ApplyMarkers(state, new List<(string, double, double, CompassMarkerKind)>());
        Assert.Empty(state.Markers);
    }

    [Fact]
    public void ObjectiveWatchSeedsSilentlyThenReportsOnlyAStageChangeWithText()
    {
        var watch = new ObjectiveWatch();
        Assert.Null(watch.Observe([("q1", 10, "Find the key")]));
        Assert.Null(watch.Observe([("q1", 10, "Find the key")]));
        Assert.Equal("Open the door", watch.Observe([("q1", 20, "Open the door")]));
        Assert.Null(watch.Observe([("q1", 30, null)]));
        Assert.Equal("New job", watch.Observe([("q1", 30, null), ("q2", 5, "New job")]));
        watch.Reset();
        Assert.Null(watch.Observe([("q2", 6, "Seeded again")]));
    }
}
