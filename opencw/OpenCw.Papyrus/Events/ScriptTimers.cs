using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

// Requires: main thread; Advance and Deliver are the only clock steps, so a timer fires when the summed advances reach its interval, and each timer fires once.
public sealed class ScriptTimers(string handler = ScriptTimers.HandlerName)
{
    public const int Capacity = 1024;
    public const string HandlerName = "OnTimer";
    public const string GameTimeHandlerName = "OnTimerGameTime";

    private readonly record struct Pending(ScriptInstance Instance, int Id, double Due, long Sequence);

    private readonly List<Pending> _timers = new();
    private double _now;
    private long _sequence;

    public int Count => _timers.Count;

    public IReadOnlyList<(ScriptInstance Instance, int Id, double Remaining)> Scheduled =>
        _timers.OrderBy(timer => timer.Sequence).Select(timer => (timer.Instance, timer.Id, Math.Max(0, timer.Due - _now))).ToArray();

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
        var limit = _sequence;
        var delivered = 0;
        while (TakeDue(limit) is { } due)
        {
            if (due.Instance.Detached || !host.Instances.Contains(due.Instance) || !due.Instance.DefinesFunction(handler))
            {
                Skipped++;
                continue;
            }
            var result = host.RunEvent(due.Instance, handler, PapyrusValue.FromInt(due.Id));
            if (result.Completed || result.Reason == VmStopReason.Waiting) delivered++;
            else Refused++;
        }
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

    public void Clear() => _timers.Clear();
}
