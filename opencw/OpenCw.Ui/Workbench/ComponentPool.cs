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
    private IEnumerable<InventoryStack> AllStacks => stash is null ? items.Stacks : items.Stacks.Concat(stash.SelectMany(store => store.Stacks));

    private readonly Dictionary<string, Dictionary<string, int>?> _yields = new(StringComparer.OrdinalIgnoreCase);

    public int CountOf(string id)
    {
        long total = 0;
        foreach (var stack in AllStacks)
        {
            if (string.Equals(stack.Id, id, StringComparison.OrdinalIgnoreCase)) total += stack.Count;
            else if (YieldOf(stack.Id) is { } y && y.TryGetValue(id, out var per)) total += (long)per * stack.Count;
        }
        return (int)Math.Min(total, int.MaxValue);
    }

    public bool Consume(IReadOnlyList<WorkshopMaterial> needs)
    {
        if (Plan(needs) is not { } plan) return false;
        var stores = stash is null ? new[] { items } : new[] { items }.Concat(stash).ToArray();
        var before = stores.Select(store => store.Entries.ToArray()).ToArray();
        foreach (var (id, count) in plan.Taken)
            if (!Take(stores, id, count))
            {
                for (var i = 0; i < stores.Length; i++) stores[i].ReplaceEntries(before[i]);
                return false;
            }
        foreach (var (id, count) in plan.Refund)
            items.Add(id, count);
        return true;
    }

    private static bool Take(PlayerItems[] stores, string id, int count)
    {
        var left = count;
        foreach (var store in stores)
        {
            if (left <= 0) break;
            var part = Math.Min(left, store.CountOf(id));
            if (part <= 0) continue;
            if (!store.Remove(id, part)) return false;
            left -= part;
        }
        return left <= 0;
    }

    // Constraint: each component pays out its scrap item; a component with no scrap item pays nothing, and an unreadable mapping pays nothing and reports false.
    public bool Credit(IReadOnlyList<WorkshopMaterial> parts)
    {
        if (PlanCredit(parts) is not { } rewards) return false;
        Pay(rewards);
        return true;
    }

    public void Pay(IReadOnlyList<(string Item, int Count)> rewards)
    {
        foreach (var (item, count) in rewards) items.Add(item, count);
    }

    // Constraint: resolves every reward before anything is paid; null when any mapping could not be read, so the caller can refuse before it removes the scrapped object.
    public IReadOnlyList<(string Item, int Count)>? PlanCredit(IReadOnlyList<WorkshopMaterial> parts)
    {
        var before = reader.FailureCount;
        var rewards = new List<(string Item, int Count)>();
        foreach (var part in parts.Where(part => part.Needed > 0))
            if (RecordRows.Link(reader, part.Id, RecordRows.ComponentScrapItemPath) is { } scrap)
                rewards.Add((scrap, part.Needed));
        return reader.FailureCount == before ? rewards : null;
    }

    // Constraint: a yield read that failed is answered as no yield now but is not cached, so a recovered read is retried.
    private Dictionary<string, int>? YieldOf(string itemId)
    {
        if (_yields.TryGetValue(itemId, out var cached)) return cached;
        var before = reader.FailureCount;
        var parts = ScrapYield.Of(reader, itemId);
        var yield = parts is { Count: > 0 } ? parts.ToDictionary(part => part.Id, part => part.Needed, StringComparer.OrdinalIgnoreCase) : null;
        if (reader.FailureCount == before) _yields[itemId] = yield;
        return yield;
    }

    public bool CanConsume(IReadOnlyList<WorkshopMaterial> needs) => needs.All(need => need.Needed <= 0) || Plan(needs) is not null;

    public bool CanSpend(IReadOnlyList<WorkshopMaterial> needs) => CanConsume(needs);

    // Constraint: the greedy answer is kept when it exists; the bounded search only runs when greedy fails, so it is never worse than the greedy planner.
    private ConsumePlan? Plan(IReadOnlyList<WorkshopMaterial> needs) => PlanGreedy(needs) ?? Search(needs);

    private const int SearchBudget = 2048;

    private ConsumePlan? Search(IReadOnlyList<WorkshopMaterial> needs)
    {
        var groups = needs.Where(n => n.Needed > 0).GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (groups.Count == 0) return null;
        var ids = groups.Select(group => group.Key).ToArray();
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in AllStacks) held[stack.Id] = Clamp((long)held.GetValueOrDefault(stack.Id) + stack.Count);
        var start = new SearchState(
            held,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            groups.Select(group => Clamp(group.Sum(n => (long)n.Needed))).ToArray());
        var budget = SearchBudget;
        return Solve(start, ids, 0, ref budget) is { } done ? Finish(done.Taken, done.Surplus) : null;
    }

    private SearchState? Solve(SearchState state, string[] ids, int index, ref int budget)
    {
        if (budget-- <= 0) return null;
        for (; index < ids.Length; index++)
        {
            state.Left[index] -= Draw(state.Surplus, ids[index], state.Left[index]);
            if (state.Left[index] > 0) break;
        }
        if (index == ids.Length) return state;
        var id = ids[index];
        var left = state.Left[index];
        var direct = Math.Min(state.Held.GetValueOrDefault(id), left);
        if (direct > 0)
        {
            var next = state.Copy();
            next.Held[id] -= direct;
            next.Taken[id] = Clamp((long)next.Taken.GetValueOrDefault(id) + direct);
            next.Left[index] -= direct;
            if (Solve(next, ids, index, ref budget) is { } found) return found;
            if (budget <= 0) return null;
        }
        var candidates = state.Held.Where(pair => pair.Value > 0 && YieldOf(pair.Key) is { } y && y.ContainsKey(id))
            .Select(pair => new Junk(pair.Key, YieldOf(pair.Key)!))
            .OrderBy(candidate => candidate.Yield.Count).ThenByDescending(candidate => candidate.Yield[id])
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToList();
        foreach (var junk in candidates)
        {
            var per = (long)junk.Yield[id];
            var units = (int)Math.Min(state.Held[junk.Id], (left + per - 1) / per);
            if (units <= 0) continue;
            var next = state.Copy();
            next.Held[junk.Id] -= units;
            next.Taken[junk.Id] = Clamp((long)next.Taken.GetValueOrDefault(junk.Id) + units);
            foreach (var (component, amount) in junk.Yield)
                next.Surplus[component] = Clamp((long)next.Surplus.GetValueOrDefault(component) + (long)amount * units);
            if (Solve(next, ids, index, ref budget) is { } found) return found;
            if (budget <= 0) return null;
        }
        return null;
    }

    // Constraint: order is UNVERIFIED (the game's plan builder is unnamed); surplus first, then direct units, then junk with the fewest components, then the highest yield, then id.
    private ConsumePlan? PlanGreedy(IReadOnlyList<WorkshopMaterial> needs)
    {
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in AllStacks) held[stack.Id] = Clamp((long)held.GetValueOrDefault(stack.Id) + stack.Count);
        var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var surplus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var need in needs.Where(n => n.Needed > 0).GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
        {
            var id = need.Key;
            var remaining = Clamp(need.Sum(n => (long)n.Needed));
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
                var units = (int)Math.Min(held[chosen.Id], (remaining + per - 1) / per);
                if (units <= 0) return null;
                held[chosen.Id] -= units;
                taken[chosen.Id] = Clamp((long)taken.GetValueOrDefault(chosen.Id) + units);
                foreach (var (component, amount) in chosen.Yield)
                    surplus[component] = Clamp((long)surplus.GetValueOrDefault(component) + (long)amount * units);
                remaining -= Draw(surplus, id, remaining);
            }
        }
        return Finish(taken, surplus);
    }

    private ConsumePlan? Finish(Dictionary<string, int> taken, Dictionary<string, int> surplus)
    {
        var refund = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var before = reader.FailureCount;
        foreach (var (component, count) in surplus.Where(pair => pair.Value > 0))
            if (RecordRows.Link(reader, component, RecordRows.ComponentScrapItemPath) is { } scrap)
                refund[scrap] = Clamp((long)refund.GetValueOrDefault(scrap) + count);
        return reader.FailureCount == before ? new ConsumePlan(taken, refund) : null;
    }

    private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);

    private static int Draw(Dictionary<string, int> surplus, string id, int wanted)
    {
        var take = Math.Min(surplus.GetValueOrDefault(id), wanted);
        if (take > 0) surplus[id] -= take;
        return take;
    }

    private sealed record SearchState(Dictionary<string, int> Held, Dictionary<string, int> Taken, Dictionary<string, int> Surplus, int[] Left)
    {
        public SearchState Copy() => new(
            new Dictionary<string, int>(Held, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(Taken, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(Surplus, StringComparer.OrdinalIgnoreCase),
            (int[])Left.Clone());
    }

    private sealed record Junk(string Id, Dictionary<string, int> Yield);

    private sealed record ConsumePlan(Dictionary<string, int> Taken, Dictionary<string, int> Refund);
}
