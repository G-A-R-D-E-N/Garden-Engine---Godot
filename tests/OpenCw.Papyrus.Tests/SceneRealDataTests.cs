using OpenCommonwealth.Services.Papyrus.Scenes;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Papyrus.Tests;

[Trait("Category", "RealData")]
public sealed class SceneRealDataTests
{
    private const string Scene = "03467E:Fallout4.esm";
    private const string Quest = "027411:Fallout4.esm";

    [RealDataFact]
    public void TheGoodneighborStartSceneDecodesIntoTwelvePhasesWithTheQuestEndStage()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });

        var def = RadicleSceneSource.Read(env, Scene)!;

        Assert.NotNull(def);
        Assert.Equal(Quest, def.Quest);
        Assert.Equal(12, def.Phases.Count);
        Assert.Null(def.Refusal);
        Assert.Null(def.ConditionRefusal);
        Assert.Null(def.BeginStage);
        Assert.Equal(10, def.EndStage);
        Assert.Equal(15, def.Actions.Count(a => a.Kind == SceneActionKind.Dialogue));
        Assert.Equal(4, def.Actions.Count(a => a.Kind == SceneActionKind.Package && a.IgnoreForCompletion));
        Assert.Equal(new[] { 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 7, 8, 9, 10, 11 }, def.Actions.Where(a => a.Kind == SceneActionKind.Dialogue).Select(a => a.StartPhase).OrderBy(p => p));
        Assert.All(def.Actions.Where(a => a.Kind == SceneActionKind.Dialogue), a => Assert.Matches(@"^[0-9A-F]{6}:Fallout4\.esm$", a.Target!));
        Assert.Contains(def.Actions, a => a.Target == "034671:Fallout4.esm" && a.StartPhase == 0);
        Assert.Contains(def.Actions, a => a.Target == "034666:Fallout4.esm" && a.StartPhase == 11);
        Assert.Equal(new[] { 1, 2, 3 }, def.Actions.Select(a => a.Alias).Distinct().OrderBy(a => a));
    }

    [RealDataFact]
    public void TheThreeSceneActorsAreUniqueActorAliasesOfTheQuest()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });

        var fills = RadicleSceneSource.ReadAliasFills(env, Quest);

        Assert.Contains(fills, f => f.AliasId == 1 && !f.Specific);
        Assert.Contains(fills, f => f.AliasId == 2 && !f.Specific);
        Assert.Contains(fills, f => f.AliasId == 3 && !f.Specific);
    }

    [RealDataFact]
    public void ThreeSceneTopicsHaveNoInfoAndTheRestHaveOne()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        var def = RadicleSceneSource.Read(env, Scene)!;

        var counts = def.Actions.Where(a => a.Kind == SceneActionKind.Dialogue).ToDictionary(a => a.Target!, a =>
        {
            RadicleSceneSource.TryParseKey(a.Target!, out var key);
            return env.TopicChildren(env.Winner(key)!.Value).Count;
        });

        Assert.Equal(new[] { "01B29B:Fallout4.esm", "01B29C:Fallout4.esm", "065E70:Fallout4.esm" }, counts.Where(c => c.Value == 0).Select(c => c.Key).OrderBy(k => k));
        Assert.All(counts.Where(c => c.Value != 0), c => Assert.Equal(1, c.Value));
    }

    [RealDataFact]
    public void TheMacCreadyMeetSceneIsHostedByTheDialogueMenuAndNotRefusedForItsPlayerDialogue()
    {
        const string Meet = "066DC3:Fallout4.esm";
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });

        var def = RadicleSceneSource.Read(env, Meet)!;
        var runtime = new SceneRuntime(key => key == Meet ? def : null, new NoWorld());

        Assert.Null(def.Refusal);
        Assert.Equal(5, def.Actions.Count(a => a.Kind == SceneActionKind.PlayerDialogue));
        Assert.Contains("player dialogue action", def.HostRefusal);
        Assert.Contains("package action package#20 spans phases", def.HostRefusal);
        Assert.True(def.PhaseConditionsOnly);
        Assert.Equal(SceneStartOutcome.Refused, runtime.Start(Meet).Outcome);
        Assert.Equal(SceneStartOutcome.Started, runtime.StartHosted(Meet).Outcome);
    }

    private sealed class NoWorld : ISceneWorld
    {
        public string? ResolveActor(string quest, int alias) => null;

        public SpeechResult Speak(string context, string topic, string? actor) => new(SpeechStatus.NoLine, 0, "");

        public void StopSpeech(string context, string actor)
        {
        }

        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "");

        public void EndPackage(string package, string? actor)
        {
        }

        public bool SetStage(string quest, int stage) => true;
    }
}
