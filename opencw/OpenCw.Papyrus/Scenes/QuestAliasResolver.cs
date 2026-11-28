using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

public sealed record AliasFillDef(string Quest, int AliasId, bool Specific, string Target);

public sealed record ForcedAlias(string Quest, int Alias, string? RefKey);

public sealed record AliasCollection(string Quest, int Alias, IReadOnlyList<string> Refs);

// Invariant: a fill resolves lazily on every call (the alias fill order is unproven); ForceRefTo and Clear override the fill until changed again, and are saved through Capture and Restore (SceneStateSave).
public sealed class QuestAliasResolver(Func<string, IReadOnlyList<AliasFillDef>> fills, Func<AliasFillDef, string?> live, Func<object?>? version = null)
{
    private object? _version;

    private sealed class SlotComparer : IEqualityComparer<(string Quest, int Alias)>
    {
        public static readonly SlotComparer Instance = new();

        public bool Equals((string Quest, int Alias) a, (string Quest, int Alias) b) => a.Alias == b.Alias && FormKeyComparer.Instance.Equals(a.Quest, b.Quest);

        public int GetHashCode((string Quest, int Alias) slot) => HashCode.Combine(FormKeyComparer.Instance.GetHashCode(slot.Quest), slot.Alias);
    }

    private readonly Dictionary<(string Quest, int Alias), string?> _forced = new(SlotComparer.Instance);
    private readonly Dictionary<string, IReadOnlyList<AliasFillDef>> _fills = new(FormKeyComparer.Instance);
    private readonly Dictionary<(string Quest, int Alias), List<string>> _members = new(SlotComparer.Instance);

    public string? Resolve(string quest, int alias)
    {
        if (_forced.TryGetValue((quest, alias), out var forced)) return forced;
        var stamp = version?.Invoke();
        if (!ReferenceEquals(stamp, _version))
        {
            _fills.Clear();
            _version = stamp;
        }
        if (!_fills.TryGetValue(quest, out var list))
        {
            list = fills(quest);
            if (list.Count > 0) _fills[quest] = list;
        }
        return list.FirstOrDefault(fill => fill.AliasId == alias) is { } found ? live(found) : null;
    }

    public void InvalidateFills() => _fills.Clear();

    public void ClearAll()
    {
        _forced.Clear();
        _fills.Clear();
        _members.Clear();
    }

    public IReadOnlyList<ForcedAlias> Capture() =>
        _forced.OrderBy(pair => pair.Key.Quest, StringComparer.Ordinal).ThenBy(pair => pair.Key.Alias).Select(pair => new ForcedAlias(pair.Key.Quest, pair.Key.Alias, pair.Value)).ToArray();

    // Invariant: replaces every forced override and every collection, so a load never keeps one the saved world did not have.
    public void Restore(IEnumerable<ForcedAlias> forced, IEnumerable<AliasCollection>? collections = null)
    {
        _forced.Clear();
        _members.Clear();
        foreach (var alias in forced) _forced[(alias.Quest, alias.Alias)] = alias.RefKey;
        foreach (var collection in collections ?? [])
            foreach (var key in collection.Refs) AddMember(collection.Quest, collection.Alias, key);
    }

    public IReadOnlyList<AliasCollection> CaptureCollections() =>
        _members.Where(pair => pair.Value.Count > 0).OrderBy(pair => pair.Key.Quest, StringComparer.Ordinal).ThenBy(pair => pair.Key.Alias)
            .Select(pair => new AliasCollection(pair.Key.Quest, pair.Key.Alias, pair.Value.ToArray())).ToArray();

    // Constraint: a collection holds only the references added to it; a fill by conditions is not modelled, so a collection starts empty.
    public IReadOnlyList<string> Members(string quest, int alias) => _members.TryGetValue((quest, alias), out var list) ? list : Array.Empty<string>();

    public bool AddMember(string quest, int alias, string refKey)
    {
        if (!_members.TryGetValue((quest, alias), out var list)) _members[(quest, alias)] = list = new List<string>();
        if (list.Contains(refKey, FormKeyComparer.Instance)) return false;
        list.Add(refKey);
        return true;
    }

    public bool RemoveMember(string quest, int alias, string refKey) =>
        _members.TryGetValue((quest, alias), out var list) && list.RemoveAll(member => FormKeyComparer.Instance.Equals(member, refKey)) > 0;

    public void ClearMembers(string quest, int alias) => _members.Remove((quest, alias));

    public void ForceRefTo(string quest, int alias, string refKey) => _forced[(quest, alias)] = refKey;

    public void Clear(string quest, int alias) => _forced[(quest, alias)] = null;

    public void ClearQuest(string quest)
    {
        foreach (var key in _forced.Keys.Where(key => FormKeyComparer.Instance.Equals(key.Quest, quest)).ToArray()) _forced.Remove(key);
        foreach (var key in _members.Keys.Where(key => FormKeyComparer.Instance.Equals(key.Quest, quest)).ToArray()) _members.Remove(key);
    }
}
