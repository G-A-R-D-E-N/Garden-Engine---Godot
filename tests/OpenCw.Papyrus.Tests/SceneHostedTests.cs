using OpenCommonwealth.Services.Papyrus.Scenes;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SceneHostedTests
{
    private const string Quest = "027411:Test.esm";
    private const string Scene = "03467E:Test.esm";

    private sealed class World : ISceneWorld
    {
        public List<string> Calls { get; } = new();

        public string? ResolveActor(string quest, int alias) => $"A{alias}:Test.esm";

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            Calls.Add($"speak {topic}");
            return new SpeechResult(SpeechStatus.Spoken, 1.0, "");
        }

        public void StopSpeech(string context, string actor) => Calls.Add("stop-speech");

        public PackageResult BeginPackage(string context, string package, string? actor)
        {
            Calls.Add($"package {package}");
            return new PackageResult(true, "");
        }

        public void EndPackage(string package, string? actor) => Calls.Add($"package-end {package}");

        public bool SetStage(string quest, int stage)
        {
            Calls.Add($"stage {stage}");
            return true;
        }
    }

    private static SceneActionDef Menu(int phase) => new($"PlayerDialogue#{phase}", SceneActionKind.PlayerDialogue, 1, phase, phase, false, null, 0);

    private static SceneActionDef Package(string name, int start, int end) => new($"Package#{name}", SceneActionKind.Package, 2, start, end, false, name, 0);

    private static SceneDef Def(string? host = "action PlayerDialogue#0 is a player dialogue action, which only the dialogue menu runs", string? condition = null, bool phaseOnly = false, params SceneActionDef[] extra) =>
        new(Scene, Quest, [new(0, "Begin", 20, 21), new(1, "Middle", 30, 31), new(2, "Last", 40, 41)], [Menu(0), Menu(1), Menu(2), .. extra], 5, 10, null, condition, host, phaseOnly);

    private static SceneRuntime Runtime(SceneDef def, World world) => new(key => key == Scene ? def : null, world);

    [Fact]
    public void AScriptStartOfASceneThatNeedsTheDialogueMenuIsRefusedByName()
    {
        var runtime = Runtime(Def(), new World());

        var started = runtime.Start(Scene);
        var forced = runtime.Start(Scene, force: true);
        var resumed = runtime.Resume(new SceneRunState(Scene, 1));

        Assert.All(new[] { started, forced, resumed }, r => Assert.Equal(SceneStartOutcome.Refused, r.Outcome));
        Assert.Contains("player dialogue action", started.Detail);
        Assert.False(runtime.IsPlaying(Scene));
    }

    [Fact]
    public void AHostedRunSetsNothingUntilTickAndTheMenuMovesAreAppliedInOrder()
    {
        var world = new World();
        var runtime = Runtime(Def(), world);

        Assert.Equal(SceneStartOutcome.Started, runtime.StartHosted(Scene).Outcome);
        Assert.True(runtime.HostPhase(Scene, 0));
        Assert.True(runtime.HostPhase(Scene, 2));
        Assert.True(runtime.HostEnd(Scene));
        Assert.Empty(world.Calls);
        Assert.True(runtime.IsPlaying(Scene));

        runtime.Tick(0.25);

        Assert.Equal(new[] { "stage 5", "stage 20", "stage 21", "stage 40", "stage 41", "stage 10" }, world.Calls);
        Assert.False(runtime.IsPlaying(Scene));
        Assert.True(runtime.ReportOf(Scene)!.Completed);
        Assert.True(runtime.ReportOf(Scene)!.EndStageSet);
        Assert.DoesNotContain(world.Calls, c => c.StartsWith("speak", StringComparison.Ordinal));
    }

    [Fact]
    public void AHostedRunStaysInThePhaseTheMenuChoseAndIgnoresRepeatedAndUnknownPhases()
    {
        var world = new World();
        var runtime = Runtime(Def(), world);
        runtime.StartHosted(Scene);

        runtime.HostPhase(Scene, 1);
        runtime.Tick(0.25);
        runtime.HostPhase(Scene, 1);
        runtime.HostPhase(Scene, 7);
        runtime.Tick(30);

        Assert.Equal(new[] { "stage 5", "stage 30" }, world.Calls);
        Assert.True(runtime.IsPlaying(Scene));
        Assert.Contains(runtime.Trace, e => e.Kind == "host-phase-ignored");
        Assert.Contains(runtime.Trace, e => e.Kind == "host-action" && e.Detail.StartsWith("PlayerDialogue#1", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellingAHostedRunReleasesItsPackagesAndDoesNotSetTheEndStage()
    {
        var world = new World();
        var runtime = Runtime(Def(extra: Package("PKG:Test.esm", 0, 2)), world);
        runtime.StartHosted(Scene);
        runtime.HostPhase(Scene, 1);
        runtime.Tick(0.25);

        Assert.True(runtime.StopHosted(Scene));

        Assert.Equal(new[] { "stage 5", "stage 30", "package PKG:Test.esm", "package-end PKG:Test.esm" }, world.Calls);
        Assert.DoesNotContain("stage 10", world.Calls);
        Assert.False(runtime.IsPlaying(Scene));
        Assert.True(runtime.ReportOf(Scene)!.Stopped);
        Assert.False(runtime.ReportOf(Scene)!.EndStageSet);
        Assert.False(runtime.HostPhase(Scene, 0));
        Assert.False(runtime.StopHosted(Scene));
    }

    [Fact]
    public void ASpanningPackageStaysThroughTheSpanAndEndsWhenThePhaseLeavesIt()
    {
        var world = new World();
        var runtime = Runtime(Def(extra: Package("PKG:Test.esm", 0, 1)), world);
        runtime.StartHosted(Scene);
        runtime.HostPhase(Scene, 0);
        runtime.HostPhase(Scene, 1);
        runtime.Tick(0.25);
        Assert.Equal(1, world.Calls.Count(c => c == "package PKG:Test.esm"));
        Assert.DoesNotContain("package-end PKG:Test.esm", world.Calls);

        runtime.HostPhase(Scene, 2);
        runtime.Tick(0.25);

        Assert.Single(world.Calls, "package-end PKG:Test.esm");
    }

    [Fact]
    public void AScriptRunCannotBeDrivenOrStoppedByTheMenu()
    {
        var world = new World();
        var runtime = Runtime(Def(host: null), world);
        runtime.Start(Scene);

        Assert.False(runtime.HostPhase(Scene, 0));
        Assert.False(runtime.HostEnd(Scene));
        Assert.False(runtime.StopHosted(Scene));
        Assert.True(runtime.IsPlaying(Scene));
    }

    [Fact]
    public void AHostedRunIsNotSavedAndAFinishedOneCanBeHostedAgain()
    {
        var runtime = Runtime(Def(), new World());
        runtime.StartHosted(Scene);
        runtime.Tick(0.25);

        Assert.Empty(runtime.Capture());
        Assert.Equal(SceneStartOutcome.AlreadyPlaying, runtime.StartHosted(Scene).Outcome);
        runtime.HostEnd(Scene);
        runtime.Tick(0.25);
        Assert.Equal(SceneStartOutcome.Started, runtime.StartHosted(Scene).Outcome);
    }

    [Fact]
    public void OnlyPhaseStartConditionsAreAcceptedByAHostedRun()
    {
        var phaseOnly = Runtime(Def(condition: "a phase has conditions", phaseOnly: true), new World());
        var scene = Runtime(Def(condition: "the scene has 1 start condition(s)"), new World());
        var broken = Runtime(Def() with { Refusal = "package action P must complete the phase" }, new World());

        Assert.Equal(SceneStartOutcome.Started, phaseOnly.StartHosted(Scene).Outcome);
        Assert.Equal(SceneStartOutcome.Refused, scene.StartHosted(Scene).Outcome);
        Assert.Equal(SceneStartOutcome.Refused, broken.StartHosted(Scene).Outcome);
        Assert.Equal(SceneStartOutcome.Unknown, phaseOnly.StartHosted("000001:Test.esm").Outcome);
    }

    [Fact]
    public void AHostedStartOfASceneWithNoHostOnlyPartRunsLikeAScriptStartAndSpeaksItsLines()
    {
        var world = new World();
        var say = new SceneActionDef("Dialogue#0", SceneActionKind.Dialogue, 1, 0, 0, false, "TOPIC:Test.esm", 0);
        var def = new SceneDef(Scene, Quest, [new(0, "Only", null, null)], [say], null, 10);
        var runtime = Runtime(def, world);

        Assert.Equal(SceneStartOutcome.Started, runtime.StartHosted(Scene).Outcome);
        Assert.False(runtime.HostPhase(Scene, 0));
        runtime.Tick(0.25);
        runtime.Tick(2);

        Assert.Equal(new[] { "speak TOPIC:Test.esm", "stage 10" }, world.Calls);
        Assert.True(runtime.ReportOf(Scene)!.Completed);
    }

    [Fact]
    public void OnlyAnUnconditionalSceneWithNoHostOnlyPartRunsItselfAndAPhaseConditionOnlySceneIsHosted()
    {
        var say = new SceneActionDef("Dialogue#0", SceneActionKind.Dialogue, 1, 0, 0, false, "TOPIC:Test.esm", 0);
        SceneDef Npc(string? condition, bool phaseOnly) => new(Scene, Quest, [new(0, "Only", null, null)], [say], null, 10, null, condition, null, phaseOnly);
        var plain = Runtime(Npc(null, false), new World());
        var world = new World();
        var phaseConditions = Runtime(Npc("a phase has conditions", true), world);
        var hostOnly = Runtime(Def(), new World());

        Assert.True(plain.RunsItself(Scene));
        Assert.False(hostOnly.RunsItself(Scene));
        Assert.False(phaseConditions.RunsItself(Scene));
        Assert.False(plain.RunsItself("000001:Test.esm"));
        Assert.Equal(SceneStartOutcome.Started, phaseConditions.StartHosted(Scene).Outcome);
        Assert.True(phaseConditions.HostPhase(Scene, 0));
        phaseConditions.Tick(0.25);
        Assert.DoesNotContain(world.Calls, c => c.StartsWith("speak", StringComparison.Ordinal));
    }
}
