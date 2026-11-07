using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class SceneRuntimeTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (SceneRuntime.ControlKey, "PROVEN"),
        (SceneRuntime.DispatchKey, "PROVEN"),
        (SceneRuntime.AdvancementKey, "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static (SceneRuntime Runtime, ScriptInstance Instance, List<string> Seen) Setup(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("SceneScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("Action_A", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("Action_B", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in new[] { "Action_A", "Action_B" })
        {
            host.Overrides.Register("SceneScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }
        var instance = host.Attach("SceneScript");
        host.Initialize(instance);
        return (new SceneRuntime(host, gate), instance, seen);
    }

    private static SceneInput Scene(params SceneAction[] actions) =>
        new("Scene", "SceneScript", new[]
        {
            new ScenePhase(0, actions),
            new ScenePhase(1, Array.Empty<SceneAction>()),
        });

    [Fact]
    public void StartAndStopChangeTheAuthoritativeState()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));

        Assert.Equal(SceneState.Stopped, runtime.StateOf("Scene"));
        Assert.True(runtime.Control(scene, "Start").Applied);
        Assert.Equal(SceneState.Playing, runtime.StateOf("Scene"));
        Assert.True(runtime.Control(scene, "Stop").Applied);
        Assert.Equal(SceneState.Stopped, runtime.StateOf("Scene"));
    }

    [Fact]
    public void ASecondStartWhilePlayingIsRefused()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));
        runtime.Control(scene, "Start");

        var outcome = runtime.Control(scene, "Start");

        Assert.False(outcome.Applied);
        Assert.Contains("does not apply", outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void PauseIsRecorded()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));
        runtime.Control(scene, "Start");

        Assert.True(runtime.Control(scene, "Pause").Applied);
        Assert.Equal(SceneState.Paused, runtime.StateOf("Scene"));
    }

    [Fact]
    public void TheCurrentPhaseRunsItsActionsInOrder()
    {
        var gate = Gate();
        var (runtime, instance, seen) = Setup(gate);
        var scene = Scene(new SceneAction(1, "Action_B"), new SceneAction(0, "Action_A"));
        runtime.Control(scene, "Start");

        var outcome = runtime.RunPhase(scene, instance);

        Assert.True(outcome.Applied);
        Assert.Equal(2, outcome.FragmentsRun);
        Assert.Equal(new[] { "Action_A", "Action_B" }, seen);
    }

    [Fact]
    public void PhaseAdvancementIsRefusedAndNamed()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));

        var outcome = runtime.Advance(scene);

        Assert.False(outcome.Applied);
        Assert.Equal(0, runtime.PhaseOf("Scene"));
        Assert.Contains("not proven", outcome.Reason, StringComparison.Ordinal);
        var dependency = Assert.Single(runtime.Dependencies());
        Assert.Equal(SceneRuntime.AdvancementKey, dependency.Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoppedOrPausedSceneRunsNoFragments(bool paused)
    {
        var (runtime, instance, seen) = Setup(Gate());
        var scene = Scene(new SceneAction(0, "Action_A"));
        if (paused)
        {
            runtime.Control(scene, "Start");
            runtime.Control(scene, "Pause");
        }

        var outcome = runtime.RunPhase(scene, instance);

        Assert.False(outcome.Applied);
        Assert.Equal(0, outcome.FragmentsRun);
        Assert.Empty(seen);
        Assert.Equal(1, runtime.Refused);
        Assert.Contains("not playing", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoppedSceneCannotAdvanceEvenWithTheKeyAllowed()
    {
        var (runtime, _, _) = Setup(Gate((SceneRuntime.AdvancementKey, "ENGINE_POLICY")));

        var outcome = runtime.Advance(Scene());

        Assert.False(outcome.Applied);
        Assert.Equal(0, runtime.PhaseOf("Scene"));
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void StopResetsThePhaseBeforeRestart()
    {
        var (runtime, _, _) = Setup(Gate((SceneRuntime.AdvancementKey, "ENGINE_POLICY")));
        var scene = Scene();
        Assert.True(runtime.Control(scene, "Start").Applied);
        Assert.True(runtime.Advance(scene).Applied);
        Assert.Equal(1, runtime.PhaseOf("Scene"));
        Assert.True(runtime.Control(scene, "Stop").Applied);
        Assert.True(runtime.Control(scene, "Start").Applied);

        Assert.Equal(0, runtime.PhaseOf("Scene"));
    }

    [Fact]
    public void AnUnprovenDispatchRunsNothing()
    {
        var gate = Gate((SceneRuntime.DispatchKey, "UNPROVEN"));
        var (runtime, instance, seen) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));

        var outcome = runtime.RunPhase(scene, instance);

        Assert.False(outcome.Applied);
        Assert.Empty(seen);
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void TheReportCarriesStateAndDependencies()
    {
        var gate = Gate();
        var (runtime, instance, _) = Setup(gate);
        var scene = Scene(new SceneAction(0, "Action_A"));
        var outcomes = new List<SceneOutcome>
        {
            runtime.Control(scene, "Start"),
            runtime.RunPhase(scene, instance),
            runtime.Advance(scene),
        };

        var first = runtime.Report(outcomes);
        var second = runtime.Report(outcomes);

        Assert.Equal(first.Outcomes.Count, first.Applied + first.Refused);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(2, first.Applied);
        Assert.Equal(1, first.Refused);
        Assert.Single(first.Dependencies);
        Assert.Equal(64, first.Digest().Length);
    }
}
