using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HazardFieldTests
{
    sealed class HostileHealth : IMagicEffectRecords
    {
        public MagicEffectInfo? Of(string effectKey) => new(VitalsEffects.ValueModifier, VitalsEffects.HostileFlag, VitalsEffects.HealthValue);
    }

    static readonly IReadOnlyList<ConsumableEffect> Burn = [new ConsumableEffect("fx", 100, 0)];

    static HazardInfo Hazard(float radius = 4f, float interval = 0.2f) => new(radius, 0f, interval, 0, 0, null);

    static ActorState Actor(float health = 100f)
    {
        var state = new ActorState(10, null);
        state.SetHealth(health);
        return state;
    }

    static Func<IReadOnlyList<HazardVictim>> Victims(ActorState state, Vector3 at) =>
        () => [new HazardVictim(at, false, new ActorStateTarget(state), source => new ActorStateTarget(state, null, null, source))];

    [Fact]
    public void ALethalTickOfASourcedHazardNamesItsSource()
    {
        var field = new HazardField(new HostileHealth(), 1);
        var state = Actor(10f);
        Assert.NotNull(field.Spawn("h", Hazard(), Vector3.Zero, Burn, source: "actor:7").Id);
        field.Advance(0.25, Victims(state, Vector3.Zero));
        Assert.True(state.IsDead);
        Assert.Equal("actor:7", state.Killer);
    }

    [Fact]
    public void ALethalTickOfAnEnvironmentalHazardHasNoKiller()
    {
        var field = new HazardField(new HostileHealth(), 1);
        var state = Actor(10f);
        Assert.NotNull(field.Spawn("h", Hazard(), Vector3.Zero, Burn).Id);
        field.Advance(0.25, Victims(state, Vector3.Zero));
        Assert.True(state.IsDead);
        Assert.Null(state.Killer);
    }

    [Fact]
    public void TheSourceSurvivesASnapshotAndTheSaveSection()
    {
        var field = new HazardField(new HostileHealth(), 1);
        field.Spawn("h", Hazard(), Vector3.Zero, Burn, source: "actor:7");
        field.Spawn("g", Hazard(), Vector3.One, Burn);
        var rows = new List<HazardRow>();
        foreach (var entry in field.Snapshot()) rows.Add(entry.Row);

        Assert.Equal(new[] { "", "actor:7" }, rows.Select(row => row.Source).Order());
        Assert.Equal(rows, HazardSave.Read(HazardSave.Sections(rows))!);
    }

    [Fact]
    public void ANegativeRadiusIsRefusedAndAZeroRadiusIsNot()
    {
        var field = new HazardField(new HostileHealth(), 1);
        var refused = field.Spawn("h", Hazard(-4f), Vector3.Zero, Burn);
        Assert.Null(refused.Id);
        Assert.NotEmpty(refused.Skipped);
        Assert.Equal(0, field.Count);
        Assert.Null(field.Spawn("h", Hazard(float.NaN), Vector3.Zero, Burn).Id);
        Assert.NotNull(field.Spawn("h", Hazard(0f), Vector3.Zero, Burn).Id);
    }

    [Fact]
    public void ALongUpdateAppliesNoMoreThanTheStepClampOfTicks()
    {
        var field = new HazardField(new HostileHealth(), 1);
        var state = Actor();
        field.Spawn("h", Hazard(interval: 0.1f), Vector3.Zero, [new ConsumableEffect("fx", 10, 0)]);
        field.Advance(10, Victims(state, Vector3.Zero));
        Assert.Equal(98f, state.Health);
    }

    [Fact]
    public void AClearedFieldDamagesNobody()
    {
        var field = new HazardField(new HostileHealth(), 1);
        var state = Actor();
        field.Spawn("h", Hazard(), Vector3.Zero, Burn);
        field.Clear();
        field.Advance(0.25, Victims(state, Vector3.Zero));
        Assert.Equal(0, field.Count);
        Assert.Equal(100f, state.Health);
    }
}
