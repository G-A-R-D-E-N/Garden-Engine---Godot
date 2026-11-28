using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

public enum SceneStartOutcome
{
    Started,
    AlreadyPlaying,
    Unknown,
    Refused,
}

public sealed record SceneStartResult(SceneStartOutcome Outcome, string Detail);

public sealed record SceneEvent(double At, string Scene, int Phase, string Kind, string Detail)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"t={At:F2}s {Scene} phase={Phase} {Kind} {Detail}");
}

public sealed record SceneRunState(string Scene, int Phase);

public sealed class SceneRunReport
{
    public bool Completed { get; internal set; }

    public bool Stopped { get; internal set; }

    public bool Failed { get; internal set; }

    public bool EndStageSet { get; internal set; }

    public int Spoken { get; internal set; }

    public int NoLine { get; internal set; }

    public int MissingAudio { get; internal set; }

    public int ActorAbsent { get; internal set; }

    public int PackagesApplied { get; internal set; }

    public int PackagesNotApplied { get; internal set; }

    public override string ToString() =>
        $"completed={Completed} stopped={Stopped} failed={Failed} endStageSet={EndStageSet} spoken={Spoken} noLine={NoLine} missingAudio={MissingAudio} actorAbsent={ActorAbsent} packagesApplied={PackagesApplied} packagesNotApplied={PackagesNotApplied}";
}

public sealed class SceneRuntime(Func<string, SceneDef?> define, ISceneWorld world, Action<string>? log = null)
{
    private sealed class ActionState(SceneActionDef def, string? actor)
    {
        public SceneActionDef Def { get; } = def;

        public string? Actor { get; } = actor;

        public double EndsAt { get; set; }

        public bool PackageActive { get; set; }
    }

    private sealed class Run(SceneDef def, SceneRunReport report)
    {
        public SceneDef Def { get; } = def;

        public SceneRunReport Report { get; } = report;

        public int Phase { get; set; } = -1;

        public bool Resuming { get; set; }

        public bool Hosted { get; init; }

        public bool Began { get; set; }

        public Queue<int> Moves { get; } = new();

        public string? Action { get; set; }

        public List<ActionState> Actions { get; } = new();
    }

    private const int EndMove = -1;
    public const int TraceCapacity = 1024;

    private readonly Dictionary<string, Run> _runs = new(FormKeyComparer.Instance);
    private readonly Dictionary<string, SceneRunReport> _reports = new(FormKeyComparer.Instance);
    private readonly List<SceneEvent> _trace = new();
    private double _now;

    public IReadOnlyList<SceneEvent> Trace => _trace;

    public IEnumerable<SceneEvent> TraceSince(double at) => _trace.Where(e => e.At >= at);

    public long TraceDropped { get; private set; }

    public double Now => _now;

    public bool IsPlaying(string scene) => _runs.ContainsKey(scene);

    public SceneRunReport? ReportOf(string scene) => _reports.GetValueOrDefault(scene);

    public string? QuestOf(string scene) => define(scene)?.Quest;

    // Requires: only queues; Tick does all the work so a native callback never runs scene actions.
    public SceneStartResult Start(string scene, bool force = false)
    {
        if (_runs.ContainsKey(scene)) return new(SceneStartOutcome.AlreadyPlaying, $"scene {scene} is already playing");
        if (define(scene) is not { } def) return new(SceneStartOutcome.Unknown, $"scene {scene} is not a known scene record");
        if (def.Refusal is { } shape) return new(SceneStartOutcome.Refused, $"scene {scene} refused: {shape}");
        if (def.HostRefusal is { } host) return new(SceneStartOutcome.Refused, $"scene {scene} refused: {host}; only a dialogue that presents the scene can start it");
        if (!force && def.ConditionRefusal is { } conditions) return new(SceneStartOutcome.Refused, $"scene {scene} refused: {conditions}");
        _reports[scene] = new SceneRunReport();
        _runs[scene] = new Run(def, _reports[scene]);
        Emit(scene, -1, "start", force ? "queued (ForceStart: conditions ignored)" : "queued");
        return new(SceneStartOutcome.Started, $"scene {scene} queued");
    }

