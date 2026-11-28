using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

// Requires: main thread only. One entry per (id, state); plain ids hold a single entry, so they stay counts.
public sealed class PlayerItems
{
    private readonly List<ItemEntry> _entries = new();

    public static PlayerItems Current { get; } = new();

    public IReadOnlyList<ItemEntry> Entries => _entries;

    public int Revision { get; private set; }

    public IReadOnlyList<InventoryStack> Stacks =>
        _entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Select(g => new InventoryStack(g.First().Id, g.Sum(e => e.Count))).ToArray();

    public int CountOf(string id) => _entries.Where(e => Same(e.Id, id)).Sum(e => e.Count);

    public int CountOf(string id, ItemState state) => _entries.FirstOrDefault(e => Same(e.Id, id) && e.State.Equals(state))?.Count ?? 0;

    // Invariant: only the stateless Add takes DefaultState; Add(id, state, count), Transform, MoveTo and ReplaceEntries never do, so saves and transfers cannot re-add default mods.
    public Func<string, ItemState>? DefaultState { get; set; }

    public ItemState NewState(string id) => DefaultState?.Invoke(id) ?? ItemState.Plain;

    public bool CanAdd(string id, ItemState state, long count) => count >= 0 && count <= int.MaxValue - CountOf(id, state);

    public void Add(string id, int count) => Add(id, count > 0 ? NewState(id) : ItemState.Plain, count);

    public void Add(string id, ItemState state, int count)
    {
        if (count <= 0) return;
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(state));
        if (at < 0) _entries.Add(new ItemEntry(id, state, count));
        else _entries[at] = _entries[at] with { Count = _entries[at].Count + count };
        Revision++;
    }

    // Constraint: all or nothing; plain copies go first, then instances in held order.
    public bool Remove(string id, int count) => MoveTo(null, id, count);

    internal bool Remove(string id, long count) => MoveEntries(null, id, count);

    // Constraint: same order as Remove; instances keep their state in the target.
    public bool MoveTo(PlayerItems? target, string id, int count) => MoveEntries(target, id, count);

    private bool MoveEntries(PlayerItems? target, string id, long count)
    {
        if (count <= 0 || _entries.Where(e => Same(e.Id, id)).Sum(e => (long)e.Count) < count) return false;
        foreach (var entry in _entries.Where(e => Same(e.Id, id)).OrderBy(e => e.State.IsPlain ? 0 : 1).ToArray())
        {
            var take = (int)Math.Min(count, entry.Count);
            Remove(id, entry.State, take);
            target?.Add(id, entry.State, take);
            if ((count -= take) == 0) break;
        }
        return true;
    }

    public bool Remove(string id, ItemState state, int count)
    {
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(state));
        if (count <= 0 || at < 0 || _entries[at].Count < count) return false;
        if (_entries[at].Count == count) _entries.RemoveAt(at);
        else _entries[at] = _entries[at] with { Count = _entries[at].Count - count };
        Revision++;
        return true;
    }

    public bool Transform(string id, ItemState from, ItemState to)
    {
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(from));
        if (at < 0) return false;
        Remove(id, from, 1);
        Add(id, to, 1);
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        Revision++;
    }

    public void Replace(IEnumerable<InventoryStack> stacks) =>
        ReplaceEntries(stacks.Select(stack => new ItemEntry(stack.Id, ItemState.Plain, stack.Count)));

    public void ReplaceEntries(IEnumerable<ItemEntry> entries)
    {
        Clear();
        foreach (var entry in entries) Add(entry.Id, entry.State, entry.Count);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
