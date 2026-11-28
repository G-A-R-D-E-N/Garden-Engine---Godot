using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

[Collection("PlayerHealth")]
public class PlayerHealthTests
{
    [Fact]
    public void NothingIsAttachedByDefaultSoThereIsNoTarget()
    {
        PlayerHealth.Detach();
        Assert.Null(PlayerHealth.Current);
        Assert.Null(PlayerHealth.Target);
    }

    [Fact]
    public void AnAttachedHolderGivesAnEffectTargetOverTheSameVitals()
    {
        try
        {
            var vitals = PlayerHealth.Attach(100, 80, 90, 40);
            Assert.Same(vitals, PlayerHealth.Current);
            Assert.True(new EffectApplier().Apply(PlayerHealth.Target!, new EffectSpec("Health", EffectKind.Damage, 25)).Accepted);
            Assert.Equal(55, PlayerHealth.Current!.Health);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnprovenMaximumHealthLeavesTheHolderEmptyAndClearsAnEarlierOne(double max)
    {
        try
        {
            PlayerHealth.Attach(100, 100, 90, 40);
            Assert.Null(PlayerHealth.Attach(max, 50, 90, 40));
            Assert.Null(PlayerHealth.Current);
            Assert.Null(PlayerHealth.Target);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnprovenCurrentHealthLeavesTheHolderEmptyAndClearsAnEarlierOne(double health)
    {
        try
        {
            PlayerHealth.Attach(100, 100, 90, 40);
            Assert.Null(PlayerHealth.Attach(100, health, 90, 40));
            Assert.Null(PlayerHealth.Current);
            Assert.Null(PlayerHealth.Target);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Fact]
    public void DetachClearsTheHolder()
    {
        PlayerHealth.Attach(100, 100, 90, 40);
        PlayerHealth.Detach();
        Assert.Null(PlayerHealth.Target);
    }

    sealed class HealthModifier : IMagicEffectRecords
    {
        public MagicEffectInfo? Of(string effectKey) => new(VitalsEffects.ValueModifier, 0, VitalsEffects.HealthValue);
    }

    static Func<string, double?> Stored(Dictionary<string, double> values, List<string>? asked = null) => name =>
    {
        asked?.Add(name);
        return values.TryGetValue(name, out var value) ? value : null;
    };

    [Fact]
    public void EnsureFromTakesTheMaximaFromTheStoredRecordValuesAndStartsFull()
    {
        PlayerHealth.Detach();
        try
        {
            var asked = new List<string>();
            var vitals = PlayerHealth.EnsureFrom(Stored(new() { ["Health"] = 150, ["ActionPoints"] = 100 }, asked));
            Assert.Equal(new[] { "Health", "ActionPoints", "Endurance", "Agility" }, asked);
            Assert.Equal((150d, 150d, 100d, 100d), (vitals!.MaxHealth, vitals.Health, vitals.MaxActionPoints, vitals.ActionPoints));
            vitals.AddHealth(-100);
            Assert.True(VitalsEffects.Apply(vitals, [new ConsumableEffect("fx", 80, 0, false)], new HealthModifier()));
            Assert.Equal(130, vitals.Health);
            VitalsEffects.Apply(vitals, [new ConsumableEffect("fx", 80, 0, false)], new HealthModifier());
            Assert.Equal(150, vitals.Health);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Fact]
    public void EnsureFromKeepsAnAttachedStoreInsteadOfRefillingIt()
    {
        PlayerHealth.Detach();
        try
        {
            var first = PlayerHealth.EnsureFrom(Stored(new() { ["Health"] = 150, ["ActionPoints"] = 100 }))!;
            first.AddHealth(-60);
            var again = PlayerHealth.EnsureFrom(_ => throw new InvalidOperationException("the record must not be read again"));
            Assert.Same(first, again);
            Assert.Equal(90, PlayerHealth.Current!.Health);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, -5)]
    public void EnsureFromAttachesNothingWhenHealthIsNotStoredAsAPositiveValue(bool stored, double health)
    {
        PlayerHealth.Detach();
        try
        {
            var values = new Dictionary<string, double> { ["ActionPoints"] = 100 };
            if (stored) values["Health"] = health;
            Assert.Null(PlayerHealth.EnsureFrom(Stored(values)));
            Assert.Null(PlayerHealth.Current);
        }
        finally { PlayerHealth.Detach(); }
    }

    [Fact]
    public void EnsureFromReadsAMissingActionPointValueAsZero()
    {
        PlayerHealth.Detach();
        try
        {
            var vitals = PlayerHealth.EnsureFrom(Stored(new() { ["Health"] = 150 }))!;
            Assert.Equal((0d, 0d), (vitals.MaxActionPoints, vitals.ActionPoints));
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(1, 70)]
    [InlineData(8, 140)]
    [InlineData(10, 160)]
    public void MaximumActionPointsAreSixtyPlusTenPerAgility(double agility, double expected) =>
        Assert.Equal(expected, PlayerHealth.MaxActionPointsFor(agility));

    [Theory]
    [InlineData(1, 1, 85)]
    [InlineData(1, 2, 88)]
    [InlineData(10, 1, 130)]
    [InlineData(10, 2, 137)]
    [InlineData(10, 101, 880)]
    [InlineData(5, 0, 105)]
    public void MaximumHealthIsTheFlooredEnduranceAndLevelFormulaAndAFloorOfLevelOne(double endurance, int level, double expected) =>
        Assert.Equal(expected, PlayerHealth.MaxHealthFor(endurance, level));

    [Fact]
    public void EnsureFromDerivesTheMaximaFromStoredEnduranceAndAgilityAtTheGivenLevel()
    {
        PlayerHealth.Detach();
        try
        {
            var asked = new List<string>();
            var vitals = PlayerHealth.EnsureFrom(Stored(new() { ["Health"] = 150, ["ActionPoints"] = 100, ["Endurance"] = 10, ["Agility"] = 8 }, asked), 3);
            Assert.Equal(new[] { "Health", "ActionPoints", "Endurance", "Agility" }, asked);
            Assert.Equal((145d, 145d, 140d, 140d), (vitals!.MaxHealth, vitals.Health, vitals.MaxActionPoints, vitals.ActionPoints));
        }
        finally { PlayerHealth.Detach(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void EnsureFromFallsBackToTheStoredHealthAndActionPointsWhenTheSpecialValueIsNotAPositiveNumber(double special)
    {
        PlayerHealth.Detach();
        try
        {
            var vitals = PlayerHealth.EnsureFrom(Stored(new() { ["Health"] = 150, ["ActionPoints"] = 100, ["Endurance"] = special, ["Agility"] = special }))!;
            Assert.Equal((150d, 100d), (vitals.MaxHealth, vitals.MaxActionPoints));
        }
        finally { PlayerHealth.Detach(); }
    }
}