    // Requires: only queues, like Start; the dialogue menu owns speech and phase choice and drives the run with HostPhase and HostEnd.
    public SceneStartResult StartHosted(string scene)
    {
        if (RunsItself(scene)) return Start(scene);
        if (_runs.ContainsKey(scene)) return new(SceneStartOutcome.AlreadyPlaying, $"scene {scene} is already playing");
        if (define(scene) is not { } def) return new(SceneStartOutcome.Unknown, $"scene {scene} is not a known scene record");
        if (def.Refusal is { } shape) return new(SceneStartOutcome.Refused, $"scene {scene} refused: {shape}");
        if (def.ConditionRefusal is { } conditions && !def.PhaseConditionsOnly) return new(SceneStartOutcome.Refused, $"scene {scene} refused: {conditions}");
        _reports[scene] = new SceneRunReport();
        _runs[scene] = new Run(def, _reports[scene]) { Hosted = true };
        Emit(scene, -1, "start", "queued (hosted by the dialogue menu)");
        return new(SceneStartOutcome.Started, $"scene {scene} queued, hosted by the dialogue menu");
    }

    // Invariant: true when Start plays the whole scene without a dialogue menu, so a conversation must not present its lines again.
    public bool RunsItself(string scene) => define(scene) is { Refusal: null, HostRefusal: null, ConditionRefusal: null };

    public bool HostPhase(string scene, int phase) => Host(scene, phase);

    public bool HostEnd(string scene) => Host(scene, EndMove);

    public bool StopHosted(string scene) => _runs.TryGetValue(scene, out var run) && run.Hosted && Stop(scene);

    private bool Host(string scene, int move)
    {
        if (!_runs.TryGetValue(scene, out var run) || !run.Hosted) return false;
        run.Moves.Enqueue(move);
        return true;
    }

    // Invariant: a run that has not begun is saved with phase -1; a begun run with the phase it is in.
    public IReadOnlyList<SceneRunState> Capture() => _runs.Where(pair => !pair.Value.Hosted).Select(pair => new SceneRunState(pair.Key, pair.Value.Phase)).ToArray();

    // Requires: only queues, like Start; the saved phase re-enters without setting its stages again.
    public SceneStartResult Resume(SceneRunState state)
    {
        if (state.Phase < 0) return Start(state.Scene, force: true);
        if (_runs.ContainsKey(state.Scene)) return new(SceneStartOutcome.AlreadyPlaying, $"scene {state.Scene} is already playing");
        if (define(state.Scene) is not { } def) return new(SceneStartOutcome.Unknown, $"scene {state.Scene} is not a known scene record");
        if (def.Refusal is { } shape) return new(SceneStartOutcome.Refused, $"scene {state.Scene} refused: {shape}");
        if (def.HostRefusal is { } host) return new(SceneStartOutcome.Refused, $"scene {state.Scene} refused: {host}; only a dialogue that presents the scene can start it");
        if (state.Phase >= def.Phases.Count) return new(SceneStartOutcome.Refused, $"scene {state.Scene} has no phase {state.Phase} in this plugin set");
        _reports[state.Scene] = new SceneRunReport();
        _runs[state.Scene] = new Run(def, _reports[state.Scene]) { Phase = state.Phase, Resuming = true };
        Emit(state.Scene, state.Phase, "resume", "queued: the phase's actions start again, stages are not set again");
        return new(SceneStartOutcome.Started, $"scene {state.Scene} resume queued at phase {state.Phase}");
    }

    // Invariant: true when every actor the actions of the saved phase name resolves now.
    public bool Ready(SceneRunState state) =>
        state.Phase < 0 || define(state.Scene) is not { } def || Resumed(def, state.Phase).All(action => action.Alias < 0 || world.ResolveActor(def.Quest, action.Alias) is not null);

    // Invariant: the actors of the scene's actions that resolve now, whichever phase they belong to.
    public IReadOnlyList<string> ActorsOf(string scene) =>
        define(scene) is { } def ? def.Actions.Where(action => action.Alias >= 0).Select(action => world.ResolveActor(def.Quest, action.Alias)).OfType<string>().Distinct().ToArray() : Array.Empty<string>();

    private static IEnumerable<SceneActionDef> Resumed(SceneDef def, int phase) =>
        def.Actions.Where(action => action.StartPhase == phase || action.Kind == SceneActionKind.Package && action.StartPhase < phase && action.EndPhase >= phase);

    public bool Stop(string scene)
    {
        if (!_runs.TryGetValue(scene, out var run)) return false;
        ReleaseAll(scene, run, speech: true);
        run.Report.Stopped = true;
        _runs.Remove(scene);
        Emit(scene, run.Phase, "stop", "cancelled; end stage not set");
        return true;
    }

    public void StopAll()
    {
        foreach (var scene in _runs.Keys.ToArray()) Stop(scene);
    }

    public void ForgetReports() => _reports.Clear();

