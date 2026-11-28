using System;
using OpenCommonwealth;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActorStateTests
{
    private static ActorState Spawn(ActorProtection protection, float? health = 25f)
    {
        var state = new ActorState(12, null) { Protection = protection };
        if (health is { } max) state.SetHealth(max);
        return state;
    }

    [Fact]
    public void AnEssentialActorHoldsAtOneHealthUnderLethalDamageAndAnOrdinaryActorDies()
    {
        var ordinary = Spawn(ActorProtection.None);
        var essential = Spawn(ActorProtection.Essential);
        int deaths = 0;
        ordinary.Died += _ => deaths++;
        essential.Died += _ => deaths++;

        Assert.True(ordinary.Damage(1000f, "p"));
        Assert.True(ordinary.IsDead);
        Assert.True(essential.Damage(1000f, "p"));
        Assert.True(essential.Damage(1000f, "p"));

        Assert.False(essential.IsDead);
        Assert.Equal(1f, essential.Health);
        Assert.Equal(1, deaths);
    }

    [Fact]
    public void AnInvulnerableActorTakesNoDamageAndKeepsItsHealth()
    {
        var state = Spawn(ActorProtection.Invulnerable);

        Assert.False(state.Damage(5f, "p"));

        Assert.Equal(25f, state.Health);
        Assert.False(state.IsDead);
    }

    [Fact]
    public void AnActorWithoutProvenHealthIgnoresDamageAndNeverDiesFromIt()
    {
        var state = Spawn(ActorProtection.None, health: null);

        Assert.False(state.Damage(1000f, "p"));

        Assert.Null(state.Health);
        Assert.False(state.IsDead);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void HealthThatIsNotAPositiveFiniteNumberIsRefusedInsteadOfStored(float max)
    {
        var state = new ActorState(1, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetHealth(max));

        Assert.Null(state.Health);
    }

    [Fact]
    public void RestoringADeadActorFromASaveDoesNotRaiseDiedAgain()
    {
        var state = Spawn(ActorProtection.None);
        int deaths = 0;
        state.Died += _ => deaths++;

        state.RestoreDead();

        Assert.True(state.IsDead);
        Assert.Equal(0f, state.Health);
        Assert.Equal(0, deaths);
    }

    [Theory]
    [InlineData(0u, ActorProtection.None)]
    [InlineData(1u << 1, ActorProtection.Essential)]
    [InlineData(1u << 11, ActorProtection.Protected)]
    [InlineData(1u << 11 | 1u << 1, ActorProtection.Essential)]
    [InlineData(1u << 31 | 1u << 1, ActorProtection.Invulnerable)]
    public void ConfigurationFlagsMapToTheProtectionThatDamageHonours(uint flags, ActorProtection expected)
    {
        Assert.Equal(expected, ActorSpawnRules.ProtectionOf(flags));
    }

    [Fact]
    public void ASpawnedActorWithProvenHealthStartsFullAndDiesAfterThatMuchDamage()
    {
        var state = ActorSpawnRules.StateFor(12, null, 25f, ActorProtection.None);

        Assert.Equal(12, state.Level);
        Assert.Equal(25f, state.Health);
        Assert.Equal(25f, state.MaxHealth);
        int deaths = 0;
        state.Died += _ => deaths++;
        Assert.True(state.Damage(10f, "a"));
        Assert.False(state.IsDead);
        Assert.True(state.Damage(15f, "b"));
        Assert.True(state.IsDead);
        Assert.Equal("b", state.Killer);
        Assert.Equal(1, deaths);
    }

    [Fact]
    public void ASpawnedEssentialActorKeepsItsProtectionThroughTheSpawnState()
    {
        var state = ActorSpawnRules.StateFor(12, null, 25f, ActorProtection.Essential);

        Assert.Equal(ActorProtection.Essential, state.Protection);
        Assert.True(state.Damage(1000f, "p"));
        Assert.False(state.IsDead);
        Assert.Equal(1f, state.Health);
    }

    [Fact]
    public void AStateWithAnUnprovenLevelHasNoHealthEvenWhenHealthIsGiven()
    {
        var state = ActorSpawnRules.StateFor(1, "template", 25f, ActorProtection.None);

        Assert.Equal("template", state.LevelUnresolved);
        Assert.Null(state.Health);
        Assert.Null(state.MaxHealth);
        Assert.False(state.Damage(1000f, "a"));
    }

    [Fact]
    public void AStateWithoutProvenHealthOrReadableFlagsHasNoHealthAndIgnoresDamage()
    {
        var noHealth = ActorSpawnRules.StateFor(12, null, null, ActorProtection.None);
        var noFlags = ActorSpawnRules.StateFor(12, null, 25f, null);

        Assert.Null(noHealth.Health);
        Assert.False(noHealth.Damage(1000f, "a"));
        Assert.Null(noFlags.Health);
        Assert.False(noFlags.Damage(1000f, "a"));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AStateWithAHealthThatIsNotAPositiveFiniteNumberHasNoHealth(float health)
    {
        var state = ActorSpawnRules.StateFor(12, null, health, ActorProtection.None);

        Assert.Null(state.Health);
        Assert.False(state.Damage(1000f, "a"));
    }

    [Fact]
    public void TheHitThatReachesTheFloorPutsAnEssentialActorDownAndRecoveryGetsItUpAtAFractionOfMaxHealth()
    {
        var state = Spawn(ActorProtection.Essential, health: 40f);

        state.Damage(10f, "p");
        Assert.False(state.IsEssentialDown);
        state.Damage(1000f, "p");
        Assert.True(state.IsEssentialDown);
        Assert.False(state.IsDead);

        state.Tick(5f, 20f, 0.5f);
        Assert.True(state.IsEssentialDown);
        state.Tick(15f, 20f, 0.5f);
        Assert.False(state.IsEssentialDown);
        Assert.Equal(ActorLife.Alive, state.Life);
        Assert.Equal(20f, state.Health);
    }

    [Fact]
    public void AProtectedActorKeepsTheFloorButNeverGoesDown()
    {
        var state = Spawn(ActorProtection.Protected);

        Assert.True(state.Damage(1000f, "p"));

        Assert.Equal(1f, state.Health);
        Assert.False(state.IsEssentialDown);
        Assert.False(state.IsDead);
    }

    [Fact]
    public void NoBleedoutRecoveryKeepsADownedActorDownAndAKillStillKillsIt()
    {
        var state = Spawn(ActorProtection.Essential);
        state.Damage(1000f, "p");
        state.NoBleedoutRecovery = true;

        state.Tick(1000f, 20f, 0.5f);
        Assert.True(state.IsEssentialDown);
        Assert.True(state.Kill("k"));
        Assert.True(state.IsDead);
        Assert.False(state.IsEssentialDown);
    }

    [Fact]
    public void RecoveryNeverLowersHealthAndIgnoresNonPositiveOrNonFiniteTicks()
    {
        var state = Spawn(ActorProtection.Essential);
        state.Damage(1000f, "p");

        state.Tick(0f, 0f, 0f);
        state.Tick(float.NaN, 0f, 0f);
        Assert.True(state.IsEssentialDown);
        state.Tick(1f, 0f, 0f);
        Assert.False(state.IsEssentialDown);
        Assert.Equal(1f, state.Health);
    }

    [Fact]
    public void ADownedActorTakingMoreDamageStaysAtTheFloorAndDoesNotRestartTheTimer()
    {
        var state = Spawn(ActorProtection.Essential);
        state.Damage(1000f, "p");
        state.Tick(15f, 20f, 0.5f);

        Assert.True(state.Damage(5f, "p"));
        state.Tick(5f, 20f, 0.5f);

        Assert.False(state.IsEssentialDown);
        Assert.Equal(12.5f, state.Health);
    }

    [Fact]
    public void AKnockValueIsTimedAndExpiresToNormalAndStaggerIsASeparateBit()
    {
        var state = Spawn(ActorProtection.None);

        Assert.Equal(ActorKnock.Normal, state.Knock);
        Assert.True(state.SetKnock(ActorKnock.Down, 3f));
        Assert.True(state.Stagger(1f));
        Assert.Equal(ActorKnock.Down, state.Knock);
        Assert.Equal(7, (int)state.Knock);
        Assert.True(state.Staggered);
        Assert.True(state.Timed);

        state.Tick(2f, 20f, 0.5f);
        Assert.Equal(ActorKnock.Down, state.Knock);
        Assert.False(state.Staggered);
        state.Tick(1f, 20f, 0.5f);
        Assert.Equal(ActorKnock.Normal, state.Knock);
        Assert.False(state.Timed);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void AKnockOrStaggerWithoutAPositiveFiniteDurationIsRefused(float seconds)
    {
        var state = Spawn(ActorProtection.None);

        Assert.False(state.SetKnock(ActorKnock.Out, seconds));
        Assert.False(state.Stagger(seconds));
        Assert.Equal(ActorKnock.Normal, state.Knock);
    }

    [Fact]
    public void ADeadActorCannotBeKnockedOrStaggered()
    {
        var state = Spawn(ActorProtection.None);
        state.Kill("k");

        Assert.False(state.SetKnock(ActorKnock.Out, 3f));
        Assert.False(state.Stagger(3f));
    }
}
