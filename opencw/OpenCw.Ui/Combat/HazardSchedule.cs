using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Combat;

public sealed record HazardTick(int Id, int Count);

// Requires: main thread only; the caller passes one frame delta per Advance.
public sealed class HazardSchedule
{
    private const double Epsilon = 1e-7;

    private readonly List<Active> _active = [];
    private int _nextId;

    public int Count => _active.Count;

    public bool Contains(int id) => _active.Exists(active => active.Id == id);

    // Invariant: a hazard added with an age has already ticked for that age, so it does not tick again for time it lived through before a save; an age at or past a finite lifetime is refused.
    public int? Add(HazardInfo info, double age = 0)
    {
        if (!float.IsFinite(info.TargetInterval) || info.TargetInterval <= 0f) return null;
        if (!float.IsFinite(info.Lifetime) || info.Lifetime < 0f) return null;
        if (!double.IsFinite(age) || age < 0 || (info.Lifetime > 0 && age >= info.Lifetime)) return null;
        while (info.Limit > 0 && _active.Count >= info.Limit) _active.RemoveAt(0);
        var id = _nextId++;
        _active.Add(new Active(id, info) { Age = age, Ticked = (long)Math.Floor(age / info.TargetInterval * (1 + Epsilon)) });
        return id;
    }

    public double AgeOf(int id) => _active.Find(active => active.Id == id)?.Age ?? 0;

    public IReadOnlyList<HazardTick> Advance(double seconds)
    {
        var ticks = new List<HazardTick>();
        if (!double.IsFinite(seconds) || seconds <= 0) return ticks;
        foreach (var active in _active)
        {
            var age = active.Age + seconds;
            var end = active.Lifetime > 0 ? Math.Min(age, active.Lifetime) : age;
            var reached = (long)Math.Floor(end / active.Interval * (1 + Epsilon));
            if (reached > active.Ticked) ticks.Add(new HazardTick(active.Id, (int)Math.Min(reached - active.Ticked, int.MaxValue)));
            active.Ticked = reached;
            active.Age = age;
        }
        _active.RemoveAll(active => active.Lifetime > 0 && active.Age >= active.Lifetime);
        return ticks;
    }

    private sealed class Active(int id, HazardInfo info)
    {
        public int Id => id;
        public double Interval => info.TargetInterval;
        public double Lifetime => info.Lifetime;
        public double Age { get; set; }
        public long Ticked { get; set; }
    }
}
