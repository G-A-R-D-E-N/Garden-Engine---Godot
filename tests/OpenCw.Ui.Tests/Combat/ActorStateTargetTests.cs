using System;
using OpenCommonwealth;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ActorStateTargetTests
{
    static ActorState Spawn(ActorProtection protection = ActorProtection.None, float? health = 100f)
    {
        var state = new ActorState(10, null) { Protection = protection };
        if (health is { } max) state.SetHealth(max);
        return state;
    }

    static EffectSpec Damage(double amount, double seconds = 0) => new("Health", EffectKind.Damage, amount, seconds);

    static EffectSpec Restore(double amount, double seconds = 0) => new("Health", EffectKind.Restore, amount, seconds);

    [Fact]
    public void HealRaisesHealthUpToTheMaximumAndRefusesDeadFullOrUnsetActors()
    {
        var state = Spawn();
        state.Damage(60f, null);
        Assert.True(state.Heal(20f));
        Assert.Equal(60f, state.Health);
        Assert.True(state.Heal(500f));
        Assert.Equal(100f, state.Health);
        Assert.False(state.Heal(5f));
        Assert.False(Spawn(health: null).Heal(5f));
        var dead = Spawn();
        dead.Kill(null);
        Assert.False(dead.Heal(5f));
        Assert.Equal(0f, dead.Health);
        var live = Spawn();
        live.Damage(10f, null);
        Assert.False(live.Heal(0f));
        Assert.False(live.Heal(float.NaN));
        Assert.Equal(90f, live.Health);
    }

    [Fact]
    public void DamageAndRestoreThroughTheApplierMoveTheActorStateHealth()
    {
        var state = Spawn();
        var target = new ActorStateTarget(state);
        var applier = new EffectApplier();
        Assert.True(applier.Apply(target, Damage(30)).Accepted);
        Assert.Equal(70f, state.Health);
        Assert.True(applier.Apply(target, Restore(10)).Accepted);
        Assert.Equal(80f, state.Health);
    }

    [Fact]
    public void ATimedDrainOnAnActorKillsItAndStopsRunning()
    {
        var state = Spawn(health: 20f);
        var killed = 0;
        state.Died += _ => killed++;
        var applier = new EffectApplier();
        applier.Apply(new ActorStateTarget(state), Damage(40, 4));
        applier.Tick(2);
        Assert.True(state.IsDead);
        Assert.Equal(1, killed);
        applier.Tick(1);
        Assert.Equal(0, applier.Active);
        Assert.Equal(1, killed);
    }

    [Fact]
    public void ProtectionRulesStillApplyThroughTheTarget()
    {
        var invulnerable = Spawn(ActorProtection.Invulnerable);
        Assert.Equal("target unchanged", new EffectApplier().Apply(new ActorStateTarget(invulnerable), Damage(50)).Reason);
        Assert.Equal(100f, invulnerable.Health);
        var essential = Spawn(ActorProtection.Essential);
        Assert.True(new EffectApplier().Apply(new ActorStateTarget(essential), Damage(500)).Accepted);
        Assert.Equal(1f, essential.Health);
        Assert.False(essential.IsDead);
    }

    [Fact]
    public void AnActorWithUnsetHealthCannotBeChanged()
    {
        var state = Spawn(health: null);
        var applier = new EffectApplier();
        Assert.Equal("target unchanged", applier.Apply(new ActorStateTarget(state), Damage(5)).Reason);
        Assert.Equal("target unchanged", applier.Apply(new ActorStateTarget(state), Restore(5)).Reason);
        Assert.Null(state.Health);
    }

    [Fact]
    public void ADeadActorIsRefusedByTheApplier()
    {
        var state = Spawn();
        state.Kill("x");
        var applier = new EffectApplier();
        Assert.Equal("target is dead", applier.Apply(new ActorStateTarget(state), Damage(5)).Reason);
        Assert.Equal("target is dead", applier.Apply(new ActorStateTarget(state), Restore(5)).Reason);
    }

    [Fact]
    public void ADamageRouteIsUsedForDamageAndRestoreBypassesIt()
    {
        var state = Spawn();
        state.Damage(50f, null);
        var routed = 0f;
        var target = new ActorStateTarget(state, amount => { routed += amount; return state.Damage(amount, null); });
        var applier = new EffectApplier();
        Assert.True(applier.Apply(target, Damage(12)).Accepted);
        Assert.Equal(12f, routed);
        Assert.Equal(38f, state.Health);
        Assert.True(applier.Apply(target, Restore(10)).Accepted);
        Assert.Equal(12f, routed);
        Assert.Equal(48f, state.Health);
    }

    [Fact]
    public void AnEssentialActorAtOneHealthReportsDamageUnchanged()
    {
        var state = Spawn(ActorProtection.Essential);
        state.Damage(99f, null);
        Assert.Equal(1f, state.Health);
        var target = new ActorStateTarget(state);
        Assert.False(target.Change("Health", -5));
        Assert.Equal("target unchanged", new EffectApplier().Apply(target, Damage(5)).Reason);
        Assert.Equal(1f, state.Health);
        Assert.False(state.IsDead);
    }

    [Fact]
    public void ARetiredNodeReadsAsDeadSoARunningEffectStopsTouchingIt()
    {
        var state = Spawn();
        var valid = true;
        var routed = 0;
        var target = new ActorStateTarget(state, _ => { routed++; return true; }, () => valid);
        var applier = new EffectApplier();
        applier.Apply(target, Damage(10, 10));
        applier.Tick(1);
        Assert.Equal(1, routed);
        valid = false;
        Assert.True(target.IsDead);
        applier.Tick(1);
        Assert.Equal(1, routed);
        Assert.Equal(0, applier.Active);
    }

    [Fact]
    public void OnlyHealthIsTrackedForAnActor()
    {
        var target = new ActorStateTarget(Spawn());
        Assert.True(target.Tracks("Health"));
        Assert.False(target.Tracks("ActionPoints"));
        Assert.Equal("actor value ActionPoints not tracked", new EffectApplier().Apply(target, new EffectSpec("ActionPoints", EffectKind.Damage, 5)).Reason);
    }
}
