using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Scenes;

namespace OpenCommonwealth;

public partial class PapyrusRuntime
{
    private readonly Dictionary<string, List<SceneRunState>> _parkedScenes = new();

    public SceneResumeQueue SceneResumes { get; } = new();

    // Invariant: a save holds the running runs, the runs still waiting to resume and the runs parked for a left cell, so none is lost by saving while one is in transit.
    public IReadOnlyDictionary<string, byte[]> SceneStateSections() =>
        SceneStateSave.Sections((Scenes?.Capture() ?? []).Concat(SceneResumes.States).Concat(_parkedScenes.Values.SelectMany(states => states)), SceneHost?.Aliases);

    public IReadOnlyDictionary<string, byte[]> ScriptEventSections() => ScriptEventsSave.Sections(Timers, Registrations, CustomEvents);

    public IReadOnlyDictionary<string, byte[]> GameTimerSections() => GameTimersSave.Sections(GameTimers);

    public void RestoreGameTimers(IReadOnlyList<SavedTimer> saved)
    {
        var dropped = GameTimersSave.Restore(saved, Session!.Host.Instances, GameTimers);
        GD.Print($"[Save] game timers: {saved.Count} saved, {dropped} dropped (script not live)");
    }

    // Requires: main thread, after the script instances were restored; the tables are replaced by the saved ones.
    public void RestoreScriptEvents(ScriptEventsSnapshot snapshot)
    {
        var dropped = ScriptEventsSave.Restore(snapshot, Session!.Host.Instances, Timers, Registrations, CustomEvents);
        GD.Print($"[Save] script events: {snapshot.Timers.Count} timer(s), {snapshot.Registrations.Count} registration(s), {snapshot.Receivers.Count} custom-event receiver(s) saved, {dropped} dropped (script not live)");
    }

    public void RestoreSceneState(SceneStateSnapshot snapshot) => SceneStateSave.Restore(snapshot, Scenes, SceneHost?.Aliases, SceneResumes);

    internal RestoredStateSnapshot CaptureRestoredState()
    {
        var runs = (Scenes?.Capture() ?? []).Concat(SceneResumes.States).Concat(_parkedScenes.Values.SelectMany(states => states)).DistinctBy(run => run.Scene).ToArray();
        var scenes = new SceneStateSnapshot(runs, SceneHost?.Aliases.Capture() ?? Array.Empty<ForcedAlias>(), SceneHost?.Aliases.CaptureCollections());
        return new RestoredStateSnapshot(ScriptEventsSave.Capture(Timers, Registrations, CustomEvents), GameTimersSave.Capture(GameTimers), scenes);
    }

    internal void RestoreRestoredState(RestoredStateSnapshot snapshot)
    {
        ClearRestoredState();
        if (Session is not null)
        {
            ScriptEventsSave.Restore(snapshot.ScriptEvents, Session.Host.Instances, Timers, Registrations, CustomEvents);
            GameTimersSave.Restore(snapshot.GameTimers, Session.Host.Instances, GameTimers);
        }
        RestoreSceneState(snapshot.Scenes);
    }

    // Requires: called before the cell's nodes are freed, so the scene's actors still resolve; a scene with no actor in the cell keeps running.
    private void SuspendScenesIn(Node root, string cell)
    {
        if (Scenes is null) return;
        foreach (var state in Scenes.Capture())
        {
            var inCell = Scenes.ActorsOf(state.Scene).Any(key => ActorRegistry.Find(key) is { } identity && IsInstanceValid(identity.Node) && root.IsAncestorOf(identity.Node));
            if (!inCell || !Scenes.Stop(state.Scene)) continue;
            if (!_parkedScenes.TryGetValue(cell, out var parked)) _parkedScenes[cell] = parked = new List<SceneRunState>();
            parked.Add(state);
            GD.Print($"[Scene] {state.Scene} parked at phase {state.Phase}: its cell {cell} was left");
        }
    }

    private void ResumeParkedScenes(string cell)
    {
        if (!_parkedScenes.Remove(cell, out var states)) return;
        SceneResumes.Enqueue(states);
        GD.Print($"[Scene] {states.Count} parked scene(s) wait for their actors in {cell}");
    }
}
