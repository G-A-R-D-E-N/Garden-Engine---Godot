using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SpawnIdentityTests
{
    private const string ListKey = "000900:Pick.esm";
    private const string Low = "000901:Pick.esm";
    private const string High = "000902:Pick.esm";

    private sealed record Entry(ushort MinLevel, string Npc);

    private static readonly Entry[] LeveledList = [new(1, Low), new(10, High)];

    private static string? Fixture(ushort level, ulong seed) =>
        LeveledList.Where(entry => entry.MinLevel <= level).OrderByDescending(entry => entry.MinLevel).Select(entry => entry.Npc).FirstOrDefault();

    private static ScriptAttachment Script(string name, string value) =>
        new(name, [new ScriptValue("Mode", ScriptValueKind.String, value, null)]);

    private static string Source(params string[] path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SpawnIdentityTests).Assembly.Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "opencw", "scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "opencw" }.Concat(path).ToArray()));
    }

    [Fact]
    public void TheStreamerResolvesLeveledMarkersBeforeItAttachesTheCellAndTheSpawnerDoesToo()
    {
        var streamer = Source("scripts", "WorldspaceStreamer.Lifecycle.cs");
        int resolve = streamer.IndexOf("NpcPuppetSpawner.ResolveLeveled(inst);", StringComparison.Ordinal);
        Assert.True(resolve >= 0 && resolve < streamer.IndexOf("AttachCell(slot.ResPath, inst);", StringComparison.Ordinal));
        Assert.Contains("ResolveLeveled(markers);", Source("scripts", "Actors", "NpcPuppetSpawner.cs"));
    }

    [Theory]
    [InlineData("scripts/Actors/RadicleRelationSource.cs", "actor.NpcKey")]
    [InlineData("Services/Navigation/RadicleNavWalk.cs", "actor.NpcKey")]
    [InlineData("scripts/UI/Bootstrap/PlayerHudBinding.cs", "actor.Identity.NpcKey")]
    [InlineData("scripts/Actors/NpcDialogue.cs", "NpcSnapshots.BaseKeyOf(reference)")]
    [InlineData("scripts/Actors/ActorRegistry.cs", "SpawnIdentity.KeyOf(owner.BaseFormKey")]
    public void ReadersOfRecordsAboutAnActorUseTheChosenKeyNotTheLeveledListKey(string file, string expected)
    {
        Assert.Contains(expected, Source(file.Split('/')));
    }

    [Fact]
    public void AMarkerThatIsNotLeveledKeepsItsBaseAndNeverAsksForAPick()
    {
        int asked = 0;
        var choice = SpawnIdentity.Choose("Npc", Low, null, () => { asked++; return High; });
        Assert.Equal(new SpawnChoice(Low, "Npc", false), choice);
        Assert.Equal(0, asked);
        Assert.Equal(new SpawnChoice(ListKey, "Container", false), SpawnIdentity.Choose("Container", ListKey, null, null));
    }

    [Fact]
    public void ALeveledMarkerTakesTheNpcTheListPicksAtTheSpawnLevel()
    {
        var choice = SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => Fixture(1, 7));
        Assert.Equal(new SpawnChoice(Low, "Npc", true), choice);
        Assert.Equal(High, SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => Fixture(12, 7))!.Value.NpcKey);
    }

    [Fact]
    public void ALeveledMarkerWhoseListGivesNothingHasNoChoice()
    {
        Assert.Null(SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => null));
        Assert.Null(SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => ""));
        Assert.Null(SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => Fixture(0, 1)));
    }

    [Fact]
    public void ALeveledMarkerWithNoPickSeamKeepsTheListKeyAndTypeSoTheSpawnerStubsIt()
    {
        Assert.Equal(new SpawnChoice(ListKey, SpawnIdentity.LeveledType, false), SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, null));
    }

    [Fact]
    public void APinnedChoiceSurvivesALevelUpThatWouldHavePickedAnotherNpc()
    {
        var first = SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => Fixture(1, 7))!.Value;
        int asked = 0;
        var later = SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, first.NpcKey, () => { asked++; return Fixture(12, 7); })!.Value;
        Assert.Equal(first, later);
        Assert.Equal(0, asked);
        Assert.Equal(High, SpawnIdentity.Choose(SpawnIdentity.LeveledType, ListKey, null, () => Fixture(12, 7))!.Value.NpcKey);
    }

    [Fact]
    public void TheChosenKeyIsTheResolvedNpcWhenThereIsOneAndTheBaseOtherwise()
    {
        Assert.Equal(High, SpawnIdentity.KeyOf(ListKey, High));
        Assert.Equal(ListKey, SpawnIdentity.KeyOf(ListKey, ""));
        Assert.Equal(ListKey, SpawnIdentity.KeyOf(ListKey, null));
    }

    [Fact]
    public void TheChosenNpcScriptsAttachBesideTheReferencesOwnAndTheOwnCopyWinsByName()
    {
        string own = ScriptAttachments.ToMetadataJson([Script("PlacedOnly", "own"), Script("Shared", "own")]);
        var merged = ScriptAttachments.Parse(SpawnIdentity.MergeScripts(own, [Script("shared", "npc"), Script("NpcOnly", "npc")]));
        Assert.Equal(["NpcOnly", "PlacedOnly", "Shared"], merged.Select(script => script.Name).ToArray());
        Assert.Equal("own", merged.Single(script => script.Name == "Shared").Values.Single().Value);
    }

    [Fact]
    public void MergingTheChosenScriptsTwiceChangesNothingAndNoScriptsAtAllIsNull()
    {
        IReadOnlyList<ScriptAttachment> chosen = [Script("NpcOnly", "npc")];
        string once = SpawnIdentity.MergeScripts(null, chosen)!;
        Assert.Equal(once, SpawnIdentity.MergeScripts(once, chosen));
        Assert.Null(SpawnIdentity.MergeScripts(null, Array.Empty<ScriptAttachment>()));
        Assert.Null(SpawnIdentity.MergeScripts("", Array.Empty<ScriptAttachment>()));
    }
}
