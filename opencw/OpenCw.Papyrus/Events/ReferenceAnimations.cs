using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

public sealed record AnimationKey(float Time, string Text);

public sealed record AnimationClip(string Name, float Seconds, IReadOnlyList<AnimationKey> Keys, RootPoseTrack? Root = null, string? PoseBlocked = null);

// Requires: Find may run inside a native callback and must not throw; null means no clip is known for this reference and name.
public interface IAnimationClips
{
    AnimationClip? Find(string refKey, string animation);
}

// Requires: main thread; the calls may come from inside a native callback, so an implementation must not run script events.
public interface IAnimationSink
{
    void Started(string refKey, AnimationClip clip);

    void Ended(string refKey, AnimationClip clip, bool completed);

    void Stepped(string refKey, AnimationClip clip, double seconds)
    {
    }
}

public readonly record struct ReferenceAnimationEvent(string RefKey, string Animation, string Name);

public readonly record struct AnimationWake(long Token, bool Completed);

public readonly record struct TrackRow(string RefKey, string Animation, double Time);

public readonly record struct RegistrationRow(string Owner, string Script, string SourceKey, string EventName);

public sealed record AnimationStep(IReadOnlyList<ReferenceAnimationEvent> Events, IReadOnlyList<AnimationWake> Wakes);

// Requires: main thread; Play may run inside a native callback, Advance and Deliver must run outside one.
public sealed class ReferenceAnimations(IAnimationClips clips, IAnimationSink? sink = null)
{
    public const int Capacity = 64;
    public const int RegistrationCapacity = 256;
    public const int PostedCapacity = 256;
    public const string OpenAnimation = "Open";
    public const string CloseAnimation = "Close";
    public const string DoneEvent = "done";
    public const string HandlerName = "OnAnimationEvent";

    // Invariant: tokens start above the UiServices range (1, 2, ...) because the host refuses a reused latent token.
    private static long s_lastToken = 1L << 40;

    private sealed class Track(string refKey, string animation, AnimationClip clip, string? waitEvent, long token)
    {
        public string RefKey => refKey;
        public string Animation => animation;
        public AnimationClip Clip => clip;
        public string? WaitEvent => waitEvent;
        public long Token = token;
        public double Time;
        public int NextKey;
    }

    private sealed record Registration(ScriptInstance Instance, PapyrusValue Sender, string SourceKey, string EventName);

    private readonly Dictionary<string, Track> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AnimationWake> _released = [];
    private readonly List<Registration> _registrations = [];
    private readonly List<RegistrationRow> _orphans = [];
    private readonly List<ReferenceAnimationEvent> _posted = [];

    public int Playing => _tracks.Count;

    public int Registered => _registrations.Count;

    public int Waiting => _orphans.Count;

    public int Unnamed { get; private set; }

    // Invariant: a registration whose instance has no owner cannot be named across a save and is counted in Unnamed instead.
    public IReadOnlyList<RegistrationRow> RegistrationRows()
    {
        Unnamed = 0;
        var rows = new List<RegistrationRow>();
        foreach (var entry in _registrations)
        {
            if (entry.Instance.Owner is not { } owner) { Unnamed++; continue; }
            rows.Add(new RegistrationRow(owner.Key, entry.Instance.ScriptName, entry.SourceKey, entry.EventName));
        }
        rows.AddRange(_orphans);
        return rows;
    }

