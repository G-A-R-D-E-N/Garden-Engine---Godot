using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public sealed record HitFilter(string Target, string? Aggressor, string? Source, string? Projectile, int Power, int Sneak, int Bash, int Block, bool Match);

public readonly record struct HitBlow(string Victim, string? Aggressor, string? Source = null, string? Projectile = null,
    bool Power = false, bool Sneak = false, bool Bash = false, bool Blocked = false);

// Requires: main thread; the registrations of one session, cleared when a new session starts.
public sealed class HitRegistrations
{
    public const int Capacity = 256;

    private sealed record Entry(ScriptInstance Instance, HitFilter Filter);

    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    // Invariant: the same instance and filter is one registration; a full table refuses.
    public bool Register(ScriptInstance instance, HitFilter filter)
    {
        if (_entries.Exists(entry => Same(entry, instance, filter))) return true;
        if (_entries.Count >= Capacity) return false;
        _entries.Add(new Entry(instance, filter));
        return true;
    }

    public void Unregister(ScriptInstance instance, HitFilter filter) =>
        _entries.RemoveAll(entry => Same(entry, instance, filter));

    // Invariant: no target drops every registration of the instance, a target drops only that target's.
    public void UnregisterAll(ScriptInstance instance, string? target) =>
        _entries.RemoveAll(entry => entry.Instance == instance
            && (target is null || string.Equals(entry.Filter.Target, target, StringComparison.OrdinalIgnoreCase)));

    public void Clear() => _entries.Clear();

    public IReadOnlyList<ScriptInstance> Matching(HitBlow blow) =>
        _entries.Where(entry => string.Equals(entry.Filter.Target, blow.Victim, StringComparison.OrdinalIgnoreCase) && Passes(entry.Filter, blow) == entry.Filter.Match)
            .Select(entry => entry.Instance).Distinct().ToList();

    private static bool Same(Entry entry, ScriptInstance instance, HitFilter filter) =>
        entry.Instance == instance && entry.Filter == filter;

    // Invariant: an unset filter matches anything, and a value Garden does not report counts as None or false.
    private static bool Passes(HitFilter filter, HitBlow blow) =>
        Is(filter.Aggressor, blow.Aggressor) && Is(filter.Source, blow.Source) && Is(filter.Projectile, blow.Projectile)
        && Flag(filter.Power, blow.Power) && Flag(filter.Sneak, blow.Sneak) && Flag(filter.Bash, blow.Bash) && Flag(filter.Block, blow.Blocked);

    private static bool Is(string? wanted, string? actual) =>
        wanted is null || string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Flag(int wanted, bool actual) => wanted < 0 || (wanted != 0) == actual;
}
