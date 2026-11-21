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

    public IReadOnlyList<InventoryStack> Stacks =>
        _entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Select(g => new InventoryStack(g.First().Id, Total(g))).ToArray();

    public int CountOf(string id) => Total(_entries.Where(e => Same(e.Id, id)));

    public static int Total(IEnumerable<ItemEntry> entries) => (int)Math.Min(entries.Sum(e => (long)e.Count), int.MaxValue);

    // Constraint: a count above int.MaxValue is not representable, so a transfer that would pass it is refused whole.
    public bool CanAdd(string id, ItemState state, long count) => (long)CountOf(id, state) + count <= int.MaxValue;

    public int CountOf(string id, ItemState state) => _entries.FirstOrDefault(e => Same(e.Id, id) && e.State.Equals(state))?.Count ?? 0;

    // Invariant: only the stateless Add takes DefaultState; Add(id, state, count), Transform, MoveTo and ReplaceEntries never do, so saves and transfers cannot re-add default mods.
    public Func<string, ItemState>? DefaultState { get; set; }

    public ItemState NewState(string id) => DefaultState?.Invoke(id) ?? ItemState.Plain;

    public void Add(string id, int count) => Add(id, count > 0 ? NewState(id) : ItemState.Plain, count);

    public void Add(string id, ItemState state, int count)
    {
        if (count <= 0) return;
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(state));
        if (at < 0) _entries.Add(new ItemEntry(id, state, count));
        else _entries[at] = _entries[at] with { Count = (int)Math.Min((long)_entries[at].Count + count, int.MaxValue) };
    }

    // Constraint: all or nothing; plain copies go first, then instances in held order.
    public bool Remove(string id, int count) => MoveTo(null, id, count);

    // Constraint: same order as Remove; instances keep their state in the target.
    public bool MoveTo(PlayerItems? target, string id, int count) => MoveTo(target, id, count, null, null);

    // Constraint: map changes each moved state on its way into the target; moved lists the source states and counts taken; order ranks the states to take first (default plain copies first, then instances in held order).
    public bool MoveTo(PlayerItems? target, string id, int count, Func<ItemState, ItemState>? map, List<(ItemState State, int Count)>? moved, Func<ItemState, int>? order = null)
    {
        if (count <= 0 || CountOf(id) < count) return false;
        var plan = new List<(ItemState State, int Take)>();
        foreach (var entry in _entries.Where(e => Same(e.Id, id)).OrderBy(e => order?.Invoke(e.State) ?? (e.State.IsPlain ? 0 : 1)))
        {
            var take = Math.Min(count, entry.Count);
            plan.Add((entry.State, take));
            if ((count -= take) == 0) break;
        }
        if (target is not null && plan.GroupBy(step => map?.Invoke(step.State) ?? step.State).Any(group => !target.CanAdd(id, group.Key, group.Sum(step => (long)step.Take))))
            return false;
        foreach (var (state, take) in plan)
        {
            Remove(id, state, take);
            target?.Add(id, map?.Invoke(state) ?? state, take);
            moved?.Add((state, take));
        }
        return true;
    }

    public bool Remove(string id, ItemState state, int count)
    {
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(state));
        if (count <= 0 || at < 0 || _entries[at].Count < count) return false;
        if (_entries[at].Count == count) _entries.RemoveAt(at);
        else _entries[at] = _entries[at] with { Count = _entries[at].Count - count };
        return true;
    }

    public event Action<string, ItemState, ItemState>? Transformed;

    public bool Transform(string id, ItemState from, ItemState to)
    {
        var at = _entries.FindIndex(e => Same(e.Id, id) && e.State.Equals(from));
        if (at < 0) return false;
        Remove(id, from, 1);
        Add(id, to, 1);
        Transformed?.Invoke(id, from, to);
        return true;
    }

    public void Clear() => _entries.Clear();

    public void Replace(IEnumerable<InventoryStack> stacks) =>
        ReplaceEntries(stacks.Select(stack => new ItemEntry(stack.Id, ItemState.Plain, stack.Count)));

    public void ReplaceEntries(IEnumerable<ItemEntry> entries)
    {
        Clear();
        foreach (var entry in entries) Add(entry.Id, entry.State, entry.Count);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
