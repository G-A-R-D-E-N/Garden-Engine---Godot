using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

// Requires: main thread; Advance and Deliver are the only clock steps, so a timer fires when the summed advances reach its interval, and each timer fires once.
public sealed class ScriptTimers
{
    public const int Capacity = 1024;
    public const string HandlerName = "OnTimer";

    private readonly record struct Pending(ScriptInstance Instance, int Id, double Due, long Sequence);

    private readonly record struct Orphan(string Owner, string Script, int Id, double Due, long Sequence);

    private readonly List<Pending> _timers = new();
    private readonly List<Orphan> _orphans = new();
    private double _now;
    private long _sequence;

    public int Count => _timers.Count;

    public int Waiting => _orphans.Count;

    public int Unnamed { get; private set; }

    // Invariant: rows come in firing order; a timer whose instance has no owner cannot be named across a save and is counted in Unnamed instead.
    public IReadOnlyList<TimerRow> Snapshot()
    {
        Unnamed = 0;
        var rows = new List<(double Due, long Sequence, TimerRow Row)>();
        foreach (var timer in _timers)
        {
            if (timer.Instance.Owner is not { } owner) { Unnamed++; continue; }
            rows.Add((timer.Due, timer.Sequence, new TimerRow(owner.Key, timer.Instance.ScriptName, timer.Id, Math.Max(0, timer.Due - _now))));
        }
        foreach (var orphan in _orphans) rows.Add((orphan.Due, orphan.Sequence, new TimerRow(orphan.Owner, orphan.Script, orphan.Id, Math.Max(0, orphan.Due - _now))));
        return rows.OrderBy(row => row.Due).ThenBy(row => row.Sequence).Select(row => row.Row).ToList();
    }

    // Invariant: the live table is replaced; a row whose instance is not in instances waits and binds in Deliver once it attaches. Returns the rows kept, rows past Capacity or with a non-finite time are dropped.
    public int Replace(IEnumerable<TimerRow> rows, IReadOnlyList<ScriptInstance> instances)
    {
        _timers.Clear();
        _orphans.Clear();
        var kept = 0;
        foreach (var row in rows)
        {
            if (!double.IsFinite(row.Remaining) || _timers.Count + _orphans.Count >= Capacity) continue;
            var due = _now + Math.Max(0, row.Remaining);
            if (Find(instances, row.Owner, row.Script) is { } instance) _timers.Add(new Pending(instance, row.Id, due, _sequence++));
            else _orphans.Add(new Orphan(row.Owner, row.Script, row.Id, due, _sequence++));
            kept++;
        }
        return kept;
    }

    private static ScriptInstance? Find(IReadOnlyList<ScriptInstance> instances, string owner, string script) =>
        instances.FirstOrDefault(instance => instance.Owner is { } id && string.Equals(id.Key, owner, StringComparison.Ordinal)
            && string.Equals(instance.ScriptName, script, StringComparison.OrdinalIgnoreCase));

    private void BindOrphans(IReadOnlyList<ScriptInstance> instances)
    {
        if (_orphans.Count == 0) return;
        foreach (var orphan in _orphans.ToList())
        {
            if (Find(instances, orphan.Owner, orphan.Script) is not { } instance) continue;
            _orphans.Remove(orphan);
            _timers.Add(new Pending(instance, orphan.Id, orphan.Due, orphan.Sequence));
        }
    }

    // Invariant: a timer with the same instance and id is replaced; false means the interval is not finite or the table is full.
    public bool Start(ScriptInstance instance, double interval, int id)
    {
        if (!double.IsFinite(interval)) return false;
        var replaced = _timers.RemoveAll(timer => ReferenceEquals(timer.Instance, instance) && timer.Id == id) > 0;
        if (!replaced && _timers.Count >= Capacity) return false;
        _timers.Add(new Pending(instance, id, _now + Math.Max(0, interval), _sequence++));
        return true;
    }

    public bool Cancel(ScriptInstance instance, int id) =>
        _timers.RemoveAll(timer => ReferenceEquals(timer.Instance, instance) && timer.Id == id) > 0;

    public IReadOnlyList<(ScriptInstance Instance, int Id)> Advance(double seconds)
    {
        AddElapsed(seconds);
        var due = _timers.Where(timer => timer.Due <= _now).OrderBy(timer => timer.Due).ThenBy(timer => timer.Sequence)
            .Select(timer => (timer.Instance, timer.Id)).ToList();
        _timers.RemoveAll(timer => timer.Due <= _now);
        return due;
    }

    private void AddElapsed(double seconds)
    {
        if (double.IsFinite(seconds) && seconds > 0) _now += seconds;
    }

    public int Skipped { get; private set; }

    public int Refused { get; private set; }

    // Requires: must run outside a native callback; a discarded or detached instance, or one with no OnTimer, is skipped and counted, a refused run is counted and not retried.
    public int Deliver(IScriptHost host, double seconds)
    {
        AddElapsed(seconds);
        BindOrphans(host.Instances);
        var limit = _sequence;
        var delivered = 0;
        var parked = new List<Pending>();
        while (TakeDue(limit) is { } due)
        {
            if (due.Instance.Detached && host.Instances.Contains(due.Instance))
            {
                parked.Add(due);
                continue;
            }
            if (due.Instance.Detached || !host.Instances.Contains(due.Instance) || !due.Instance.DefinesFunction(HandlerName))
            {
                Skipped++;
                continue;
            }
            var result = host.RunEvent(due.Instance, HandlerName, PapyrusValue.FromInt(due.Id));
            if (result.Completed || result.Reason == VmStopReason.Waiting) delivered++;
            else Refused++;
        }
        _timers.AddRange(parked);
        return delivered;
    }

    private Pending? TakeDue(long limit)
    {
        var best = -1;
        for (var i = 0; i < _timers.Count; i++)
        {
            var timer = _timers[i];
            if (timer.Due > _now || timer.Sequence >= limit) continue;
            if (best < 0 || timer.Due < _timers[best].Due || (timer.Due == _timers[best].Due && timer.Sequence < _timers[best].Sequence)) best = i;
        }
        if (best < 0) return null;
        var found = _timers[best];
        _timers.RemoveAt(best);
        return found;
    }

    public void Clear()
    {
        _timers.Clear();
        _orphans.Clear();
    }
}

public readonly record struct TimerRow(string Owner, string Script, int Id, double Remaining);
