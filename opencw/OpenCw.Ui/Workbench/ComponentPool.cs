using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Workbench;

// Requires: main thread; counts held items plus the components of held MISC junk, and consumption is all or nothing; the stash stores are drawn on after the player's own items.
public sealed class ComponentPool(IRecordReader reader, PlayerItems items, IReadOnlyList<PlayerItems>? stash = null) : IItemStock
{
    private readonly PlayerItems[] _stores = stash is null ? [items] : new[] { items }.Concat(stash).Distinct().ToArray();

    private IEnumerable<ItemEntry> AllStacks => _stores.SelectMany(store => store.Entries);

    private readonly Dictionary<string, Dictionary<string, int>?> _yields = new(StringComparer.OrdinalIgnoreCase);

    public int CountOf(string id)
    {
        long total = 0;
        foreach (var stack in AllStacks)
        {
            if (string.Equals(stack.Id, id, StringComparison.OrdinalIgnoreCase)) total += stack.Count;
            else if (YieldOf(stack.Id) is { } y && y.TryGetValue(id, out var per)) total += (long)per * stack.Count;
            if (total >= int.MaxValue) return int.MaxValue;
        }
        return (int)total;
    }

    public bool Consume(IReadOnlyList<WorkshopMaterial> needs) => Consume(needs, []);

    internal bool Consume(IReadOnlyList<WorkshopMaterial> needs, IReadOnlyList<(string Item, int Count)> outputs)
    {
        if (Plan(needs, outputs) is not { } plan) return false;
        var stores = _stores;
        var before = stores.Select(store => store.Entries.ToArray()).ToArray();
        foreach (var (id, count) in plan.Taken)
            if (!Take(stores, id, count))
            {
                for (var i = 0; i < stores.Length; i++) stores[i].ReplaceEntries(before[i]);
                return false;
            }
        foreach (var entry in plan.Refund)
            items.Add(entry.Id, entry.State, entry.Count);
        return true;
    }


