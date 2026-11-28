using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SceneReviewFixTests : HostFixture
{
    private const string Quest = "027411:Test.esm";
    private const string SceneA = "03467E:Test.esm";
    private const string SceneB = "03467F:Test.esm";

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void GetOwningQuestFeedsAQuestNative(HostKind kind)
    {
        var gate = TestHosts.Gate((SceneNativeAdapters.SystemKey, "ENGINE_POLICY"), (QuestStateMachine.LifecycleKey, "ENGINE_POLICY"),
            ("owner.engine_identity", "ENGINE_POLICY"), (QuestRuntime.FragmentTimingKey, "ENGINE_POLICY"));
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Quest", null, "Function Start() native", "Function Stop() native", "Function Reset() native", "Function CompleteQuest() native",
                "Function SetActive(Bool abActive) native", "Bool Function SetStage(Int aiStage) native"),
            TestHosts.Stub("Scene", null, "Function Start() native", "Function ForceStart() native", "Function Stop() native", "bool Function IsPlaying() native",
                "Quest Function GetOwningQuest() native"),
            TestHosts.Stub("Probe", null, "Scene Function MakeScene() global native"),
            new ScriptSpec("Caller", "Scriptname Caller\nBool Function Stage()\n  Return Probe.MakeScene().GetOwningQuest().SetStage(10)\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "MakeScene"), _ => PapyrusValue.FromObject(new EngineObjectReference(SceneA)));
        var world = new FakeWorld();
        var runtime = new SceneRuntime(key => Def(key), world);
        new SceneNativeAdapters(host, gate, runtime, world, _ => { }).Bind();
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { Quest });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Stage");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(result.Value.Flag);
        Assert.Equal(10, machine.StageOf(Quest));
    }

    [Fact]
    public void AFailingSceneIsNamedMarkedFailedRemovedAndDoesNotStopTheOtherScene()
    {
        var world = new FakeWorld { Throwing = "Boom" };
        var log = new List<string>();
        var runtime = new SceneRuntime(Def, world, log.Add);
        runtime.Start(SceneA);
        runtime.Start(SceneB);

        runtime.Tick(0.5);
        runtime.Tick(0.5);
        runtime.Tick(5);

        Assert.True(runtime.ReportOf(SceneA)!.Failed);
        Assert.False(runtime.ReportOf(SceneA)!.Completed);
        Assert.False(runtime.IsPlaying(SceneA));
        Assert.Equal(1, world.SpeakCalls["Boom"]);
        Assert.Contains(log, line => line.Contains(SceneA) && line.Contains("failed") && line.Contains("Dialogue#Boom"));
        Assert.True(runtime.ReportOf(SceneB)!.Completed);
        Assert.False(runtime.ReportOf(SceneB)!.Failed);
    }

    [Fact]
    public void AFailingEndStageFailsTheRunInsteadOfThrowingFromTick()
    {
        var world = new FakeWorld { ThrowOnStage = true };
        var runtime = new SceneRuntime(Def, world);
        runtime.Start(SceneB);

        runtime.Tick(5);
        runtime.Tick(5);

        Assert.True(runtime.ReportOf(SceneB)!.Failed);
        Assert.False(runtime.ReportOf(SceneB)!.Completed);
        Assert.False(runtime.IsPlaying(SceneB));
    }

    [Fact]
    public void StopStillRemovesTheRunWhenReleasingAPackageThrows()
    {
        var world = new FakeWorld { ThrowOnEnd = true };
        var runtime = new SceneRuntime(key => new SceneDef(key, Quest, new[] { new ScenePhaseDef(0, "", null, null) },
            new[] { new SceneActionDef("Package#1", SceneActionKind.Package, 1, 0, 0, true, "P0:Test.esm", 0), new SceneActionDef("Timer#2", SceneActionKind.Timer, -1, 0, 0, false, null, 99) }, null, null), world);
        runtime.Start(SceneA);
        runtime.Tick(1);

        Assert.True(runtime.Stop(SceneA));

        Assert.False(runtime.IsPlaying(SceneA));
    }

    [Fact]
    public void SceneKeysCompareAsFormKeysSoPluginCaseDoesNotSplitARun()
    {
        var runtime = new SceneRuntime(Def, new FakeWorld());

        Assert.Equal(SceneStartOutcome.Started, runtime.Start("03467E:Test.esm").Outcome);

        Assert.Equal(SceneStartOutcome.AlreadyPlaying, runtime.Start("03467E:TEST.ESM").Outcome);
        Assert.True(runtime.IsPlaying("03467e:test.esm"));
    }

    [Fact]
    public void ForcedAliasesOfAResetQuestAreDroppedAndOthersKept()
    {
        var resolver = new QuestAliasResolver(quest => new[] { new AliasFillDef(quest, 1, false, "T:Test.esm") }, _ => "fill");
        resolver.ForceRefTo(Quest, 1, "forced");
        resolver.ForceRefTo("000001:Other.esm", 1, "other");
        var gate = TestHosts.Gate((QuestStateMachine.LifecycleKey, "ENGINE_POLICY"));
        var machine = new QuestStateMachine(gate);
        machine.QuestReset += resolver.ClearQuest;
        machine.Apply(Quest, QuestNative.Start);
        Assert.Equal("forced", resolver.Resolve(Quest, 1));

        machine.Apply(Quest, QuestNative.Reset);

        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal("other", resolver.Resolve("000001:Other.esm", 1));
    }

    [Fact]
    public void StopAllReleasesEveryRunAndSetsNoEndStage()
    {
        var world = new FakeWorld();
        var runtime = new SceneRuntime(Def, world);
        runtime.Start(SceneA);
        runtime.Start(SceneB);
        runtime.Tick(0.5);

        runtime.StopAll();

        Assert.False(runtime.IsPlaying(SceneA));
        Assert.False(runtime.IsPlaying(SceneB));
        Assert.True(runtime.ReportOf(SceneA)!.Stopped);
        Assert.Empty(world.Stages);
        Assert.Equal(2, world.Stops.Count);
        runtime.Tick(5);
        Assert.Empty(world.Stages);
    }

    [Fact]
    public void ClearAllDropsForcedAliasesAndTheFillCache()
    {
        var calls = 0;
        var resolver = new QuestAliasResolver(quest => { calls++; return new[] { new AliasFillDef(quest, 1, false, "T:Test.esm") }; }, _ => "fill");
        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal(1, calls);
        resolver.ForceRefTo(Quest, 2, "forced");
        Assert.Equal("forced", resolver.Resolve(Quest, 2));

        resolver.ClearAll();

        Assert.Null(resolver.Resolve(Quest, 2));
        Assert.Equal(2, calls);
        resolver.InvalidateFills();
        resolver.Resolve(Quest, 1);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AChangedEnvironmentVersionDropsTheCachedFillsAndAnEmptyListIsNeverCached()
    {
        object environment = new();
        var calls = 0;
        var empty = true;
        var resolver = new QuestAliasResolver(quest => { calls++; return empty ? Array.Empty<AliasFillDef>() : new[] { new AliasFillDef(quest, 1, false, "T:Test.esm") }; }, _ => "fill", () => environment);

        Assert.Null(resolver.Resolve(Quest, 1));
        empty = false;
        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal(2, calls);
        environment = new object();
        Assert.Equal("fill", resolver.Resolve(Quest, 1));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AForcedAliasResolvesWithoutTouchingTheEnvironmentVersionOrFills()
    {
        var resolver = new QuestAliasResolver(_ => throw new InvalidOperationException("no records"), _ => "fill", () => throw new InvalidOperationException("no records"));
        resolver.ForceRefTo(Quest, 1, "forced");

        Assert.Equal("forced", resolver.Resolve(Quest, 1));
    }

    [Fact]
    public void QuestKeysOfDifferentCaseShareOneForcedSlot()
    {
        var resolver = new QuestAliasResolver(quest => new[] { new AliasFillDef(quest, 1, false, "T:Test.esm") }, _ => "fill");
        resolver.ForceRefTo("027411:Test.esm", 1, "forced");

        Assert.Equal("forced", resolver.Resolve("027411:TEST.ESM", 1));
        resolver.Clear("027411:TEST.ESM", 1);
        Assert.Null(resolver.Resolve("027411:test.esm", 1));
        resolver.ForceRefTo("027411:Test.esm", 1, "again");
        resolver.ClearQuest("027411:TEST.ESM");
        Assert.Equal("fill", resolver.Resolve("027411:Test.esm", 1));
    }

    private static SceneDef TwoPhases(string key, params SceneActionDef[] actions) =>
        new(key, Quest, new[] { new ScenePhaseDef(0, "", null, null), new ScenePhaseDef(1, "", null, null) }, actions, null, 10);

    private static SceneActionDef Line(string topic, int phase) => new($"Dialogue#{topic}", SceneActionKind.Dialogue, 1, phase, phase, false, topic, 0);

    [Fact]
    public void AFrameHitchStartsTheNextPhaseAtTheClockOfTheFrameThatFinishedTheLast()
    {
        var runtime = new SceneRuntime(key => TwoPhases(key, Line("One", 0), Line("Two", 1)), new FakeWorld());
        runtime.Start(SceneA);
        runtime.Tick(0.1);

        runtime.Tick(10);

        var phase = runtime.Trace.Single(e => e.Kind == "phase-start" && e.Phase == 1);
        Assert.Equal(runtime.Now, phase.At, 6);
        Assert.False(runtime.ReportOf(SceneA)!.Completed);
        runtime.Tick(1.5);
        Assert.True(runtime.ReportOf(SceneA)!.Completed);
    }

    [Fact]
    public void AStoppedSceneStartsAgainCleanWithAFreshReport()
    {
        var world = new FakeWorld();
        var runtime = new SceneRuntime(key => TwoPhases(key, Line("One", 0), Line("Two", 1)), world);
        runtime.Start(SceneA);
        runtime.Tick(0.5);
        Assert.True(runtime.Stop(SceneA));
        var first = runtime.ReportOf(SceneA)!;

        Assert.Equal(SceneStartOutcome.Started, runtime.Start(SceneA).Outcome);
        runtime.Tick(0.5);
        runtime.Tick(5);
        runtime.Tick(5);

        var second = runtime.ReportOf(SceneA)!;
        Assert.NotSame(first, second);
        Assert.True(first.Stopped);
        Assert.True(second.Completed);
        Assert.False(second.Stopped);
        Assert.Equal(2, second.Spoken);
        Assert.Equal(new[] { 10 }, world.Stages);
    }

    [Fact]
    public void AFailureInBeginMidPhaseReleasesTheEarlierActionAndSetsNoStage()
    {
        var world = new FakeWorld { Throwing = "Boom" };
        var runtime = new SceneRuntime(key => TwoPhases(key, Line("One", 0), Line("Boom", 0)), world);
        runtime.Start(SceneA);

        runtime.Tick(0.5);
        runtime.Tick(5);

        var report = runtime.ReportOf(SceneA)!;
        Assert.True(report.Failed);
        Assert.False(runtime.IsPlaying(SceneA));
        Assert.Equal(1, report.Spoken);
        Assert.Equal(new[] { $"scene {SceneA} action Dialogue#One" }, world.Stops);
        Assert.Empty(world.Stages);
        Assert.Equal(1, world.SpeakCalls["Boom"]);
    }

    private static SceneDef? Def(string key) => key.ToUpperInvariant() switch
    {
        "03467E:TEST.ESM" => new SceneDef(key, Quest, new[] { new ScenePhaseDef(0, "", null, null) },
            new[] { new SceneActionDef("Dialogue#Boom", SceneActionKind.Dialogue, 1, 0, 0, false, "Boom", 0) }, null, 10),
        "03467F:TEST.ESM" => new SceneDef(key, Quest, new[] { new ScenePhaseDef(0, "", null, null) },
            new[] { new SceneActionDef("Dialogue#Ok", SceneActionKind.Dialogue, 1, 0, 0, false, "Ok", 0) }, null, 10),
        _ => null,
    };

    private sealed class FakeWorld : ISceneWorld
    {
        public string? Throwing { get; init; }

        public bool ThrowOnStage { get; init; }

        public bool ThrowOnEnd { get; init; }

        public Dictionary<string, int> SpeakCalls { get; } = new();

        public List<string> Stops { get; } = new();

        public List<int> Stages { get; } = new();

        public string? ResolveActor(string quest, int alias) => "A1:Test.esm";

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            SpeakCalls[topic] = SpeakCalls.GetValueOrDefault(topic) + 1;
            if (topic == Throwing) throw new InvalidOperationException("voice exploded");
            return new SpeechResult(SpeechStatus.Spoken, 1, "");
        }

        public void StopSpeech(string context, string actor) => Stops.Add(context);

        public PackageResult BeginPackage(string context, string package, string? actor) => new(true, "");

        public void EndPackage(string package, string? actor)
        {
            if (ThrowOnEnd) throw new InvalidOperationException("package end exploded");
        }

        public bool SetStage(string quest, int stage)
        {
            Stages.Add(stage);
            return ThrowOnStage ? throw new InvalidOperationException("stage exploded") : true;
        }
    }
}
