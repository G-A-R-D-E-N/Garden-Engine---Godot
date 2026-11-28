using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BodyPartRaceTests
{
    sealed class FakeNpcs : IBodyPartNpcRecords
    {
        public Dictionary<string, uint> Flags { get; } = new();

        public Dictionary<string, string> Owners { get; } = new();

        public Dictionary<string, string> Races { get; } = new();

        public uint? TemplateFlags(string npcKey) => Flags.TryGetValue(npcKey, out var flags) ? flags : null;

        public string? TraitsOwner(string npcKey) => Owners.GetValueOrDefault(npcKey);

        public string? Race(string npcKey) => Races.GetValueOrDefault(npcKey);
    }

    static FakeNpcs Templated(uint flags)
    {
        var npcs = new FakeNpcs();
        npcs.Flags["N"] = flags;
        npcs.Owners["N"] = "T";
        npcs.Races["N"] = "RaceN";
        npcs.Races["T"] = "RaceT";
        return npcs;
    }

    [Fact]
    public void ClearTraitsFlagUsesTheNpcsOwnRace()
    {
        Assert.Equal("RaceN", BodyPartRace.Resolve("N", Templated(0)));
    }

    [Fact]
    public void SetTraitsFlagUsesTheTemplateOwnersRace()
    {
        Assert.Equal("RaceT", BodyPartRace.Resolve("N", Templated(BodyPartRace.TraitsFlag)));
    }

    [Fact]
    public void OtherTemplateBitsDoNotRedirectTheRace()
    {
        Assert.Equal("RaceN", BodyPartRace.Resolve("N", Templated((1u << 2) | (1u << 12))));
    }

    [Fact]
    public void UndecidedTemplateChainGivesNoRaceEvenWhenTheNpcHasOne()
    {
        var npcs = new FakeNpcs();
        npcs.Flags["N"] = BodyPartRace.TraitsFlag;
        npcs.Races["N"] = "RaceN";

        Assert.Null(BodyPartRace.Resolve("N", npcs));
    }

    [Fact]
    public void UnreadableFlagsAskTheTemplateChain()
    {
        var npcs = new FakeNpcs();
        npcs.Owners["N"] = "N";
        npcs.Races["N"] = "RaceN";
        Assert.Equal("RaceN", BodyPartRace.Resolve("N", npcs));

        npcs.Owners["N"] = "T";
        npcs.Races["T"] = "RaceT";
        Assert.Equal("RaceT", BodyPartRace.Resolve("N", npcs));
    }

    [Fact]
    public void OwnerWithoutARaceGivesNoRace()
    {
        var npcs = new FakeNpcs();
        npcs.Flags["N"] = BodyPartRace.TraitsFlag;
        npcs.Owners["N"] = "T";

        Assert.Null(BodyPartRace.Resolve("N", npcs));
    }
}