    public void Tick(double seconds)
    {
        _now += seconds;
        foreach (var scene in _runs.Keys.ToArray())
            if (_runs.TryGetValue(scene, out var run)) Guarded(scene, run);
    }

    // Invariant: one scene's exception never reaches the caller or another scene; the run is released and removed so it is not re-entered next frame.
    private void Guarded(string scene, Run run)
    {
        run.Action = null;
        try { Advance(scene, run); }
        catch (Exception error)
        {
            run.Report.Failed = true;
            Emit(scene, run.Phase, "failed", $"action={run.Action ?? "-"} {error.GetType().Name}: {error.Message}");
            ReleaseAll(scene, run, speech: true);
            _runs.Remove(scene);
        }
    }

    private void ReleaseAll(string scene, Run run, bool speech)
    {
        foreach (var state in run.Actions)
        {
            try { Release(scene, run, state, speech); }
            catch (Exception error) { Emit(scene, run.Phase, "release-failed", $"action={state.Def.Name} {error.GetType().Name}: {error.Message}"); }
        }
    }

    private void Advance(string scene, Run run)
    {
        if (run.Hosted)
        {
            AdvanceHosted(scene, run);
            return;
        }
        if (run.Resuming)
        {
            run.Resuming = false;
            Emit(scene, run.Phase, "resumed", $"{run.Def.Phases.Count} phase(s)");
            Enter(scene, run, resumed: true);
        }
        else if (run.Phase < 0)
        {
            if (run.Def.BeginStage is { } begin) SetStage(scene, run, begin, "begin stage");
            run.Phase = 0;
            Emit(scene, 0, "begin", $"{run.Def.Phases.Count} phase(s), {run.Def.Actions.Count} action(s)");
            if (run.Def.Phases.Count > 0) Enter(scene, run);
        }
        while (run.Phase < run.Def.Phases.Count && run.Actions.All(state => state.Def.IgnoreForCompletion || _now >= state.EndsAt))
        {
            var phase = run.Def.Phases[run.Phase];
            Emit(scene, run.Phase, "phase-complete", "");
            foreach (var state in run.Actions.Where(s => s.Def.EndPhase <= run.Phase).ToArray())
            {
                Release(scene, run, state, speech: false);
                run.Actions.Remove(state);
            }
            foreach (var state in run.Actions.Where(s => !s.Def.IgnoreForCompletion && _now >= s.EndsAt).ToArray())
            {
                Release(scene, run, state, speech: false);
                run.Actions.Remove(state);
            }
            if (phase.CompletionStage is { } completion) SetStage(scene, run, completion, "phase completion stage");
            run.Phase++;
            if (run.Phase < run.Def.Phases.Count) Enter(scene, run);
        }
        if (run.Phase >= run.Def.Phases.Count) Finish(scene, run);
    }

    private void AdvanceHosted(string scene, Run run)
    {
        if (!run.Began)
        {
            run.Began = true;
            if (run.Def.BeginStage is { } begin) SetStage(scene, run, begin, "begin stage");
            Emit(scene, -1, "begin", $"hosted, {run.Def.Phases.Count} phase(s), {run.Def.Actions.Count} action(s)");
        }
        while (run.Moves.TryDequeue(out var move))
        {
            if (move == EndMove)
            {
                LeavePhase(scene, run);
                Finish(scene, run);
                return;
            }
            if (move != run.Phase) EnterHosted(scene, run, move);
        }
    }

    private void LeavePhase(string scene, Run run)
    {
        if (run.Phase >= 0 && run.Def.Phases[run.Phase].CompletionStage is { } completion) SetStage(scene, run, completion, "phase completion stage");
    }

    private void EnterHosted(string scene, Run run, int phase)
    {
        if (phase < 0 || phase >= run.Def.Phases.Count)
        {
            Emit(scene, run.Phase, "host-phase-ignored", $"phase {phase} is not in the scene");
            return;
        }
        LeavePhase(scene, run);
        foreach (var state in run.Actions.Where(s => s.Def.StartPhase > phase || s.Def.EndPhase < phase).ToArray())
        {
            Release(scene, run, state, speech: false);
            run.Actions.Remove(state);
        }
        run.Phase = phase;
        var def = run.Def.Phases[phase];
        Emit(scene, phase, "phase-start", def.Name);
        if (def.StartStage is { } start) SetStage(scene, run, start, "phase start stage");
        foreach (var action in Resumed(run.Def, phase))
        {
            if (action.Kind != SceneActionKind.Package) Emit(scene, phase, "host-action", $"{action.Name} is presented by the dialogue menu");
            else if (run.Actions.All(s => s.Def != action))
            {
                run.Actions.Add(Begin(scene, run, action));
                run.Action = null;
            }
        }
    }

