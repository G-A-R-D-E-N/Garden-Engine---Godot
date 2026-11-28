using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Papyrus.Tests;

[Trait("Category", "RealData")]
public sealed class QuestFragmentRealTests
{
    private const string MeetQuest = "027411:Fallout4.esm";
    private const string MeetScene = "03467E:Fallout4.esm";

    private static readonly List<string> Removed = new();

    private sealed class World : ISceneWorld
    {
        public string? ResolveActor(string quest, int alias) => null;
        public SpeechResult Speak(string context, string topic, string? actor) => new(SpeechStatus.Spoken, 1, "");
        public void StopSpeech(string context, string actor) { }
        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "override absent");
        public void EndPackage(string package, string? actor) { }
        public bool SetStage(string quest, int stage) => true;
    }

    [RealDataFact]
    public void TheMacCreadyMeetStageFiveFragmentRunsAndStartsTheMeetScene()
    {
        var data = RegistryCorpusRealTests.DataDirectory!;
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(data, "Fallout4.esm") });
        var input = RadicleQuestStartup.Build(environment).Single(row => row.Quest == MeetQuest);
        var archives = Fo4Data.Archives(data);
        using var scripts = OpenedScriptSet.Open(new[] { data }, archives);
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(scripts, data, cache.Root, new[] { input });
        using var session = GameSession.Load(cache.Root);
        session.BindQuestNatives(session.QuestStartup.Select(row => row.Quest));
        var world = new World();
        var runtime = new SceneRuntime(key => RadicleSceneSource.Read(environment, key), world);
        new SceneNativeAdapters(session.Host, session.Gate, runtime, world, _ => { }).Bind();
        new AliasNativeAdapters(session.Host, session.Gate, new QuestAliasResolver(quest => RadicleSceneSource.ReadAliasFills(environment, quest), fill => fill.Target), key => new EngineObjectReference(key) { ScriptType = "Actor" }).Bind();
        new FactionNativeAdapters(session.Host, session.Gate, _ => false, _ => -1, (_, _) => true, _ => true, new LoggingActorFactions(Removed)).Bind();
        ((RadicleScriptHost)session.Host).PublishUnboundRefusals();
        Assert.True(session.StartNewGame().Applied);
        for (var pump = 0; pump < 8; pump++) { session.RunQueuedFragments(); session.Host.RunStep(PapyrusHostDefaults.StepBudget); }
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(MeetQuest));

        Assert.True(session.QuestStages.Apply(MeetQuest, QuestNative.SetStage, 5));
        var ran = session.RunQueuedFragments();

        Assert.True(session.FragmentFailures.Count == 0, string.Join("; ", session.FragmentFailures));
        Assert.Equal(1, ran);
        Assert.True(runtime.IsPlaying(MeetScene));
        Assert.Single(Removed);
    }

    private sealed class LoggingActorFactions(List<string> removed) : IActorFactions
    {
        public int? Rank(string actor, string faction) => -1;

        public void SetRank(string actor, string faction, int rank) { }

        public void Leave(string actor, string faction) => removed.Add(actor + " " + faction);
    }
}
