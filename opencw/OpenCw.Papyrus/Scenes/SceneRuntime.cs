using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

public enum SceneState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>One scene action: the fragment it runs when its phase plays.</summary>
public sealed record SceneAction(int Order, string Fragment);

/// <summary>One decoded SCEN phase, as Radicle hands it over.</summary>
public sealed record ScenePhase(int Index, IReadOnlyList<SceneAction> Actions);

public sealed record SceneInput(string Scene, string Script, IReadOnlyList<ScenePhase> Phases);

public sealed record SceneOutcome(string Scene, string Operation, SceneState State, int FragmentsRun, bool Applied, string Reason,
    bool Pending = false);

public sealed record SceneDependency(string Behavior, string Key, string Reason);

public sealed record SceneReport(
    IReadOnlyList<SceneOutcome> Outcomes,
    IReadOnlyList<SceneDependency> Dependencies,
    int Applied,
    int Refused,
    int Pending = 0)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"operations={Outcomes.Count} applied={Applied} refused={Refused} pending={Pending} dependencies={Dependencies.Count}\n");
        foreach (var outcome in Outcomes)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Scene} {outcome.Operation} state={outcome.State.ToString().ToLowerInvariant()} fragments={outcome.FragmentsRun} applied={outcome.Applied} pending={outcome.Pending} {outcome.Reason}\n");
        }
        foreach (var dependency in Dependencies)
        {
            text.Append(CultureInfo.InvariantCulture, $"  dependency {dependency.Behavior} key={dependency.Key} {dependency.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Scene state control and the proven scene fragment hooks.
/// </summary>
/// <remarks>
/// The scene state lives here, so start, stop and pause are authoritative and a second start while a
/// scene is already playing is refused rather than duplicated. A scene's current phase runs its actions
/// in order on the shared host once the fragment dispatch is proven. Advancing to the next phase needs
/// the scene runtime, which does not exist, so it is refused and the missing behaviour is named as a
/// dependency instead of being guessed.
/// </remarks>
public sealed class SceneRuntime
{
    public const string ControlKey = "scene.state.control";
    public const string DispatchKey = "scene.fragment.dispatch";
    public const string AdvancementKey = "scene.phase.advancement";

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<string, SceneState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _phases = new(StringComparer.Ordinal);

    public SceneRuntime(IScriptHost host, EvidenceGate gate)
    {
        _host = host;
        _gate = gate;
    }

    public int Applied { get; private set; }

    public int Refused { get; private set; }

    public int Pending { get; private set; }

    public SceneState StateOf(string scene) =>
        _states.TryGetValue(scene, out var state) ? state : SceneState.Stopped;

    public int PhaseOf(string scene) => _phases.TryGetValue(scene, out var phase) ? phase : 0;

    public SceneOutcome Control(SceneInput scene, string operation)
    {
        if (!_gate.Gate(ControlKey).Allowed)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, operation, StateOf(scene.Scene), 0, false,
                "scene state control is not proven, so nothing changed");
        }

        var current = StateOf(scene.Scene);
        switch (operation)
        {
            case "Start" when current == SceneState.Stopped:
                _states[scene.Scene] = SceneState.Playing;
                break;
            case "Stop" when current != SceneState.Stopped:
                _states[scene.Scene] = SceneState.Stopped;
                _phases[scene.Scene] = 0;
                break;
            case "Pause" when current == SceneState.Playing:
                _states[scene.Scene] = SceneState.Paused;
                break;
            default:
                Refused++;
                return new SceneOutcome(scene.Scene, operation, current, 0, false,
                    $"the scene is {current.ToString().ToLowerInvariant()}, so {operation} does not apply");
        }

        Applied++;
        return new SceneOutcome(scene.Scene, operation, StateOf(scene.Scene), 0, true,
            "the scene state was changed");
    }

    /// <summary>Runs the current phase's action fragments in order.</summary>
    public SceneOutcome RunPhase(SceneInput scene, ScriptInstance? instance)
    {
        var phase = scene.Phases.FirstOrDefault(candidate => candidate.Index == PhaseOf(scene.Scene));
        if (phase is null)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), 0, false,
                "the scene has no phase at this index, so nothing ran");
        }
        if (!_gate.Gate(DispatchKey).Allowed || instance is null)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), 0, false,
                "the scene fragment dispatch is not proven or has no instance, so nothing ran");
        }

        if (StateOf(scene.Scene) != SceneState.Playing)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), 0, false,
                "the scene is not playing, so nothing ran");
        }

        var run = 0;
        foreach (var action in phase.Actions.OrderBy(action => action.Order))
        {
            var result = _host.RunEvent(instance, action.Fragment, Array.Empty<PapyrusValue>());
            if (result.Reason == VmStopReason.Waiting)
            {
                Pending++;
                return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), run, false,
                    $"{action.Fragment} latent wait: {result.Message}; later actions have no automatic continuation", true);
            }
            if (!result.Completed)
            {
                Refused++;
                return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), run, false,
                    result.Message ?? result.Reason.ToString());
            }
            run++;
        }

        Applied++;
        return new SceneOutcome(scene.Scene, "RunPhase", StateOf(scene.Scene), run, true,
            "the current phase's action fragments ran in order");
    }

    /// <summary>Advances to the next phase, which needs the missing scene runtime.</summary>
    public SceneOutcome Advance(SceneInput scene)
    {
        if (!_gate.Gate(AdvancementKey).Allowed)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, "Advance", StateOf(scene.Scene), 0, false,
                "the scene phase advancement is not proven, so the phase did not change");
        }

        if (StateOf(scene.Scene) != SceneState.Playing)
        {
            Refused++;
            return new SceneOutcome(scene.Scene, "Advance", StateOf(scene.Scene), 0, false,
                "the scene is not playing, so the phase did not change");
        }

        _phases[scene.Scene] = PhaseOf(scene.Scene) + 1;
        Applied++;
        return new SceneOutcome(scene.Scene, "Advance", StateOf(scene.Scene), 0, true,
            "the scene advanced to the next phase");
    }

    /// <summary>The scene behaviour that does not exist yet, named.</summary>
    public IReadOnlyList<SceneDependency> Dependencies()
    {
        var dependencies = new List<SceneDependency>();
        if (!_gate.Gate(AdvancementKey).Allowed)
        {
            dependencies.Add(new SceneDependency("scene.phase.advancement", AdvancementKey,
                "when a scene phase ends and the next begins is not proven"));
        }
        return dependencies;
    }

    public SceneReport Report(IEnumerable<SceneOutcome> outcomes) =>
        new(outcomes.ToList(), Dependencies(), Applied, Refused, Pending);
}
