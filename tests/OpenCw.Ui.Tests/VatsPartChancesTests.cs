using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsPartChancesTests
{
    static readonly VatsChanceSettings Esm = new(
        NonActorBodyTerm: 50f, WeaponPerceptionOffset: 0.05f, BodyPartMultiplier: 0.075f, PerceptionMultiplier: 0.004f,
        WeaponDistanceMultiplier: 1.25f, DistanceTermMin: 0.05f, DistanceOffset: 0.05f, DistanceMultiplier: 3f, WeaponBaseMultiplier: 0.03f);

    static readonly VatsBodyPart Head = new("Head", "Head1", "Head", 1, 30, 2f, 20, VatsPartFlags.None);
    static readonly VatsBodyPart Torso = new("Torso", "Torso", "Chest", 0, 60, 1f, 75, VatsPartFlags.None);
    static readonly VatsBodyPart Root = new("Root", "Root", "Root", 18, 0, 1f, 0, VatsPartFlags.None);

    static VatsAim Aim(int type = 8) => new(500f, type, 1000f, null, 0.5f, 6f, 0);

    static VatsPartState State(float visibility = 0.8f, int ap = 20) => new(visibility, ap, 90, false, new VatsPoint(10, 20));

    [Fact]
    public void RowsCarryTheComputedPercentAndTheHostState()
    {
        var rows = VatsPartChances.Build(Esm, [Torso, Head], Aim(), _ => State());

        Assert.NotNull(rows);
        Assert.Equal(new[] { "0", "1" }, rows.Select(r => r.Id));
        Assert.Equal(new[] { "Torso", "Head" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { 74, 37 }, rows.Select(r => r.HitChance));
        Assert.All(rows, r => Assert.Equal(20, r.ApCost));
        Assert.All(rows, r => Assert.Equal(90, r.Health));
        Assert.All(rows, r => Assert.Equal(new VatsPoint(10, 20), r.Screen));
    }

    [Fact]
    public void ZeroToHitPartsShowZeroPercent()
    {
        var rows = VatsPartChances.Build(Esm, [Root], Aim(), _ => State());

        Assert.Equal(0, Assert.Single(rows!).HitChance);
    }

    [Fact]
    public void PartsTheHostGivesNoStateAreNotOffered()
    {
        var rows = VatsPartChances.Build(Esm, [Torso, Head], Aim(), p => p.PartType == 1 ? State() : null);

        Assert.Equal("Head", Assert.Single(rows!).Name);
    }

    [Fact]
    public void PerPartVisibilityScalesEachRow()
    {
        var rows = VatsPartChances.Build(Esm, [Torso, Torso with { PartType = 6 }], Aim(), p => State(p.PartType == 6 ? 0.4f : 0.8f));

        Assert.Equal(new[] { 74, 37 }, rows!.Select(r => r.HitChance));
    }

    [Fact]
    public void CrippledArmsLowerEveryRow()
    {
        var rows = VatsPartChances.Build(Esm, [Torso], Aim() with { CrippledArmCount = 1 }, _ => State());

        Assert.Equal(44, Assert.Single(rows!).HitChance);
    }

    [Fact]
    public void MeleeWeaponHasNoRows()
    {
        Assert.Null(VatsPartChances.Build(Esm, [Torso], Aim(type: 6), _ => State()));
    }

    [Fact]
    public void NoPartsGivesAnEmptyList()
    {
        Assert.Empty(VatsPartChances.Build(Esm, new List<VatsBodyPart>(), Aim(), _ => State())!);
    }

    [Fact]
    public void ToHitAboveTheByteRangeIsClampedNotWrapped()
    {
        var rows = VatsPartChances.Build(Esm, [Torso with { BaseHitChance = 300 }], Aim(), _ => State());
        var max = VatsPartChances.Build(Esm, [Torso with { BaseHitChance = 255 }], Aim(), _ => State());

        Assert.Equal(max!.Single().HitChance, rows!.Single().HitChance);
    }
}
