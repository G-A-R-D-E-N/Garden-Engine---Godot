using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ExplosionDeliveryTests
{
    static ExplosionInfo Explosion(float damage = 300f, float force = 100f, float inner = 100f, float outer = 500f) =>
        new(damage, force, inner, outer, 0);

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(50.0, 1.0)]
    [InlineData(100.0, 1.0)]
    [InlineData(300.0, 0.5)]
    [InlineData(400.0, 0.25)]
    [InlineData(499.0, 0.0025)]
    public void FalloffRunsLinearlyFromTheInnerToTheOuterRadius(double distance, double expected)
    {
        Assert.Equal(expected, ExplosionDelivery.FalloffAt(Explosion(), distance), 9);
    }

    [Theory]
    [InlineData(500.0)]
    [InlineData(900.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OutsideTheOuterRadiusOrAnUnusableDistanceIsNoFalloff(double distance)
    {
        Assert.Equal(0.0, ExplosionDelivery.FalloffAt(Explosion(), distance));
    }

    [Fact]
    public void AnExplosionWithNoUsableOuterRadiusReachesNothing()
    {
        Assert.Equal(0.0, ExplosionDelivery.FalloffAt(Explosion(outer: 0f), 0));
        Assert.Equal(0.0, ExplosionDelivery.FalloffAt(Explosion(outer: -5f), 0));
        Assert.Equal(0.0, ExplosionDelivery.FalloffAt(Explosion(outer: float.NaN), 0));
    }

    [Fact]
    public void ANegativeInnerRadiusIsTreatedAsTheCenter()
    {
        Assert.Equal(1.0, ExplosionDelivery.FalloffAt(Explosion(inner: -20f, outer: 100f), 0));
        Assert.Equal(0.5, ExplosionDelivery.FalloffAt(Explosion(inner: -20f, outer: 100f), 50), 9);
    }

    [Fact]
    public void TargetsAtOrBeyondTheOuterRadiusAreNotHit()
    {
        var targets = new List<ExplosionTarget> { new ExplosionTarget("edge", 500), new ExplosionTarget("far", 900), new ExplosionTarget("bad", double.NaN) };
        Assert.Empty(ExplosionDelivery.ResolveHits(Explosion(), targets));
    }

    [Fact]
    public void AnExplosionWithoutTargetsHitsNothing()
    {
        Assert.Empty(ExplosionDelivery.ResolveHits(Explosion(), []));
    }

    [Fact]
    public void AnUnarmoredHitCarriesTheScaledDamageForceAndEffect()
    {
        var hit = Assert.Single(ExplosionDelivery.ResolveHits(Explosion(), [new ExplosionTarget("near", 0)]));

        Assert.Equal("near", hit.TargetId);
        Assert.Equal(1.0, hit.Falloff);
        Assert.Equal(300.0, hit.MitigatedDamage, 9);
        Assert.Equal(100.0, hit.Force, 9);
        Assert.Equal(new EffectSpec(VitalsEffects.HealthValue, EffectKind.Damage, 300.0), hit.Effect);
    }

    [Fact]
    public void AHitCarriesTheMitigatedDamageOfItsOwnResistance()
    {
        var hits = ExplosionDelivery.ResolveHits(Explosion(), [new ExplosionTarget("bare", 300, 0), new ExplosionTarget("plated", 300, 400)]);

        Assert.Equal(2, hits.Count);
        var bare = hits[0];
        var plated = hits[1];
        Assert.Equal(0.5, bare.Falloff, 9);
        Assert.Equal(150.0, bare.MitigatedDamage, 9);
        Assert.Equal(50.0, bare.Force, 9);
        Assert.Equal(new EffectSpec(VitalsEffects.HealthValue, EffectKind.Damage, 150.0), bare.Effect);
        Assert.Equal(0.5, plated.Falloff, 9);
        Assert.Equal(ArmorMitigation.Mitigate(150.0, 400), plated.MitigatedDamage, 12);
        Assert.True(plated.MitigatedDamage < bare.MitigatedDamage);
        Assert.Equal(plated.MitigatedDamage, plated.Effect!.Amount, 12);
        Assert.Equal(50.0, plated.Force, 9);
    }

    [Fact]
    public void AHitThatTakesNoDamageCarriesNoEffect()
    {
        var hit = Assert.Single(ExplosionDelivery.ResolveHits(Explosion(damage: 0f), [new ExplosionTarget("near", 0)]));

        Assert.Equal(0.0, hit.MitigatedDamage);
        Assert.Null(hit.Effect);
        Assert.Equal(100.0, hit.Force, 9);
    }

    [Fact]
    public void AMineArmsOnlyAfterItsDelay()
    {
        var mine = new ProximityMineState(new ProximityMineConfig());

        Assert.False(mine.Advance(0.5, 100));
        Assert.False(mine.Armed);
        Assert.Equal(0.5, mine.ElapsedSeconds, 9);
        Assert.False(mine.Advance(0.25, 100));
        Assert.True(mine.Armed);
        Assert.False(mine.Detonated);
    }

    [Fact]
    public void AnArmedMineDetonatesOnceWhenATargetIsInsideItsRadius()
    {
        var mine = new ProximityMineState(new ProximityMineConfig(ArmDelaySeconds: 0.75, TriggerRadius: 2.0));

        Assert.False(mine.Advance(1.0, 5.0));
        Assert.True(mine.Armed);
        Assert.False(mine.Detonated);
        Assert.True(mine.Advance(0.1, 2.0));
        Assert.True(mine.Detonated);
        Assert.False(mine.Advance(0.1, 0.0));
    }

    [Fact]
    public void AnUnarmedMineIgnoresATargetOnTopOfItThenDetonatesOnTheArmingTick()
    {
        var mine = new ProximityMineState(new ProximityMineConfig());

        Assert.False(mine.Advance(0.5, 0));
        Assert.False(mine.Detonated);
        Assert.True(mine.Advance(0.25, 0));
        Assert.True(mine.Armed);
        Assert.True(mine.Detonated);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.0)]
    [InlineData(2.1)]
    public void AnArmedMineIgnoresAnUnusableOrDistantTarget(double distance)
    {
        var mine = new ProximityMineState(new ProximityMineConfig());

        Assert.False(mine.Advance(1.0, distance));
        Assert.True(mine.Armed);
        Assert.False(mine.Detonated);
    }

    [Fact]
    public void AnUnusableDeltaNeverArmsTheMine()
    {
        var mine = new ProximityMineState(new ProximityMineConfig());

        Assert.False(mine.Advance(double.NaN, 0));
        Assert.False(mine.Advance(-1, 0));
        Assert.False(mine.Advance(double.PositiveInfinity, 0));
        Assert.False(mine.Armed);
        Assert.Equal(0.0, mine.ElapsedSeconds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void AnUnusableArmDelayArmsImmediately(double delay)
    {
        var mine = new ProximityMineState(new ProximityMineConfig(ArmDelaySeconds: delay));

        Assert.False(mine.Advance(0, 100));
        Assert.True(mine.Armed);
        Assert.False(mine.Detonated);
    }

    [Fact]
    public void AMineWithNoTriggerRadiusOnlySeesATargetAtItsCenter()
    {
        var mine = new ProximityMineState(new ProximityMineConfig(ArmDelaySeconds: 0, TriggerRadius: -1));

        Assert.False(mine.Advance(0, 0.01));
        Assert.True(mine.Advance(0, 0));
        Assert.True(mine.Detonated);
    }

    [Fact]
    public void ADetonatedMineStaysDetonated()
    {
        var mine = new ProximityMineState(new ProximityMineConfig(ArmDelaySeconds: 0));

        Assert.True(mine.Advance(0, 0));
        Assert.True(mine.Detonated);
        Assert.False(mine.Advance(1, 0));
    }
}
