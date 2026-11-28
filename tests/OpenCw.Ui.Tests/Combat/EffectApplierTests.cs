using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class EffectApplierTests
{
    sealed class FakeTarget(bool accepts = true) : IEffectTarget
    {
        public readonly List<(string, double)> Calls = [];
        public bool Dead { get; set; }
        public bool IsDead => Dead;
        public bool Tracks(string actorValue) => actorValue == "Health";

        public bool Change(string actorValue, double amount)
        {
            Calls.Add((actorValue, amount));
            return accepts;
        }
    }

    static PlayerVitals Vitals(double health = 100) => new(100, health, 90, 40);

    static EffectSpec Damage(double amount, double seconds = 0, string av = "Health") => new(av, EffectKind.Damage, amount, seconds);

    static EffectSpec Restore(double amount, double seconds = 0, string av = "Health") => new(av, EffectKind.Restore, amount, seconds);

    [Fact]
    public void InstantDamageLowersPlayerHealthByTheCallersAmount()
    {
        var vitals = Vitals();
        var outcome = new EffectApplier().Apply(new PlayerVitalsTarget(vitals), Damage(10));
        Assert.Equal(new EffectOutcome(true, null), outcome);
        Assert.Equal(90, vitals.Health);
    }

    [Fact]
    public void InstantRestoreRaisesHealthAndClampsAtTheMaximum()
    {
        var vitals = Vitals(50);
        var applier = new EffectApplier();
        Assert.True(applier.Apply(new PlayerVitalsTarget(vitals), Restore(20)).Accepted);
        Assert.Equal(70, vitals.Health);
        Assert.True(applier.Apply(new PlayerVitalsTarget(vitals), Restore(500)).Accepted);
        Assert.Equal(100, vitals.Health);
    }

    [Fact]
    public void RestoringAFullTargetReportsUnchanged()
    {
        var outcome = new EffectApplier().Apply(new PlayerVitalsTarget(Vitals()), Restore(5));
        Assert.Equal(new EffectOutcome(false, "target unchanged"), outcome);
    }

    [Fact]
    public void LethalDamageClampsAtZeroAndThePlayerTargetReadsDead()
    {
        var vitals = Vitals(30);
        var target = new PlayerVitalsTarget(vitals);
        Assert.True(new EffectApplier().Apply(target, Damage(500)).Accepted);
        Assert.Equal(0, vitals.Health);
        Assert.True(target.IsDead);
    }

    [Fact]
    public void ADeadTargetIsRefusedAndNothingIsChanged()
    {
        var target = new FakeTarget { Dead = true };
        var applier = new EffectApplier();
        Assert.Equal(new EffectOutcome(false, "target is dead"), applier.Apply(target, Damage(5)));
        Assert.Equal(new EffectOutcome(false, "target is dead"), applier.Apply(target, Damage(5, 3)));
        Assert.Empty(target.Calls);
        Assert.Equal(0, applier.Active);
    }

    [Theory]
    [InlineData("nan", "amount is not a positive number")]
    [InlineData("zero", "amount is not a positive number")]
    [InlineData("negative", "amount is not a positive number")]
    [InlineData("negativeDuration", "duration is not a non-negative number")]
    [InlineData("nanDuration", "duration is not a non-negative number")]
    [InlineData("untracked", "actor value Rads not tracked")]
    [InlineData("noActorValue", "no actor value")]
    public void AnEffectOutsideTheAcceptedShapeIsRefusedWithAReasonAndChangesNothing(string shape, string reason)
    {
        var spec = shape switch
        {
            "nan" => Damage(double.NaN),
            "zero" => Damage(0),
            "negative" => Damage(-5),
            "negativeDuration" => Damage(5, -1),
            "nanDuration" => Damage(5, double.NaN),
            "untracked" => Damage(5, 0, "Rads"),
            _ => Damage(5, 0, ""),
        };
        var target = new FakeTarget();
        var applier = new EffectApplier();
        Assert.Equal(new EffectOutcome(false, reason), applier.Apply(target, spec));
        Assert.Empty(target.Calls);
        Assert.Equal(0, applier.Active);
    }

    [Fact]
    public void ATargetThatRefusesTheChangeReportsUnchangedForAnInstantEffect()
    {
        var outcome = new EffectApplier().Apply(new FakeTarget(accepts: false), Damage(5));
        Assert.Equal(new EffectOutcome(false, "target unchanged"), outcome);
    }

    [Fact]
    public void ATimedEffectSpreadsItsTotalEvenlyAcrossTheDurationAndThenStops()
    {
        var vitals = Vitals();
        var applier = new EffectApplier();
        Assert.True(applier.Apply(new PlayerVitalsTarget(vitals), Damage(10, 5)).Accepted);
        Assert.Equal(100, vitals.Health);
        Assert.Equal(1, applier.Active);
        applier.Tick(2);
        Assert.Equal(96, vitals.Health, 9);
        applier.Tick(3);
        Assert.Equal(90, vitals.Health, 9);
        Assert.Equal(0, applier.Active);
        applier.Tick(10);
        Assert.Equal(90, vitals.Health, 9);
    }

    [Fact]
    public void UnevenAndOversizedTicksDeliverExactlyTheTotal()
    {
        var target = new FakeTarget();
        var applier = new EffectApplier();
        applier.Apply(target, Damage(7, 3));
        foreach (var seconds in new[] { 0.7, 0.7, 0.7, 0.7, 5.0, 4.0 }) applier.Tick(seconds);
        Assert.Equal(-7, target.Calls.Sum(call => call.Item2), 9);
        Assert.Equal(0, applier.Active);
    }

    [Fact]
    public void ATimedRestoreRaisesHealthOverTime()
    {
        var vitals = Vitals(40);
        var applier = new EffectApplier();
        applier.Apply(new PlayerVitalsTarget(vitals), Restore(30, 10));
        applier.Tick(5);
        Assert.Equal(55, vitals.Health, 9);
        applier.Tick(5);
        Assert.Equal(70, vitals.Health, 9);
    }

    [Fact]
    public void TimedEffectsStackAsIndependentRunningEffects()
    {
        var vitals = Vitals();
        var target = new PlayerVitalsTarget(vitals);
        var applier = new EffectApplier();
        applier.Apply(target, Damage(10, 2));
        applier.Apply(target, Damage(20, 4));
        Assert.Equal(2, applier.Active);
        applier.Tick(2);
        Assert.Equal(80, vitals.Health, 9);
        Assert.Equal(1, applier.Active);
        applier.Tick(2);
        Assert.Equal(70, vitals.Health, 9);
        Assert.Equal(0, applier.Active);
    }

    [Fact]
    public void ATimedEffectIsDroppedOnceItsTargetIsDeadAndTouchesItNoMore()
    {
        var target = new FakeTarget();
        var applier = new EffectApplier();
        applier.Apply(target, Damage(10, 10));
        applier.Tick(1);
        Assert.Single(target.Calls);
        target.Dead = true;
        applier.Tick(1);
        Assert.Single(target.Calls);
        Assert.Equal(0, applier.Active);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ATickWithoutAPositiveFiniteTimeAdvancesNothing(double seconds)
    {
        var target = new FakeTarget();
        var applier = new EffectApplier();
        applier.Apply(target, Damage(10, 5));
        applier.Tick(seconds);
        Assert.Empty(target.Calls);
        Assert.Equal(1, applier.Active);
    }

    [Fact]
    public void ActionPointsAreTrackedForThePlayerAndOtherValuesAreNot()
    {
        var vitals = Vitals();
        var applier = new EffectApplier();
        Assert.True(applier.Apply(new PlayerVitalsTarget(vitals), Damage(15, 0, "ActionPoints")).Accepted);
        Assert.Equal(25, vitals.ActionPoints);
        Assert.Equal("actor value Rads not tracked", applier.Apply(new PlayerVitalsTarget(vitals), Damage(5, 0, "Rads")).Reason);
    }
}
