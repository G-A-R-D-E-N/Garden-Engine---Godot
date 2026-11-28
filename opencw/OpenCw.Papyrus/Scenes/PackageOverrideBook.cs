using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

// Invariant: the newest override of an actor wins, and an entry holds until every Begin of it was Ended.
public sealed class PackageOverrideBook<T> where T : class
{
    private readonly Dictionary<string, List<(string Package, T Value, int Holders)>> _byActor = new(StringComparer.Ordinal);

    public int Revision { get; private set; }

    public int Count => _byActor.Values.Sum(list => list.Count);

    public void Begin(string actor, string package, T value)
    {
        if (!_byActor.TryGetValue(actor, out var list)) _byActor[actor] = list = new List<(string, T, int)>();
        var holders = list.Where(entry => entry.Package == package).Sum(entry => entry.Holders) + 1;
        list.RemoveAll(entry => entry.Package == package);
        list.Add((package, value, holders));
        Revision++;
    }

    public bool End(string actor, string package)
    {
        if (!_byActor.TryGetValue(actor, out var list) || list.FindIndex(entry => entry.Package == package) is not (>= 0 and var index)) return false;
        var entry = list[index];
        if (entry.Holders > 1) list[index] = entry with { Holders = entry.Holders - 1 };
        else list.RemoveAt(index);
        if (list.Count == 0) _byActor.Remove(actor);
        Revision++;
        return true;
    }

    public T? Of(string actor) => _byActor.TryGetValue(actor, out var list) && list.Count > 0 ? list[^1].Value : null;

    public void Clear()
    {
        if (_byActor.Count == 0) return;
        _byActor.Clear();
        Revision++;
    }
}