    // Invariant: the live registrations are replaced; a row whose instance is not in instances waits and binds in Deliver once it attaches. Rows past RegistrationCapacity or with a blank source or event are dropped.
    public int Replace(IEnumerable<RegistrationRow> rows, IReadOnlyList<ScriptInstance> instances)
    {
        _registrations.Clear();
        _orphans.Clear();
        var kept = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.SourceKey) || string.IsNullOrEmpty(row.EventName)) continue;
            if (_registrations.Count + _orphans.Count >= RegistrationCapacity) break;
            if (Find(instances, row) is { } instance) _registrations.Add(Bind(instance, row));
            else _orphans.Add(row);
            kept++;
        }
        return kept;
    }

    private static Registration Bind(ScriptInstance instance, RegistrationRow row) =>
        new(instance, PapyrusValue.FromObject(new EngineObjectReference(row.SourceKey)), row.SourceKey, row.EventName);

    private static ScriptInstance? Find(IReadOnlyList<ScriptInstance> instances, RegistrationRow row) =>
        instances.FirstOrDefault(instance => instance.Owner is { } id && string.Equals(id.Key, row.Owner, StringComparison.Ordinal)
            && string.Equals(instance.ScriptName, row.Script, StringComparison.OrdinalIgnoreCase));

    private void BindOrphans(IReadOnlyList<ScriptInstance> instances)
    {
        foreach (var row in _orphans.ToList())
        {
            if (Find(instances, row) is not { } instance) continue;
            _orphans.Remove(row);
            _registrations.Add(Bind(instance, row));
        }
    }

    public int Skipped { get; private set; }

    public int Refused { get; private set; }

    public int Unclaimed { get; private set; }

    // Requires: main thread; an engine-side event is queued only when a script registered for this reference and name, and the next Advance raises it.
    public bool Post(string refKey, string name)
    {
        if (string.IsNullOrEmpty(refKey) || string.IsNullOrEmpty(name)
            || !_registrations.Exists(entry => string.Equals(entry.SourceKey, refKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.EventName, name, StringComparison.OrdinalIgnoreCase))
            || _posted.Count >= PostedCapacity)
        {
            Unclaimed++;
            return false;
        }
        _posted.Add(new ReferenceAnimationEvent(refKey, "", name));
        return true;
    }

    public static long NextToken() => Interlocked.Increment(ref s_lastToken);

    public int? OpenState(string refKey) => _states.TryGetValue(refKey, out var state) ? state : null;

    public bool Play(string refKey, string animation, string? waitEvent = null, long token = 0)
    {
        if (clips.Find(refKey, animation) is not { } clip || !float.IsFinite(clip.Seconds) || clip.Seconds <= 0) return false;
        if (!_tracks.ContainsKey(refKey) && _tracks.Count >= Capacity) return false;
        Cut(refKey);
        _tracks[refKey] = new Track(refKey, animation, clip, waitEvent, token);
        MarkRunning(refKey, animation);
        sink?.Started(refKey, clip);
        return true;
    }

    public IReadOnlyList<TrackRow> TrackRows() =>
        _tracks.Values.OrderBy(track => track.RefKey, StringComparer.Ordinal).Select(track => new TrackRow(track.RefKey, track.Animation, track.Time)).ToList();

    // Invariant: a resumed track has no wait token, because a suspended script is not saved; a row whose clip is unknown, finished or already playing is skipped.
    public int Resume(IEnumerable<TrackRow> rows)
    {
        var resumed = 0;
        foreach (var row in rows)
        {
            if (_tracks.Count >= Capacity) break;
            if (_tracks.ContainsKey(row.RefKey) || !double.IsFinite(row.Time) || row.Time < 0) continue;
            if (clips.Find(row.RefKey, row.Animation) is not { } clip || !float.IsFinite(clip.Seconds) || clip.Seconds <= 0 || row.Time >= clip.Seconds) continue;
            var track = new Track(row.RefKey, row.Animation, clip, null, 0) { Time = row.Time };
            while (track.NextKey < clip.Keys.Count && clip.Keys[track.NextKey].Time <= row.Time) track.NextKey++;
            _tracks[row.RefKey] = track;
            MarkRunning(row.RefKey, row.Animation);
            sink?.Started(row.RefKey, clip);
            resumed++;
        }
        return resumed;
    }

    private void MarkRunning(string refKey, string animation)
    {
        if (Is(animation, OpenAnimation)) _states[refKey] = ReferenceOpenNativeAdapters.StateOpening;
        else if (Is(animation, CloseAnimation)) _states[refKey] = ReferenceOpenNativeAdapters.StateClosing;
    }

    public bool Register(ScriptInstance instance, PapyrusValue sender, string sourceKey, string eventName)
    {
        if (_registrations.Exists(entry => Same(entry, instance, sourceKey, eventName))) return true;
        if (_registrations.Count >= RegistrationCapacity) return false;
        _registrations.Add(new Registration(instance, sender, sourceKey, eventName));
        return true;
    }

    public void Unregister(ScriptInstance instance, string sourceKey, string eventName) =>
        _registrations.RemoveAll(entry => Same(entry, instance, sourceKey, eventName));

    // Invariant: a wait is never lost, because a cut track queues its token and the next Advance returns it with Completed false.
    public AnimationStep Advance(double seconds)
    {
        var events = new List<ReferenceAnimationEvent>(_posted);
        _posted.Clear();
        var wakes = new List<AnimationWake>(_released);
        _released.Clear();
        if (double.IsFinite(seconds) && seconds > 0)
            foreach (var track in _tracks.Values.ToList()) Step(track, seconds, events, wakes);
        return new AnimationStep(events, wakes);
    }

    // Requires: outside any native callback; waits are completed before the events run.
    public int Deliver(IScriptHost host, double seconds)
    {
        var step = Advance(seconds);
        BindOrphans(host.Instances);
        foreach (var wake in step.Wakes) host.CompleteLatent(wake.Token, PapyrusValue.FromBool(wake.Completed));
        var delivered = 0;
        foreach (var raised in step.Events)
            foreach (var entry in _registrations.ToList())
            {
                if (!string.Equals(entry.SourceKey, raised.RefKey, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(entry.EventName, raised.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Instance.Detached || !host.Instances.Contains(entry.Instance) || !entry.Instance.DefinesFunction(HandlerName))
                {
                    Skipped++;
                    continue;
                }
                var result = host.RunEvent(entry.Instance, HandlerName, entry.Sender, PapyrusValue.FromString(raised.Name));
                if (result.Completed || result.Reason == VmStopReason.Waiting) delivered++;
                else Refused++;
            }
        return delivered;
    }

    // Invariant: pending waits are dropped, not completed, because Clear runs when a new session starts and a wait belongs to the old host.
    public void Clear()
    {
        foreach (var refKey in _tracks.Keys.ToList()) Cut(refKey);
        _released.Clear();
        _posted.Clear();
        _registrations.Clear();
        _orphans.Clear();
        _states.Clear();
    }

    private void Step(Track track, double seconds, List<ReferenceAnimationEvent> events, List<AnimationWake> wakes)
    {
        track.Time += seconds;
        var length = track.Clip.Seconds;
        sink?.Stepped(track.RefKey, track.Clip, Math.Min(track.Time, length));
        var keys = track.Clip.Keys;
        var done = track.Time >= length;
        while (track.NextKey < keys.Count && (done || keys[track.NextKey].Time <= track.Time))
            Raise(track, keys[track.NextKey++].Text, events, wakes);
        if (!done) return;
        Raise(track, DoneEvent, events, wakes);
        if (track.Token != 0) wakes.Add(new AnimationWake(track.Token, false));
        _tracks.Remove(track.RefKey);
        Settle(track);
        sink?.Ended(track.RefKey, track.Clip, true);
    }

    private static void Raise(Track track, string name, List<ReferenceAnimationEvent> events, List<AnimationWake> wakes)
    {
        events.Add(new ReferenceAnimationEvent(track.RefKey, track.Animation, name));
        if (track.Token == 0 || !string.Equals(name, track.WaitEvent, StringComparison.OrdinalIgnoreCase)) return;
        wakes.Add(new AnimationWake(track.Token, true));
        track.Token = 0;
    }

    private void Cut(string refKey)
    {
        if (!_tracks.Remove(refKey, out var track)) return;
        Settle(track);
        if (track.Token != 0) _released.Add(new AnimationWake(track.Token, false));
        sink?.Ended(refKey, track.Clip, false);
    }

    private void Settle(Track track)
    {
        if (Is(track.Animation, OpenAnimation)) _states[track.RefKey] = ReferenceOpenNativeAdapters.StateOpen;
        else if (Is(track.Animation, CloseAnimation)) _states[track.RefKey] = ReferenceOpenNativeAdapters.StateClosed;
    }

    private static bool Is(string animation, string name) => string.Equals(animation, name, StringComparison.OrdinalIgnoreCase);

    private static bool Same(Registration entry, ScriptInstance instance, string sourceKey, string eventName) =>
        ReferenceEquals(entry.Instance, instance)
        && string.Equals(entry.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.EventName, eventName, StringComparison.OrdinalIgnoreCase);
}
