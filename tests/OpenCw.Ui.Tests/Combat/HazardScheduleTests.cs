using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HazardScheduleTests
{
    static HazardInfo Hazard(float interval = 0.25f, float lifetime = 0f, uint limit = 0) =>
        new(4f, lifetime, interval, limit, 16, null);

    [Fact]
    public void TicksAreDueAtEachIntervalFromTheHazardsStart()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.25f))!.Value;
        Assert.Equal(new HazardTick(id, 2), Assert.Single(schedule.Advance(0.6)));
        Assert.Equal(new HazardTick(id, 2), Assert.Single(schedule.Advance(0.4)));
    }

    [Fact]
    public void ALifetimeZeroNeverExpires()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.5f, 0f))!.Value;
        Assert.Equal(new HazardTick(id, 2000), Assert.Single(schedule.Advance(1000)));
        Assert.Equal(1, schedule.Count);
        Assert.True(schedule.Contains(id));
    }

    [Fact]
    public void ALifetimeEndsTheHazardAndTicksOnlyUpToTheEdge()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.4f, 1f))!.Value;
        Assert.Equal(new HazardTick(id, 2), Assert.Single(schedule.Advance(5)));
        Assert.Equal(0, schedule.Count);
        Assert.False(schedule.Contains(id));
        Assert.Empty(schedule.Advance(1));
    }

    [Fact]
    public void ALimitOfEightEvictsTheOldestHazardWithoutTicks()
    {
        var schedule = new HazardSchedule();
        int first = schedule.Add(Hazard(limit: 8))!.Value;
        for (var i = 1; i < 8; i++) schedule.Add(Hazard(limit: 8));
        int last = schedule.Add(Hazard(limit: 8))!.Value;
        Assert.Equal(8, schedule.Count);
        Assert.False(schedule.Contains(first));
        Assert.True(schedule.Contains(last));
    }

    [Fact]
    public void ALimitOfZeroIsUncapped()
    {
        var schedule = new HazardSchedule();
        for (var i = 0; i < 10; i++) schedule.Add(Hazard(limit: 0));
        Assert.Equal(10, schedule.Count);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-0.1f, 0f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(0.25f, -1f)]
    [InlineData(0.25f, float.PositiveInfinity)]
    public void AHazardWithAnUnusableIntervalOrLifetimeIsRefused(float interval, float lifetime)
    {
        var schedule = new HazardSchedule();
        Assert.Null(schedule.Add(Hazard(interval, lifetime)));
        Assert.Equal(0, schedule.Count);
    }

    [Fact]
    public void ANonPositiveOrNonFiniteFrameDeltaChangesNothing()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.25f))!.Value;
        Assert.Empty(schedule.Advance(0));
        Assert.Empty(schedule.Advance(-1));
        Assert.Empty(schedule.Advance(double.NaN));
        Assert.Equal(new HazardTick(id, 1), Assert.Single(schedule.Advance(0.25)));
    }

    [Fact]
    public void EachHazardKeepsItsOwnIntervalInInsertionOrder()
    {
        var schedule = new HazardSchedule();
        int fast = schedule.Add(Hazard(0.25f))!.Value;
        int slow = schedule.Add(Hazard(0.5f))!.Value;
        Assert.Equal(new[] { new HazardTick(fast, 2), new HazardTick(slow, 1) }, schedule.Advance(0.5));
    }

    [Fact]
    public void AFloatIntervalCountsEachMultipleDespiteFloatWidening()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.1f))!.Value;
        for (var i = 0; i < 3; i++) Assert.Equal(new HazardTick(id, 1), Assert.Single(schedule.Advance(0.1)));
    }

    [Fact]
    public void AFloatIntervalReachesEveryTickOnTheLifetimeEdge()
    {
        var schedule = new HazardSchedule();
        int id = schedule.Add(Hazard(0.1f, 1f))!.Value;
        Assert.Equal(new HazardTick(id, 10), Assert.Single(schedule.Advance(5)));
    }

    sealed class Effects(params (string Key, MagicEffectInfo? Info)[] rows) : IMagicEffectRecords
    {
        public MagicEffectInfo? Of(string effectKey) => rows.FirstOrDefault(row => row.Key == effectKey).Info;
    }

    const uint HostileDetrimental = VitalsEffects.HostileFlag | VitalsEffects.DetrimentalFlag | 0x04000800u;

    static readonly MagicEffectInfo HostileHealth = new(0, HostileDetrimental, "Health");

    [Theory]
    [InlineData(100.0, 0.1f, 1, 10.0)]
    [InlineData(10.0, 0.2f, 1, 2.0)]
    [InlineData(10.0, 0.2f, 5, 10.0)]
    public void AHazardMagnitudeIsDamagePerSecondSoATickRemovesMagnitudeTimesInterval(double magnitude, float interval, int ticks, double expected)
    {
        var plan = HazardDamage.Plan(Hazard(interval), ticks, [new ConsumableEffect("fire", magnitude, 0)], new Effects(("fire", HostileHealth)));
        var spec = Assert.Single(plan.Specs);
        Assert.Equal(new EffectSpec("Health", EffectKind.Damage, spec.Amount), spec);
        Assert.Equal(expected, spec.Amount, 5);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void AHazardSkipsEveryEffectItCannotApplyWithAReasonAndKeepsTheRest()
    {
        var records = new Effects(("fire", HostileHealth), ("rads", new(0, HostileDetrimental, "Rads")), ("heal", new(0, 0, "Health")), ("script", new(1, HostileDetrimental, "Health")));
        var plan = HazardDamage.Plan(Hazard(0.5f), 2,
        [
            new ConsumableEffect("rads", 30, 1), new ConsumableEffect("fire", 4, 0), new ConsumableEffect("fire", 3, 1, true),
            new ConsumableEffect("heal", 3, 0), new ConsumableEffect("script", 3, 0), new ConsumableEffect("gone", 3, 0), new ConsumableEffect("fire", 0, 0),
        ], records);
        Assert.Equal(4.0, Assert.Single(plan.Specs).Amount, 5);
        Assert.Equal(6, plan.Skipped.Count);
        Assert.Contains(plan.Skipped, reason => reason.EndsWith("actor value Rads not tracked"));
        Assert.Contains(plan.Skipped, reason => reason.EndsWith("conditional effect"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AMagicEffectWithItsOwnConditionsIsSkippedLikeTheConsumableRule(int conditions)
    {
        var plan = HazardDamage.Plan(Hazard(0.5f), 1, [new ConsumableEffect("fire", 4, 0)], new Effects(("fire", new MagicEffectInfo(0, HostileDetrimental, "Health", conditions))));
        Assert.Empty(plan.Specs);
        Assert.EndsWith("magic effect has its own conditions", Assert.Single(plan.Skipped));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveTickCountPlansNoDamage(int ticks)
    {
        var plan = HazardDamage.Plan(Hazard(0.25f), ticks, [new ConsumableEffect("fire", 10, 0)], new Effects(("fire", HostileHealth)));
        Assert.Empty(plan.Specs);
        Assert.Equal("no ticks", Assert.Single(plan.Skipped));
    }

    [Fact]
    public void AHazardScheduleTickDrivesDamageToAPlayerTargetAtTheVanillaShapedRate()
    {
        var schedule = new HazardSchedule();
        var hazard = Hazard(0.1f);
        int id = schedule.Add(hazard)!.Value;
        var vitals = new PlayerVitals(100, 100, 90, 40);
        var tick = Assert.Single(schedule.Advance(1));
        var plan = HazardDamage.Plan(hazard, tick.Count, [new ConsumableEffect("fire", 100, 0)], new Effects(("fire", HostileHealth)));
        Assert.Equal(id, tick.Id);
        Assert.Equal(1, HazardDamage.Apply(new EffectApplier(), new PlayerVitalsTarget(vitals), plan));
        Assert.Equal(0, vitals.Health, 3);
    }

    sealed class Body(bool dead = false) : IEffectTarget
    {
        public List<double> Hits { get; } = [];

        public bool IsDead => dead;

        public bool Tracks(string actorValue) => actorValue == "Health";

        public bool Change(string actorValue, double amount)
        {
            Hits.Add(amount);
            return true;
        }
    }

    const double UnitsPerMeter = 10;

    static HazardField Field() => new(new Effects(("fire", HostileHealth)), UnitsPerMeter);

    static readonly ConsumableEffect[] Fire = [new ConsumableEffect("fire", 10, 0)];

    static HazardInfo Sphere(float radius = 10f, float interval = 0.5f, float lifetime = 0f, uint limit = 0, uint flags = 0) =>
        new(radius, lifetime, interval, limit, flags, null);

    static int Run(HazardField field, double seconds, Func<IReadOnlyList<HazardVictim>> victims)
    {
        var accepted = 0;
        for (; seconds > 0; seconds -= HazardField.MaxStepSeconds) accepted += field.Advance(Math.Min(seconds, HazardField.MaxStepSeconds), victims);
        return accepted;
    }

    static HazardVictim At(float meters, Body body, bool player = false) => new(new Vector3(meters, 0, 0), player, body);

    [Fact]
    public void AHazardFieldDamagesOnlyAtEachIntervalByMagnitudeTimesIntervalAndBuildsVictimsOnlyWhenATickIsDue()
    {
        var field = Field();
        var body = new Body();
        var asked = 0;
        Assert.NotNull(field.Spawn("A", Sphere(), Vector3.Zero, Fire).Id);

        field.Advance(0.25, () => { asked++; return [At(0, body)]; });
        Assert.Equal(0, asked);
        Assert.Empty(body.Hits);
        field.Advance(0.25, () => { asked++; return [At(0, body)]; });
        Assert.Equal(1, asked);
        Assert.Equal(new[] { -5.0 }, body.Hits.ToArray());
        field.Advance(0.25, () => [At(0, body)]);
        Assert.Single(body.Hits);
        field.Advance(0.25, () => [At(0, body)]);
        Assert.Equal(new[] { -5.0, -5.0 }, body.Hits.ToArray());
    }

    [Fact]
    public void AHazardFieldHurtsOnlyActorsInsideTheRadiusInFo4Units()
    {
        var field = Field();
        var inside = new Body();
        var edge = new Body();
        var outside = new Body();
        field.Spawn("A", Sphere(radius: 10f), new Vector3(5, 0, 0), Fire);

        var accepted = Run(field, 0.5, () => [At(5.5f, inside), At(6f, edge), At(6.1f, outside)]);

        Assert.Equal(1.0, field.RadiusMeters(Sphere(radius: 10f)), 6);
        Assert.Equal(2, accepted);
        Assert.Single(inside.Hits);
        Assert.Single(edge.Hits);
        Assert.Empty(outside.Hits);
    }

    [Fact]
    public void AnExpiredHazardTicksUpToItsLifetimeThenStopsAndIsDropped()
    {
        var field = Field();
        var body = new Body();
        field.Spawn("A", Sphere(lifetime: 1f), Vector3.Zero, Fire);

        Run(field, 5, () => [At(0, body)]);
        Assert.Equal(new[] { -5.0, -5.0 }, body.Hits.ToArray());
        Assert.Equal(0, field.Count);
        Run(field, 5, () => [At(0, body)]);
        Assert.Equal(2, body.Hits.Count);
    }

    [Fact]
    public void AnAffectsPlayerOnlyHazardSkipsActorsAndAHazardTickSkipsTheDead()
    {
        var field = Field();
        var npc = new Body();
        var player = new Body();
        var corpse = new Body(dead: true);
        field.Spawn("A", Sphere(flags: HazardField.AffectsPlayerOnly), Vector3.Zero, Fire);
        field.Spawn("B", Sphere(), Vector3.Zero, Fire);

        Run(field, 0.5, () => [At(0, npc), At(0, player, true), At(0, corpse)]);

        Assert.Single(npc.Hits);
        Assert.Equal(2, player.Hits.Count);
        Assert.Empty(corpse.Hits);
    }

    [Fact]
    public void ALimitEvictsOnlyTheSameRecordsOldestHazardAndAHazardWithNoUsableEffectIsNotSpawned()
    {
        var field = Field();
        var other = field.Spawn("B", Sphere(limit: 1), Vector3.Zero, Fire);
        field.Spawn("A", Sphere(limit: 2), Vector3.Zero, Fire);
        field.Spawn("A", Sphere(limit: 2), Vector3.Zero, Fire);
        field.Spawn("A", Sphere(limit: 2), Vector3.Zero, Fire);

        Assert.NotNull(other.Id);
        Assert.Equal(3, field.Count);
        var none = field.Spawn("C", Sphere(), Vector3.Zero, [new ConsumableEffect("gone", 10, 0)]);
        Assert.Null(none.Id);
        Assert.Single(none.Skipped);
        Assert.Equal(3, field.Count);
    }

    [Fact]
    public void AFullFieldStillReplacesTheOldestHazardOfARecordAtItsLimitButRefusesAGrowingSpawn()
    {
        var field = Field();
        for (var i = 0; i < HazardField.Capacity - 1; i++) Assert.NotNull(field.Spawn($"F{i}", Sphere(), Vector3.Zero, Fire).Id);
        Assert.NotNull(field.Spawn("L", Sphere(limit: 1), Vector3.Zero, Fire).Id);
        Assert.Equal(HazardField.Capacity, field.Count);

        var replaced = field.Spawn("L", Sphere(limit: 1), Vector3.Zero, Fire);

        Assert.NotNull(replaced.Id);
        Assert.Equal(HazardField.Capacity, field.Count);
        Assert.Null(field.Spawn("F0", Sphere(), Vector3.Zero, Fire).Id);
        Assert.Null(field.Spawn("L", Sphere(limit: 2), Vector3.Zero, Fire).Id);
        Assert.Equal(HazardField.Capacity, field.Count);
    }

    [Fact]
    public void ADisabledHazardAppliesNothingAndKeepsExpiringAndAnEnabledOneResumesWithoutABurst()
    {
        var field = Field();
        var body = new Body();
        var id = field.Spawn("A", Sphere(interval: 0.5f, lifetime: 3f), Vector3.Zero, Fire).Id!.Value;

        Assert.True(field.SetEnabled("A", id, false));
        Run(field, 1, () => [At(0, body)]);
        Assert.Empty(body.Hits);

        Assert.True(field.SetEnabled("A", id, true));
        Run(field, 0.5, () => [At(0, body)]);
        Assert.Single(body.Hits);

        Run(field, 5, () => [At(0, body)]);
        Assert.False(field.Contains("A", id));
        Assert.False(field.SetEnabled("A", id, true));
        Assert.False(field.SetEnabled("missing", 0, false));
    }

    [Fact]
    public void AHazardSpawnedDisabledAppliesNothingUntilEnabled()
    {
        var field = Field();
        var body = new Body();
        var id = field.Spawn("A", Sphere(), Vector3.Zero, Fire, false).Id!.Value;

        Run(field, 0.5, () => [At(0, body)]);
        Assert.Empty(body.Hits);
        field.SetEnabled("A", id, true);
        Run(field, 0.5, () => [At(0, body)]);
        Assert.Single(body.Hits);
    }

    [Fact]
    public void ASpawnThatLostSomeEffectsCarriesAPartialNoticeAndAFullOneDoesNot()
    {
        var field = Field();
        var partial = field.Spawn("A", Sphere(), Vector3.Zero, [new ConsumableEffect("fire", 10, 0), new ConsumableEffect("gone", 10, 0)]);
        var full = field.Spawn("B", Sphere(), Vector3.Zero, Fire);
        var none = field.Spawn("C", Sphere(), Vector3.Zero, [new ConsumableEffect("gone", 10, 0)]);

        Assert.Contains("gone", partial.PartialNotice("A"));
        Assert.Null(full.PartialNotice("B"));
        Assert.Null(none.PartialNotice("C"));
    }

    [Fact]
    public void AHazardFieldDamagesAPlayerThroughTheVitalsPathAtTheVanillaShapedRate()
    {
        var field = Field();
        var vitals = new PlayerVitals(100, 100, 90, 40);
        field.Spawn("A", Sphere(interval: 0.125f), Vector3.Zero, [new ConsumableEffect("fire", 100, 0)]);

        Run(field, 1, () => [new HazardVictim(Vector3.Zero, true, new PlayerVitalsTarget(vitals))]);

        Assert.Equal(0, vitals.Health, 3);
    }
}
