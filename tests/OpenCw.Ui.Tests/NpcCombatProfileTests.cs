using OpenCommonwealth;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class NpcCombatProfileTests
{
    private static readonly CombatTunables Baseline = new(AttackRange: 2f, AttackDamage: 5f, AttackCooldown: 1.5f, AlertSeconds: 3f, SearchSeconds: 10f, LoseSeconds: 2f, FleeBelowHealth: 0.15f, FleeSafeDistance: 20f, FleeMaxSeconds: 20f);

    private static WeaponInfo Weapon(int animationType, double baseDamage = 0, double reach = 0, double maxRange = 0, int pellets = 1, double fireSeconds = 0, double attackSeconds = 0, double ammoDamage = 0, double speed = 0) =>
        new(animationType, null, 0, baseDamage, reach, maxRange, pellets, 0u, fireSeconds, 0, attackSeconds, ammoDamage, null, speed);

    [Fact]
    public void AMeleeWeaponSetsReachDamageAndSpeedScaledCooldown()
    {
        var weapon = Weapon(1, baseDamage: 20, reach: 1.5, attackSeconds: 0.8, speed: 2);

        var tunables = NpcCombatProfile.From(Baseline, weapon, combatDistance: 200, unitsPerMeter: 100, confidence: null);

        Assert.Equal(3f, tunables.AttackRange);
        Assert.Equal(20f, tunables.AttackDamage);
        Assert.Equal(0.4, (double)tunables.AttackCooldown, 5);
        Assert.Equal(Baseline.SearchSeconds, tunables.SearchSeconds);
    }

    [Fact]
    public void ARangedWeaponUsesMaxRangeAmmoDamagePerPelletAndFireTime()
    {
        var weapon = Weapon(8, baseDamage: 10, maxRange: 5000, pellets: 3, fireSeconds: 0.5, ammoDamage: 5);

        var tunables = NpcCombatProfile.From(Baseline, weapon, combatDistance: 200, unitsPerMeter: 100, confidence: null);

        Assert.Equal(50f, tunables.AttackRange);
        Assert.Equal(45f, tunables.AttackDamage);
        Assert.Equal(0.5f, tunables.AttackCooldown);
    }

    [Fact]
    public void NoWeaponAnUnsupportedKindOrZeroedFieldsKeepTheBaseline()
    {
        Assert.Equal(Baseline, NpcCombatProfile.From(Baseline, null, 200, 100, null));
        Assert.Equal(Baseline, NpcCombatProfile.From(Baseline, Weapon(10, baseDamage: 50, maxRange: 900, attackSeconds: 1), 200, 100, null));
        Assert.Equal(Baseline, NpcCombatProfile.From(Baseline, Weapon(1), 200, 100, null));
        Assert.Equal(Baseline.AttackRange, NpcCombatProfile.From(Baseline, Weapon(8, baseDamage: 10, maxRange: 5000, fireSeconds: 0.5), 200, 0, null).AttackRange);
    }

    [Fact]
    public void ATinyReachNeverShrinksTheAttackRangeBelowTheWalkerStopDistance()
    {
        var melee = NpcCombatProfile.From(Baseline, Weapon(1, baseDamage: 20, reach: 0.001, attackSeconds: 0.8), combatDistance: 200, unitsPerMeter: 100, confidence: null);
        var ranged = NpcCombatProfile.From(Baseline, Weapon(8, baseDamage: 10, maxRange: 1, fireSeconds: 0.5), combatDistance: 200, unitsPerMeter: 100, confidence: null);

        Assert.Equal(NpcCombatProfile.MinAttackRange, melee.AttackRange);
        Assert.Equal(NpcCombatProfile.MinAttackRange, ranged.AttackRange);
    }

    [Fact]
    public void AMissingCombatDistanceKeepsTheBaselineMeleeRange()
    {
        var tunables = NpcCombatProfile.From(Baseline, Weapon(1, baseDamage: 20, reach: 1.5, attackSeconds: 0.8), combatDistance: 0, unitsPerMeter: 100, confidence: null);

        Assert.Equal(Baseline.AttackRange, tunables.AttackRange);
        Assert.Equal(20f, tunables.AttackDamage);
    }

    [Fact]
    public void ConfidenceScalesTheFleeThresholdAndFoolhardyNeverFlees()
    {
        Assert.Equal(0f, NpcCombatProfile.From(Baseline, null, 0, 100, NpcCombatProfile.Foolhardy).FleeBelowHealth);
        Assert.Equal(0.3, (double)NpcCombatProfile.From(Baseline, null, 0, 100, NpcCombatProfile.Cowardly).FleeBelowHealth, 5);
        Assert.Equal(1f, NpcCombatProfile.From(Baseline with { FleeBelowHealth = 0.6f }, null, 0, 100, NpcCombatProfile.Cowardly).FleeBelowHealth);
        Assert.Equal(Baseline.FleeBelowHealth, NpcCombatProfile.From(Baseline, null, 0, 100, 2).FleeBelowHealth);
        Assert.Equal(Baseline.FleeBelowHealth, NpcCombatProfile.From(Baseline, null, 0, 100, null).FleeBelowHealth);
    }

    [Fact]
    public void AFoolhardyActorWithAWoundedHealthFractionStillAttacks()
    {
        var tunables = NpcCombatProfile.From(Baseline, null, 0, 100, NpcCombatProfile.Foolhardy);
        var brain = new NpcCombatBrain(tunables);
        var state = new ActorState(1, null);
        state.SetHealth(100f);

        var step = brain.Step(0.5f, new CombatSense("player", true, true, false, 1f, 0.05f), new ActorStateTarget(state));

        Assert.Equal(CombatPhase.Attack, step.Phase);
    }
}