    private static long Held(PlayerItems store, string id) => store.Entries.Where(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)).Sum(entry => (long)entry.Count);

    private static bool Take(PlayerItems[] stores, string id, long count)
    {
        var left = count;
        foreach (var store in stores)
        {
            if (left <= 0) break;
            var part = Math.Min(left, Held(store, id));
            if (part <= 0) continue;
            if (!store.Remove(id, part)) return false;
            left -= part;
        }
        return left <= 0;
    }

    public void Credit(IReadOnlyList<WorkshopMaterial> parts)
    {
        if (PlanCredit(parts) is { } rewards) Pay(rewards);
    }

    internal Action Checkpoint()
    {
        var stores = _stores;
        var held = stores.Select(store => store.Entries.ToArray()).ToArray();
        return () =>
        {
            for (var i = 0; i < stores.Length; i++) stores[i].ReplaceEntries(held[i]);
        };
    }

    internal void Pay(IReadOnlyList<ItemEntry> rewards)
    {
        foreach (var entry in rewards) items.Add(entry.Id, entry.State, entry.Count);
    }

    internal IReadOnlyList<ItemEntry>? PlanCredit(IReadOnlyList<WorkshopMaterial> parts)
    {
        var beforeRead = reader.FailureCount;
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Where(part => part.Needed > 0))
            if (RecordRows.Link(reader, part.Id, RecordRows.ComponentScrapItemPath) is { } scrap)
                totals[scrap] = totals.GetValueOrDefault(scrap) + part.Needed;
        var rewards = new List<ItemEntry>();
        foreach (var (id, count) in totals)
        {
            var state = items.NewState(id);
            if (!items.CanAdd(id, state, count)) return null;
            rewards.Add(new ItemEntry(id, state, (int)count));
        }
        return beforeRead == reader.FailureCount ? rewards : null;
    }

    private long _tolerated;

    private bool Unreadable(long beforeRead, long toleratedBefore) => reader.FailureCount - beforeRead != _tolerated - toleratedBefore;

    private Dictionary<string, int>? YieldOf(string itemId)
    {
        if (_yields.TryGetValue(itemId, out var cached)) return cached;
        var beforeRead = reader.FailureCount;
        var parts = ScrapYield.Of(reader, itemId);
        if (reader.FailureCount != beforeRead)
        {
            _tolerated += reader.FailureCount - beforeRead;
            return null;
        }
        return _yields[itemId] = parts is { Count: > 0 } ? parts.ToDictionary(part => part.Id, part => part.Needed, StringComparer.OrdinalIgnoreCase) : null;
    }

    public bool CanConsume(IReadOnlyList<WorkshopMaterial> needs) => needs.All(need => need.Needed <= 0) || Plan(needs, []) is not null;

    internal int CountPlacements(IReadOnlyList<WorkshopMaterial> costs, int limit)
    {
        if (costs.All(cost => cost.Needed <= 0)) return limit;
        static PlayerItems Copy(PlayerItems store)
        {
            var copy = new PlayerItems { DefaultState = store.DefaultState };
            copy.ReplaceEntries(store.Entries);
            return copy;
        }
        var simulation = new ComponentPool(reader, Copy(items), _stores.Skip(1).Select(Copy).ToArray());
        var stores = simulation._stores;
        var grouped = costs.Where(cost => cost.Needed > 0).ToDictionary(cost => cost.Id, cost => (long)cost.Needed, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        while (count < limit)
        {
            var beforeRead = reader.FailureCount;
            var toleratedBefore = simulation._tolerated;
            var greedy = simulation.PlanGreedy(grouped, []);
            if (simulation.Unreadable(beforeRead, toleratedBefore)) break;
            var plan = greedy ?? simulation.Search(grouped, []);
            if (plan is null || simulation.Unreadable(beforeRead, toleratedBefore)) break;
            var step = 1;
            if (greedy is not null && plan.Refund.Length == 0)
                step = plan.Taken.Min(pair => (int)Math.Min(limit - count,
                    stores.Sum(store => Held(store, pair.Key)) / pair.Value));
            foreach (var (id, units) in plan.Taken)
                if (!Take(stores, id, checked(units * step))) return count;
            foreach (var refund in plan.Refund) stores[0].Add(refund.Id, refund.State, refund.Count);
            count += step;
        }
        return count;
    }

    private ConsumePlan? Plan(IReadOnlyList<WorkshopMaterial> needs, IReadOnlyList<(string Item, int Count)> outputs)
    {
        var costs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var need in needs.Where(need => need.Needed > 0))
        {
            var total = costs.GetValueOrDefault(need.Id) + need.Needed;
            costs[need.Id] = total;
        }
        var beforeRead = reader.FailureCount;
        var toleratedBefore = _tolerated;
        var plan = PlanGreedy(costs, outputs);
        if (Unreadable(beforeRead, toleratedBefore)) return null;
        plan ??= Search(costs, outputs);
        return Unreadable(beforeRead, toleratedBefore) ? null : plan;
    }

    private const int SearchBudget = 2048;

    private ConsumePlan? Search(Dictionary<string, long> costs, IReadOnlyList<(string Item, int Count)> outputs)
    {
        var ids = costs.Keys.ToArray();
        var held = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in AllStacks) held[stack.Id] = held.GetValueOrDefault(stack.Id) + stack.Count;
        var start = new SearchState(
            held,
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
            ids.Select(id => costs[id]).ToArray());
        var budget = SearchBudget;
        var dead = new HashSet<string>(StringComparer.Ordinal);
        return Solve(start, ids, 0, dead, outputs, ref budget);
    }

    private ConsumePlan? Solve(SearchState state, string[] ids, int index, HashSet<string> dead, IReadOnlyList<(string Item, int Count)> outputs, ref int budget)
    {
        if (budget <= 0) return null;
        for (; index < ids.Length; index++)
        {
            state.Left[index] -= Draw(state.Surplus, ids[index], state.Left[index]);
            if (state.Left[index] > 0) break;
        }
        if (index == ids.Length)
        {
            budget--;
            return Finish(state.Taken, state.Surplus, outputs);
        }
        var signature = state.Signature(index);
        if (dead.Contains(signature)) return null;
        budget--;
        var id = ids[index];
        var left = state.Left[index];
        var direct = Math.Min(state.Held.GetValueOrDefault(id), left);
        if (direct > 0)
        {
            var next = state.Copy();
            next.Held[id] -= direct;
            next.Taken[id] = next.Taken.GetValueOrDefault(id) + direct;
            next.Left[index] -= direct;
            if (Solve(next, ids, index, dead, outputs, ref budget) is { } found) return found;
            if (budget <= 0) return null;
        }
        var candidates = state.Held.Where(pair => pair.Value > 0 && YieldOf(pair.Key) is { } y && y.ContainsKey(id))
            .Select(pair => new Junk(pair.Key, YieldOf(pair.Key)!))
            .OrderBy(candidate => candidate.Yield.Count).ThenByDescending(candidate => candidate.Yield[id])
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToList();
        foreach (var junk in candidates)
        {
            var per = (long)junk.Yield[id];
            for (var units = Math.Min(state.Held[junk.Id], (left + per - 1) / per); units > 0; units--)
            {
                var next = state.Copy();
                next.Held[junk.Id] -= units;
                next.Taken[junk.Id] = next.Taken.GetValueOrDefault(junk.Id) + units;
                foreach (var (component, amount) in junk.Yield)
                    next.Surplus[component] = next.Surplus.GetValueOrDefault(component) + (decimal)amount * units;
                if (Solve(next, ids, index, dead, outputs, ref budget) is { } found) return found;
                if (budget <= 0) return null;
            }
        }
        dead.Add(signature);
        return null;
    }

    private ConsumePlan? PlanGreedy(Dictionary<string, long> costs, IReadOnlyList<(string Item, int Count)> outputs)
    {
        var held = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in AllStacks) held[stack.Id] = held.GetValueOrDefault(stack.Id) + stack.Count;
        var taken = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var surplus = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, cost) in costs)
        {
            var remaining = cost;
            remaining -= Draw(surplus, id, remaining);
            var direct = Math.Min(held.GetValueOrDefault(id), remaining);
            if (direct > 0)
            {
                held[id] -= direct;
                taken[id] = taken.GetValueOrDefault(id) + direct;
                remaining -= direct;
            }
            while (remaining > 0)
            {
                var pick = held.Where(pair => pair.Value > 0 && YieldOf(pair.Key) is { } y && y.ContainsKey(id))
                    .Select(pair => new Junk(pair.Key, YieldOf(pair.Key)!))
                    .OrderBy(candidate => candidate.Yield.Count).ThenByDescending(candidate => candidate.Yield[id])
                    .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault();
                if (pick is not { } chosen) return null;
                var per = (long)chosen.Yield[id];
                var units = Math.Min(held[chosen.Id], (remaining + per - 1) / per);
                if (units <= 0) return null;
                held[chosen.Id] -= units;
                taken[chosen.Id] = taken.GetValueOrDefault(chosen.Id) + units;
                foreach (var (component, amount) in chosen.Yield)
                    surplus[component] = surplus.GetValueOrDefault(component) + (decimal)amount * units;
                remaining -= Draw(surplus, id, remaining);
            }
        }
        return Finish(taken, surplus, outputs);
    }

    private ConsumePlan? Finish(Dictionary<string, long> taken, Dictionary<string, decimal> surplus, IReadOnlyList<(string Item, int Count)> outputs)
    {
        var refund = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, count) in outputs)
        {
            var total = (long)refund.GetValueOrDefault(id) + count;
            if (total > int.MaxValue) return null;
            refund[id] = (int)total;
        }
        foreach (var (component, count) in surplus.Where(pair => pair.Value > 0))
            if (RecordRows.Link(reader, component, RecordRows.ComponentScrapItemPath) is { } scrap)
            {
                var total = refund.GetValueOrDefault(scrap) + count;
                if (total > int.MaxValue) return null;
                refund[scrap] = (int)total;
            }
        var payouts = refund.Select(pair => new ItemEntry(pair.Key, items.NewState(pair.Key), pair.Value)).ToArray();
        if (payouts.Any(entry => !items.CanAdd(entry.Id, entry.State, entry.Count)))
        {
            var after = new PlayerItems();
            after.ReplaceEntries(items.Entries);
            foreach (var (id, count) in taken)
                after.Remove(id, Math.Min(count, Held(after, id)));
            if (payouts.Any(entry => !after.CanAdd(entry.Id, entry.State, entry.Count))) return null;
        }
        return new ConsumePlan(taken, payouts);
    }

    private static long Draw(Dictionary<string, decimal> surplus, string id, long wanted)
    {
        var take = (long)Math.Min(surplus.GetValueOrDefault(id), wanted);
        if (take > 0) surplus[id] -= take;
        return take;
    }

    private sealed record SearchState(Dictionary<string, long> Held, Dictionary<string, long> Taken, Dictionary<string, decimal> Surplus, long[] Left)
    {
        public SearchState Copy() => new(
            new Dictionary<string, long>(Held, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, long>(Taken, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(Surplus, StringComparer.OrdinalIgnoreCase),
            (long[])Left.Clone());

        public string Signature(int index)
        {
            static string Rows<T>(Dictionary<string, T> map) where T : struct, IComparable<T> =>
                string.Join(",", map.Where(pair => pair.Value.CompareTo(default) > 0).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key.ToLowerInvariant() + "=" + pair.Value));
            return index + "|" + string.Join(",", Left) + "|" + Rows(Held) + "|" + Rows(Surplus);
        }
    }

    private sealed record Junk(string Id, Dictionary<string, int> Yield);

    private sealed record ConsumePlan(Dictionary<string, long> Taken, ItemEntry[] Refund);
}