    private void Enter(string scene, Run run, bool resumed = false)
    {
        var phase = run.Def.Phases[run.Phase];
        Emit(scene, run.Phase, "phase-start", phase.Name);
        if (!resumed && phase.StartStage is { } start) SetStage(scene, run, start, "phase start stage");
        foreach (var def in resumed ? Resumed(run.Def, run.Phase) : run.Def.Actions.Where(action => action.StartPhase == run.Phase))
        {
            run.Actions.Add(Begin(scene, run, def));
            run.Action = null;
        }
    }

    private ActionState Begin(string scene, Run run, SceneActionDef def)
    {
        run.Action = def.Name;
        var actor = def.Alias >= 0 ? world.ResolveActor(run.Def.Quest, def.Alias) : null;
        var state = new ActionState(def, actor) { EndsAt = _now };
        var context = Context(run, def);
        switch (def.Kind)
        {
            case SceneActionKind.Dialogue:
                var speech = actor is null
                    ? new SpeechResult(SpeechStatus.ActorAbsent, 0, $"alias {def.Alias} has no live actor")
                    : world.Speak(context, def.Target ?? "", actor);
                switch (speech.Status)
                {
                    case SpeechStatus.Spoken:
                        run.Report.Spoken++;
                        state.EndsAt = _now + speech.Seconds;
                        break;
                    case SpeechStatus.NoLine: run.Report.NoLine++; break;
                    case SpeechStatus.MissingAudio:
                        run.Report.MissingAudio++;
                        state.EndsAt = _now + speech.Seconds;
                        break;
                    default: run.Report.ActorAbsent++; break;
                }
                Emit(scene, run.Phase, "dialogue", $"{def.Name} alias={def.Alias} actor={actor ?? "-"} topic={def.Target} {speech.Status} seconds={speech.Seconds:F2} {speech.Detail}");
                break;
            case SceneActionKind.Timer:
                state.EndsAt = _now + def.Seconds;
                Emit(scene, run.Phase, "timer", $"{def.Name} seconds={def.Seconds:F2}");
                break;
            case SceneActionKind.PlayerDialogue:
                Emit(scene, run.Phase, "player-dialogue", $"{def.Name} is run by the dialogue menu, not by the scene runtime");
                break;
            default:
                var package = actor is null
                    ? new PackageResult(false, $"alias {def.Alias} has no live actor")
                    : world.BeginPackage(context, def.Target ?? "", actor);
                state.PackageActive = package.Applied;
                if (package.Applied) run.Report.PackagesApplied++;
                else run.Report.PackagesNotApplied++;
                Emit(scene, run.Phase, "package", $"{def.Name} alias={def.Alias} actor={actor ?? "-"} package={def.Target} applied={package.Applied} {package.Detail}");
                break;
        }
        return state;
    }

    private static string Context(Run run, SceneActionDef def) => $"scene {run.Def.Key} action {def.Name}";

    private void Release(string scene, Run run, ActionState state, bool speech)
    {
        if (state.PackageActive)
        {
            world.EndPackage(state.Def.Target ?? "", state.Actor);
            state.PackageActive = false;
            Emit(scene, run.Phase, "package-end", $"{state.Def.Name} package={state.Def.Target} actor={state.Actor}");
        }
        if (speech && state.Actor is { } actor && state.Def.Kind == SceneActionKind.Dialogue && _now < state.EndsAt) world.StopSpeech(Context(run, state.Def), actor);
    }

    private void Finish(string scene, Run run)
    {
        foreach (var state in run.Actions) Release(scene, run, state, speech: false);
        if (run.Def.EndStage is { } end) run.Report.EndStageSet = SetStage(scene, run, end, "end stage");
        run.Report.Completed = true;
        _runs.Remove(scene);
        Emit(scene, run.Def.Phases.Count, "end", run.Report.ToString());
    }

    private bool SetStage(string scene, Run run, int stage, string what)
    {
        var applied = world.SetStage(run.Def.Quest, stage);
        Emit(scene, run.Phase, "stage", $"{what} quest={run.Def.Quest} stage={stage} applied={applied}");
        return applied;
    }

    private void Emit(string scene, int phase, string kind, string detail)
    {
        var line = new SceneEvent(_now, scene, phase, kind, detail);
        if (_trace.Count == TraceCapacity) { _trace.RemoveAt(0); TraceDropped++; }
        _trace.Add(line);
        log?.Invoke(line.ToString());
    }
}
