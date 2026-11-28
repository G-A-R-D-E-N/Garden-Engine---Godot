using OpenCommonwealth.Services.Papyrus.Scenes;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SceneRuntimeTests
{
    private const string Quest = "027411:Test.esm";
    private const string Scene = "03467E:Test.esm";

    private sealed class FakeWorld : ISceneWorld
    {
        public Dictionary<int, string> Actors { get; } = new() { [1] = "A1:Test.esm", [2] = "A2:Test.esm", [3] = "A3:Test.esm" };

        public Dictionary<string, SpeechResult> Lines { get; } = new();

        public List<string> Calls { get; } = new();

        public bool PackagesApply { get; set; } = true;

        public string? ResolveActor(string quest, int alias) => Actors.GetValueOrDefault(alias);

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            Calls.Add($"speak {topic} {actor}");
            return Lines.GetValueOrDefault(topic) ?? new SpeechResult(SpeechStatus.Spoken, 1.0, "");
        }

        public void StopSpeech(string context, string actor) => Calls.Add($"stop-speech {actor}");

        public PackageResult BeginPackage(string context, string package, string? actor)
        {
            Calls.Add($"package {package} {actor}");
            return new PackageResult(PackagesApply, PackagesApply ? "" : "override absent");
        }

        public void EndPackage(string package, string? actor) => Calls.Add($"package-end {package} {actor}");

        public bool SetStage(string quest, int stage)
        {
            Calls.Add($"stage {quest} {stage}");
            return true;
        }
    }

    private static SceneActionDef Say(string topic, int alias, int phase, bool ignore = false) =>
        new($"Dialogue#{topic}", SceneActionKind.Dialogue, alias, phase, phase, ignore, topic, 0);

    private static SceneDef Def(int phases, IReadOnlyList<SceneActionDef> actions, int? begin = null, int? end = 10, string? refusal = null, string? condition = null) =>
        new(Scene, Quest, Enumerable.Range(0, phases).Select(i => new ScenePhaseDef(i, "", null, null)).ToArray(), actions, begin, end, refusal, condition);

    private static SceneRuntime Runtime(SceneDef def, FakeWorld world) => new(key => key == Scene ? def : null, world);

    private static void Run(SceneRuntime runtime, double seconds, double step = 0.25)
    {
        for (var elapsed = 0.0; elapsed < seconds; elapsed += step) runtime.Tick(step);
    }

    [Fact]
    public void TraceRetainsOnlyNewestEventsWhileLogReceivesAllEvents()
    {
        var world = new FakeWorld();
        var def = Def(1, Array.Empty<SceneActionDef>());
        var logged = new List<string>();
        var runtime = new SceneRuntime(key => key == Scene ? def : null, world, logged.Add);
        for (var i = 0; i < 600; i++)
        {
            runtime.Start(Scene);
            runtime.Stop(Scene);
        }

        Assert.Equal(1200, logged.Count);
        Assert.Equal(SceneRuntime.TraceCapacity, runtime.Trace.Count);
        Assert.Equal(1200L - SceneRuntime.TraceCapacity, runtime.TraceDropped);
        Assert.Equal(logged.Skip(logged.Count - SceneRuntime.TraceCapacity), runtime.Trace.Select(e => e.ToString()));
    }

    [Fact]
    public void TraceSinceStillFindsEventsAfterTheRingBufferDropsOlderOnes()
    {
        var world = new FakeWorld();
        var def = Def(1, Array.Empty<SceneActionDef>());
        var runtime = new SceneRuntime(key => key == Scene ? def : null, world);
        runtime.Start(Scene);
        var marker = runtime.Now;
        for (var i = 0; i < SceneRuntime.TraceCapacity + 40; i++)
        {
            runtime.Start(Scene);
            runtime.Stop(Scene);
        }

        Assert.True(runtime.TraceDropped > 0);
        Assert.Contains(runtime.TraceSince(marker), e => e.Kind == "start");
    }

    [Fact]
    public void PhasesRunInOrderAndTheEndStageIsSetOnceAfterTheLastLine()
    {
        var world = new FakeWorld();
        var runtime = Runtime(Def(3, new[] { Say("T0", 1, 0), Say("T1", 2, 1), Say("T2", 3, 2) }), world);

        Assert.Equal(SceneStartOutcome.Started, runtime.Start(Scene).Outcome);
        Assert.Empty(world.Calls);
        runtime.Tick(0);
        Assert.Equal(new[] { "speak T0 A1:Test.esm" }, world.Calls);
        Run(runtime, 1.5);
        Assert.Equal(new[] { "speak T0 A1:Test.esm", "speak T1 A2:Test.esm" }, world.Calls);
        Assert.True(runtime.IsPlaying(Scene));
        Run(runtime, 3);

        Assert.Equal(new[] { "speak T0 A1:Test.esm", "speak T1 A2:Test.esm", "speak T2 A3:Test.esm", $"stage {Quest} 10" }, world.Calls);
        Assert.False(runtime.IsPlaying(Scene));
        Assert.True(runtime.ReportOf(Scene)!.Completed);
        Assert.Equal(3, runtime.ReportOf(Scene)!.Spoken);
        Assert.Equal(new[] { 0, 1, 2 }, runtime.Trace.Where(e => e.Kind == "phase-start").Select(e => e.Phase));
    }

    [Fact]
    public void ParallelLinesInOnePhaseWaitForTheLongestAndAnIgnoredPackageDoesNotBlock()
    {
        var world = new FakeWorld();
        world.Lines["Short"] = new SpeechResult(SpeechStatus.Spoken, 0.5, "");
        world.Lines["Long"] = new SpeechResult(SpeechStatus.Spoken, 2.0, "");
        var package = new SceneActionDef("Package#P", SceneActionKind.Package, 3, 0, 1, true, "PKG:Test.esm", 0);
        var runtime = Runtime(Def(2, new[] { Say("Short", 1, 0), Say("Long", 2, 0), package, Say("Next", 1, 1) }), world);
        runtime.Start(Scene);

        Run(runtime, 1.5);
        Assert.DoesNotContain("speak Next A1:Test.esm", world.Calls);
        Run(runtime, 2.5);

        Assert.Contains("speak Next A1:Test.esm", world.Calls);
        Assert.Equal(new[] { "package PKG:Test.esm A3:Test.esm" }, world.Calls.Where(c => c.StartsWith("package ")));
        Assert.True(world.Calls.IndexOf("speak Next A1:Test.esm") < world.Calls.IndexOf("package-end PKG:Test.esm A3:Test.esm"));
        Assert.Equal(1, runtime.ReportOf(Scene)!.PackagesApplied);
    }

    [Fact]
    public void AnIgnoreForCompletionLineDoesNotHoldItsPhase()
    {
        var world = new FakeWorld();
        world.Lines["Bark"] = new SpeechResult(SpeechStatus.Spoken, 30.0, "");
        world.Lines["Main"] = new SpeechResult(SpeechStatus.Spoken, 1.0, "");
        var runtime = Runtime(Def(2, new[] { Say("Bark", 1, 0, ignore: true), Say("Main", 2, 0), Say("Next", 3, 1) }), world);
        runtime.Start(Scene);

        Run(runtime, 2.0);

        Assert.Contains("speak Next A3:Test.esm", world.Calls);
    }

    [Fact]
    public void AMissingAudioLineStillHoldsItsPhaseForTheResponsesThatPlay()
    {
        var world = new FakeWorld();
        world.Lines["Partial"] = new SpeechResult(SpeechStatus.MissingAudio, 3.0, "one response has no voice file");
        var runtime = Runtime(Def(2, new[] { Say("Partial", 1, 0), Say("Next", 2, 1) }), world);
        runtime.Start(Scene);

        Run(runtime, 2.0);
        Assert.DoesNotContain("speak Next A2:Test.esm", world.Calls);
        Run(runtime, 2.0);

        Assert.Contains("speak Next A2:Test.esm", world.Calls);
        Assert.Equal(1, runtime.ReportOf(Scene)!.MissingAudio);
        Assert.Equal(1, runtime.ReportOf(Scene)!.Spoken);
    }

    [Fact]
    public void EmptyMissingAndAbsentLinesAdvanceButAreNeverCountedAsSpoken()
    {
        var world = new FakeWorld();
        world.Lines["Empty"] = new SpeechResult(SpeechStatus.NoLine, 0, "no INFO");
        world.Lines["Mute"] = new SpeechResult(SpeechStatus.MissingAudio, 0, "no voice file");
        world.Actors.Remove(3);
        var runtime = Runtime(Def(1, new[] { Say("Empty", 1, 0), Say("Mute", 2, 0), Say("Gone", 3, 0) }), world);
        runtime.Start(Scene);

        runtime.Tick(0.1);

        var report = runtime.ReportOf(Scene)!;
        Assert.True(report.Completed);
        Assert.Equal((0, 1, 1, 1), (report.Spoken, report.NoLine, report.MissingAudio, report.ActorAbsent));
        Assert.DoesNotContain(world.Calls, c => c.Contains("Gone"));
    }

    [Fact]
    public void StartWhilePlayingIsANoOpAndAFinishedSceneCanStartAgain()
    {
        var world = new FakeWorld();
        var runtime = Runtime(Def(1, new[] { Say("T0", 1, 0) }), world);

        Assert.Equal(SceneStartOutcome.Started, runtime.Start(Scene).Outcome);
        Assert.Equal(SceneStartOutcome.AlreadyPlaying, runtime.Start(Scene).Outcome);
        Run(runtime, 3);
        Assert.Single(world.Calls, c => c.StartsWith("speak"));
        Assert.Equal(SceneStartOutcome.Started, runtime.Start(Scene).Outcome);
        Run(runtime, 3);
        Assert.Equal(2, world.Calls.Count(c => c.StartsWith("speak")));
    }

    [Fact]
    public void StopCancelsSpeechAndPackagesAndDoesNotSetTheEndStage()
    {
        var world = new FakeWorld();
        var package = new SceneActionDef("Package#P", SceneActionKind.Package, 3, 0, 1, true, "PKG:Test.esm", 0);
        var runtime = Runtime(Def(2, new[] { Say("T0", 1, 0), package, Say("T1", 2, 1) }), world);
        runtime.Start(Scene);
        runtime.Tick(0.1);

        Assert.True(runtime.Stop(Scene));
        runtime.Tick(5);

        Assert.False(runtime.IsPlaying(Scene));
        Assert.Contains("stop-speech A1:Test.esm", world.Calls);
        Assert.Contains("package-end PKG:Test.esm A3:Test.esm", world.Calls);
        Assert.DoesNotContain($"stage {Quest} 10", world.Calls);
        Assert.DoesNotContain("speak T1 A2:Test.esm", world.Calls);
        Assert.True(runtime.ReportOf(Scene)!.Stopped);
        Assert.False(runtime.Stop(Scene));
    }

    [Fact]
    public void UnsupportedShapesRefuseAtStartAndNameTheSceneAndAction()
    {
        var world = new FakeWorld();
        var runtime = Runtime(Def(1, new[] { Say("T0", 1, 0) }, refusal: "action Player Dialogue#4 is a Player Dialogue action, which Garden does not run"), world);

        var result = runtime.Start(Scene);

        Assert.Equal(SceneStartOutcome.Refused, result.Outcome);
        Assert.Contains(Scene, result.Detail);
        Assert.Contains("Player Dialogue#4", result.Detail);
        Assert.False(runtime.IsPlaying(Scene));
        Assert.Equal(SceneStartOutcome.Unknown, runtime.Start("000001:Test.esm").Outcome);
    }

    [Fact]
    public void ConditionsRefuseStartButForceStartSkipsThem()
    {
        var runtime = Runtime(Def(1, new[] { Say("T0", 1, 0) }, condition: "the scene has 2 start condition(s)"), new FakeWorld());

        Assert.Equal(SceneStartOutcome.Refused, runtime.Start(Scene).Outcome);
        Assert.Equal(SceneStartOutcome.Started, runtime.Start(Scene, force: true).Outcome);
    }

    [Fact]
    public void BeginAndPhaseStagesFireInOrder()
    {
        var world = new FakeWorld();
        var def = Def(2, new[] { Say("T0", 1, 0) }, begin: 5) with
        {
            Phases = new[] { new ScenePhaseDef(0, "", 20, 21), new ScenePhaseDef(1, "", null, 22) },
        };
        var runtime = Runtime(def, world);
        runtime.Start(Scene);

        Run(runtime, 3);

        Assert.Equal(new[] { $"stage {Quest} 5", $"stage {Quest} 20", "speak T0 A1:Test.esm", $"stage {Quest} 21", $"stage {Quest} 22", $"stage {Quest} 10" }, world.Calls);
    }

    [Fact]
    public void AliasResolverFillsLazilyAndForceRefToAndClearOverrideIt()
    {
        var live = new Dictionary<string, string> { ["BASE:Test.esm"] = "REF1:Test.esm" };
        var resolver = new QuestAliasResolver(quest => new[] { new AliasFillDef(quest, 1, false, "BASE:Test.esm"), new AliasFillDef(quest, 2, true, "REF2:Test.esm") },
            fill => live.GetValueOrDefault(fill.Target));

        Assert.Equal("REF1:Test.esm", resolver.Resolve(Quest, 1));
        Assert.Null(resolver.Resolve(Quest, 2));
        Assert.Null(resolver.Resolve(Quest, 9));
        live["REF2:Test.esm"] = "REF2:Test.esm";
        Assert.Equal("REF2:Test.esm", resolver.Resolve(Quest, 2));
        resolver.ForceRefTo(Quest, 1, "OTHER:Test.esm");
        Assert.Equal("OTHER:Test.esm", resolver.Resolve(Quest, 1));
        resolver.Clear(Quest, 1);
        Assert.Null(resolver.Resolve(Quest, 1));
    }
}
