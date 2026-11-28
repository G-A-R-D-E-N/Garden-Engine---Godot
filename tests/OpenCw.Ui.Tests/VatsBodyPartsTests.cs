using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsBodyPartsTests
{
    sealed class FakeSource : IBodyPartSource
    {
        public Dictionary<string, IReadOnlyList<VatsBodyPartData>> Rows { get; } = new();

        public IReadOnlyList<VatsBodyPartData>? Parts(string actorBaseKey) => Rows.GetValueOrDefault(actorBaseKey);
    }

    static VatsBodyPartData Row(string? name, string? target, byte hit = 50, float mult = 1f, byte flags = 0, byte type = 0, byte health = 100) =>
        new(name, "Node_" + name, target, type, mult, flags, health, hit);

    [Fact]
    public void ListsPartsWithTargetNameChanceAndMultiplier()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("Head", "Head1", hit: 60, mult: 2.5f, type: 1, health: 40), Row("Torso", "Torso", hit: 95)];

        var parts = VatsBodyParts.Targetable(source, "A");

        Assert.Equal(2, parts.Count);
        Assert.Equal("Head", parts[0].Name);
        Assert.Equal("Head1", parts[0].Target);
        Assert.Equal(60, parts[0].BaseHitChance);
        Assert.Equal(2.5f, parts[0].DamageMult);
        Assert.Equal(1, parts[0].PartType);
        Assert.Equal(40, parts[0].HealthPercent);
        Assert.Equal(95, parts[1].BaseHitChance);
    }

    [Fact]
    public void ReadsSeverableExplodableAndCrippleBits()
    {
        var source = new FakeSource();
        source.Rows["A"] =
        [
            Row("Arm", "Arm", flags: (byte)(VatsPartFlags.Severable | VatsPartFlags.OnCripple), type: 6),
            Row("Leg", "Leg", flags: (byte)VatsPartFlags.Explodable, type: 10),
            Row("Torso", "Torso", flags: 0, type: 0),
        ];

        var parts = VatsBodyParts.Targetable(source, "A");

        Assert.True(parts[0].Severable);
        Assert.True(parts[0].OnCripple);
        Assert.False(parts[0].Explodable);
        Assert.True(parts[1].Explodable);
        Assert.False(parts[1].Severable);
        Assert.False(parts[1].OnCripple);
        Assert.Equal(VatsPartFlags.None, parts[2].Flags);
    }

    [Fact]
    public void FlagBitsMatchTheDocumentedPositions()
    {
        Assert.Equal(1, (byte)VatsPartFlags.Severable);
        Assert.Equal(8, (byte)VatsPartFlags.Explodable);
        Assert.Equal(32, (byte)VatsPartFlags.OnCripple);
        Assert.Equal(128, (byte)VatsPartFlags.ShowCrippleGeometry);
    }

    [Fact]
    public void PartsWithoutATargetStringAreNotTargetable()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("Root", null, type: 18), Row("Camera", "  ", type: 21), Row("Head", "Head1", type: 1)];

        var parts = VatsBodyParts.Targetable(source, "A");

        Assert.Single(parts);
        Assert.Equal("Head1", parts[0].Target);
    }

    [Fact]
    public void MissingNameFallsBackToTheTarget()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row(null, "LeftArm1", type: 6)];

        Assert.Equal("LeftArm1", VatsBodyParts.Targetable(source, "A")[0].Name);
    }

    [Fact]
    public void UnknownActorOrEmptyDataYieldsNoParts()
    {
        var source = new FakeSource();
        source.Rows["Empty"] = [];

        Assert.Empty(VatsBodyParts.Targetable(source, "Missing"));
        Assert.Empty(VatsBodyParts.Targetable(source, "Empty"));
    }

    [Fact]
    public void KeepsRecordOrder()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("B", "B", type: 3), Row("A", "A", type: 1), Row("C", "C", type: 2)];

        Assert.Equal(new[] { "B", "A", "C" }, VatsBodyParts.Targetable(source, "A").Select(p => p.Name));
    }

    [Fact]
    public void LaterPartOfTheSameTypeReplacesTheEarlierOne()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("First", "Head1", hit: 10, type: 1), Row("Torso", "Torso", type: 0), Row("Second", "Head1", hit: 90, type: 1)];

        var parts = VatsBodyParts.Targetable(source, "A");

        Assert.Equal(new[] { "Torso", "Second" }, parts.Select(p => p.Name));
        Assert.Equal(90, parts[1].BaseHitChance);
    }

    [Fact]
    public void LaterEmptyTargetPartOfATypeHidesTheEarlierOne()
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("Head", "Head1", type: 1), Row("Torso", "Torso", type: 0), Row("Head2", "  ", type: 1)];

        var parts = VatsBodyParts.Targetable(source, "A");

        Assert.Equal(new[] { "Torso" }, parts.Select(p => p.Name));
        Assert.DoesNotContain(parts, p => p.PartType == 1);
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    [InlineData(255, false)]
    public void PartTypesOutsideTheTwentySixSlotsAreDropped(int type, bool kept)
    {
        var source = new FakeSource();
        source.Rows["A"] = [Row("P", "P", type: (byte)type)];

        Assert.Equal(kept ? 1 : 0, VatsBodyParts.Targetable(source, "A").Count);
    }
}
