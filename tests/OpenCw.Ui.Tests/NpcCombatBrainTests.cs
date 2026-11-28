using System;
using System.Collections.Generic;
using OpenCommonwealth;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class NpcCombatBrainTests
{
    private static readonly CombatTunables Tunables = new(AttackRange: 2f, AttackDamage: 5f, AttackCooldown: 1.5f, AlertSeconds: 3f, SearchSeconds: 10f, LoseSeconds: 2f, FleeBelowHealth: 0.15f, FleeSafeDistance: 20f, FleeMaxSeconds: 20f);

    private static NpcCombatBrain Brain(out List<(CombatPhase, CombatPhase)> changes)
    {
        var brain = new NpcCombatBrain(Tunables);
        var seen = new List<(CombatPhase, CombatPhase)>();
        brain.PhaseChanged += (previous, next) => seen.Add((previous, next));
        changes = seen;
        return brain;
    }

    private static ActorState Victim(float health = 100f)
    {
        var state = new ActorState(1, null);
        state.SetHealth(health);
        return state;
    }

    private static ActorStateTarget Target(ActorState state, string killer = "npc") => new(state, amount => state.Damage(amount, killer));

    private static CombatSense Sense(string? target = "player", bool hostile = true, bool visible = true, bool aware = false, float distance = 10f, float? health = 1f) =>
        new(target, hostile, visible, aware, distance, health);

    [Fact]
    public void NothingHappensWhileNobodyIsAwareOrTargeted()
    {
        var brain = Brain(out var changes);
        var victim = Victim();

        var step = brain.Step(1f, Sense(target: null, hostile: false, visible: false), Target(victim));

        Assert.Equal(CombatPhase.Idle, step.Phase);
        Assert.False(step.Hit);
        Assert.Empty(changes);
    }

    [Fact]
    public void AwarenessWithoutATargetGoesAlertAndFadesBackToIdle()
    {
        var brain = Brain(out var changes);

        Assert.Equal(CombatPhase.Alert, brain.Step(0.25f, Sense(target: null, hostile: false, visible: false, aware: true), null).Phase);
        Assert.False(brain.Engaged);
        Assert.Equal(CombatPhase.Alert, brain.Step(2f, Sense(target: null, hostile: false, visible: false), null).Phase);
        Assert.Equal(CombatPhase.Idle, brain.Step(1.5f, Sense(target: null, hostile: false, visible: false), null).Phase);
        Assert.Equal(new[] { (CombatPhase.Idle, CombatPhase.Alert), (CombatPhase.Alert, CombatPhase.Idle) }, changes);
    }

    [Fact]
    public void ASeenEnemyOutOfRangeIsPursuedAndOneInRangeIsAttacked()
    {
        var brain = Brain(out var changes);
        var victim = Victim();

        Assert.Equal(CombatPhase.Pursue, brain.Step(0.1f, Sense(distance: 10f), Target(victim)).Phase);
        Assert.Equal("player", brain.Target);
        Assert.True(brain.Engaged);
        Assert.Equal(CombatPhase.Pursue, brain.Step(0.1f, Sense(distance: 2.01f), Target(victim)).Phase);
        Assert.Equal(CombatPhase.Attack, brain.Step(0.1f, Sense(distance: 2f), Target(victim)).Phase);
        Assert.Equal(CombatPhase.Pursue, brain.Step(0.1f, Sense(distance: 3f), Target(victim)).Phase);
        Assert.Equal(new[] { (CombatPhase.Idle, CombatPhase.Pursue), (CombatPhase.Pursue, CombatPhase.Attack), (CombatPhase.Attack, CombatPhase.Pursue) }, changes);
    }

    [Fact]
    public void AnAttackDamagesTheVictimOncePerCooldown()
    {
        var brain = Brain(out _);
        var victim = Victim();
        var target = Target(victim);

        Assert.True(brain.Step(0.5f, Sense(distance: 1f), target).Hit);
        Assert.Equal(95f, victim.Health);
        Assert.False(brain.Step(0.5f, Sense(distance: 1f), target).Hit);
        Assert.False(brain.Step(0.5f, Sense(distance: 1f), target).Hit);
        Assert.Equal(95f, victim.Health);
        Assert.True(brain.Step(0.5f, Sense(distance: 1f), target).Hit);
        Assert.Equal(90f, victim.Health);
    }

    [Fact]
    public void RepeatedHitsKillTheVictimAndCreditTheAttacker()
    {
        var brain = Brain(out _);
        var victim = Victim(10f);
        var target = Target(victim, "npc-ref");

        brain.Step(0.5f, Sense(distance: 1f), target);
        brain.Step(1.5f, Sense(distance: 1f), target);

        Assert.True(victim.IsDead);
        Assert.Equal("npc-ref", victim.Killer);
        Assert.False(brain.Step(1.5f, Sense(distance: 1f), target).Hit);
        Assert.Equal(0f, victim.Health);
    }

    [Fact]
    public void ADeadVictimOrNoVictimTakesNoHit()
    {
        var brain = Brain(out _);
        var victim = Victim();
        victim.Kill("someone");

        Assert.False(brain.Step(0.5f, Sense(distance: 1f), Target(victim)).Hit);
        Assert.Equal(CombatPhase.Attack, brain.Phase);
        Assert.False(brain.Step(2f, Sense(distance: 1f), null).Hit);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(10f)]
    public void ANonHostileTargetIsNeverPursuedOrAttacked(float distance)
    {
        var brain = Brain(out var changes);
        var victim = Victim();

        for (var i = 0; i < 8; i++)
        {
            var step = brain.Step(0.5f, Sense(hostile: false, distance: distance), Target(victim));
            Assert.Equal(CombatPhase.Idle, step.Phase);
            Assert.False(step.Hit);
        }

        Assert.Equal(100f, victim.Health);
        Assert.Empty(changes);
    }

    [Fact]
    public void AFightEndsWhenTheTargetStopsBeingHostile()
    {
        var brain = Brain(out _);
        var victim = Victim();
        brain.Step(0.5f, Sense(distance: 1f), Target(victim));
        Assert.Equal(95f, victim.Health);

        var step = brain.Step(2f, Sense(hostile: false, distance: 1f), Target(victim));

        Assert.Equal(CombatPhase.Search, step.Phase);
        Assert.False(step.Hit);
        Assert.Equal(95f, victim.Health);
    }

    [Fact]
    public void LosingSightPursuesBrieflyThenSearchesThenGoesIdleAndForgetsTheTarget()
    {
        var brain = Brain(out var changes);
        var victim = Victim();
        brain.Step(0.1f, Sense(distance: 10f), Target(victim));
        Assert.Equal(CombatPhase.Pursue, brain.Phase);

        Assert.Equal(CombatPhase.Pursue, brain.Step(1f, Sense(visible: false), null).Phase);
        Assert.Equal(CombatPhase.Search, brain.Step(1f, Sense(visible: false), null).Phase);
        Assert.Equal(CombatPhase.Search, brain.Step(5f, Sense(visible: false), null).Phase);
        Assert.Equal("player", brain.Target);
        Assert.Equal(CombatPhase.Idle, brain.Step(5f, Sense(visible: false), null).Phase);
        Assert.Null(brain.Target);
        Assert.False(brain.Engaged);
        Assert.Equal(new[] { (CombatPhase.Idle, CombatPhase.Pursue), (CombatPhase.Pursue, CombatPhase.Search), (CombatPhase.Search, CombatPhase.Idle) }, changes);
    }

    [Fact]
    public void AttackingThenLosingSightFallsBackToPursueBeforeSearching()
    {
        var brain = Brain(out _);
        var victim = Victim();
        brain.Step(0.1f, Sense(distance: 1f), Target(victim));
        Assert.Equal(CombatPhase.Attack, brain.Phase);

        Assert.Equal(CombatPhase.Pursue, brain.Step(1f, Sense(visible: false, distance: 1f), Target(victim)).Phase);
        Assert.Equal(CombatPhase.Search, brain.Step(1f, Sense(visible: false, distance: 1f), Target(victim)).Phase);
    }

    [Fact]
    public void ATargetThatDisappearsFromAnyActivePhaseStartsASearch()
    {
        foreach (var distance in new[] { 10f, 1f })
        {
            var brain = Brain(out _);
            brain.Step(0.1f, Sense(distance: distance), Target(Victim()));

            Assert.Equal(CombatPhase.Search, brain.Step(0.1f, Sense(target: null, visible: false), null).Phase);
            Assert.Equal(CombatPhase.Idle, brain.Step(10f, Sense(target: null, visible: false), null).Phase);
        }
    }

    [Fact]
    public void ATargetSeenAgainDuringTheSearchIsPursuedAgain()
    {
        var brain = Brain(out _);
        brain.Step(0.1f, Sense(distance: 10f), Target(Victim()));
        brain.Step(0.1f, Sense(target: null, visible: false), null);
        Assert.Equal(CombatPhase.Search, brain.Phase);

        Assert.Equal(CombatPhase.Pursue, brain.Step(0.1f, Sense(distance: 8f), Target(Victim())).Phase);
    }

    [Fact]
    public void AWoundedActorFleesInsteadOfAttackingThenGoesIdleOnceFarAway()
    {
        var brain = Brain(out _);
        var victim = Victim();

        var step = brain.Step(0.5f, Sense(distance: 1f, health: 0.1f), Target(victim));

        Assert.Equal(CombatPhase.Flee, step.Phase);
        Assert.False(step.Hit);
        Assert.Equal(100f, victim.Health);
        Assert.Equal(CombatPhase.Flee, brain.Step(0.5f, Sense(distance: 19.9f, health: 0.1f), Target(victim)).Phase);
        Assert.Equal(CombatPhase.Idle, brain.Step(0.5f, Sense(distance: 20f, health: 0.1f), Target(victim)).Phase);
        Assert.Null(brain.Target);
    }

    [Fact]
    public void AFleeingActorKeepsFleeingWhileTheThreatIsOutOfSightButNearAndIgnoresItOnceFar()
    {
        var brain = Brain(out var changes);
        brain.Step(0.5f, Sense(distance: 5f, health: 0.1f), Target(Victim()));

        Assert.Equal(CombatPhase.Flee, brain.Step(0.5f, Sense(visible: false, distance: 8f, health: 0.1f), null).Phase);
        Assert.Equal(CombatPhase.Flee, brain.Step(5f, Sense(visible: false, distance: 15f, health: 0.1f), null).Phase);
        Assert.Equal(CombatPhase.Idle, brain.Step(0.5f, Sense(visible: false, distance: 21f, health: 0.1f), null).Phase);
        Assert.Equal(new[] { (CombatPhase.Idle, CombatPhase.Flee), (CombatPhase.Flee, CombatPhase.Idle) }, changes);
    }

    [Fact]
    public void AWoundedActorFarFromAnEnemyDoesNotEngageAtAll()
    {
        var brain = Brain(out var changes);

        Assert.Equal(CombatPhase.Idle, brain.Step(0.5f, Sense(distance: 30f, health: 0.05f), Target(Victim())).Phase);
        Assert.Empty(changes);
    }

    [Fact]
    public void UnprovenHealthNeverFlees()
    {
        var brain = Brain(out _);

        Assert.Equal(CombatPhase.Attack, brain.Step(0.5f, Sense(distance: 1f, health: null), Target(Victim())).Phase);
    }

    [Fact]
    public void ResetReturnsToIdleAndRaisesTheChangeOnce()
    {
        var brain = Brain(out var changes);
        brain.Step(0.1f, Sense(distance: 10f), Target(Victim()));
        changes.Clear();

        brain.Reset();
        brain.Reset();

        Assert.Equal(CombatPhase.Idle, brain.Phase);
        Assert.Null(brain.Target);
        Assert.Equal(new[] { (CombatPhase.Pursue, CombatPhase.Idle) }, changes);
    }

    [Fact]
    public void ANegativeOrNonFiniteStepNeverRewindsTheCooldownOrTimers()
    {
        var brain = Brain(out _);
        var victim = Victim();
        var target = Target(victim);
        Assert.True(brain.Step(0.5f, Sense(distance: 1f), target).Hit);

        Assert.False(brain.Step(-5f, Sense(distance: 1f), target).Hit);
        Assert.False(brain.Step(float.NaN, Sense(distance: 1f), target).Hit);
        Assert.Equal(95f, victim.Health);
    }

    [Fact]
    public void OnlySearchPursueAttackAndFleeCountAsEngagedSoAnAlertThatFadesNeverReleasesAWalker()
    {
        Assert.False(NpcCombatBrain.IsEngaged(CombatPhase.Idle));
        Assert.False(NpcCombatBrain.IsEngaged(CombatPhase.Alert));
        foreach (var phase in new[] { CombatPhase.Search, CombatPhase.Pursue, CombatPhase.Attack, CombatPhase.Flee }) Assert.True(NpcCombatBrain.IsEngaged(phase));

        var alert = Brain(out var alertChanges);
        alert.Step(0.25f, Sense(target: null, hostile: false, visible: false, aware: true), null);
        alert.Step(5f, Sense(target: null, hostile: false, visible: false), null);
        Assert.Equal((CombatPhase.Alert, CombatPhase.Idle), alertChanges[^1]);
        Assert.False(NpcCombatBrain.IsEngaged(alertChanges[^1].Item1));

        var pursue = Brain(out var pursueChanges);
        pursue.Step(0.1f, Sense(distance: 10f), null);
        pursue.Reset();
        Assert.Equal((CombatPhase.Pursue, CombatPhase.Idle), pursueChanges[^1]);
        Assert.True(NpcCombatBrain.IsEngaged(pursueChanges[^1].Item1));
    }

    [Fact]
    public void AnActorThatCannotFleeFightsInsteadOfFleeing()
    {
        var brain = Brain(out _);
        var victim = Victim();

        var step = brain.Step(0.5f, new CombatSense("player", true, true, false, 1f, 0.05f, CanFlee: false), Target(victim));

        Assert.Equal(CombatPhase.Attack, step.Phase);
        Assert.True(step.Hit);
    }

    [Fact]
    public void AfterTheFleeCapAVisibleThreatIsFoughtNotFledFromAgain()
    {
        var brain = Brain(out _);
        var victim = Victim();
        brain.Step(0.5f, Sense(distance: 5f, health: 0.1f), Target(victim));
        Assert.Equal(CombatPhase.Idle, brain.Step(25f, Sense(distance: 5f, health: 0.1f), Target(victim)).Phase);

        var step = brain.Step(0.5f, Sense(distance: 1f, health: 0.1f), Target(victim));

        Assert.Equal(CombatPhase.Attack, step.Phase);
        Assert.True(step.Hit);
        Assert.Equal(CombatPhase.Flee, brain.Step(25f, Sense(distance: 5f, health: 0.1f), Target(victim)).Phase);
    }

    [Fact]
    public void AFleeGivesUpAfterTheTimeCapEvenWhileTheThreatStaysNear()
    {
        var brain = Brain(out _);
        brain.Step(0.5f, Sense(distance: 5f, health: 0.1f), Target(Victim()));

        Assert.Equal(CombatPhase.Flee, brain.Step(10f, Sense(visible: false, distance: 8f, health: 0.1f), null).Phase);
        Assert.Equal(CombatPhase.Idle, brain.Step(10f, Sense(visible: false, distance: 8f, health: 0.1f), null).Phase);
        Assert.Null(brain.Target);
    }
}
