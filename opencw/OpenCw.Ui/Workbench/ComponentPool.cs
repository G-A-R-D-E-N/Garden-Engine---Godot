using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Workbench;

// Requires: main thread; counts held items plus the components of held MISC junk, and consumption is all or nothing.
public sealed class ComponentPool(IRecordReader reader, PlayerItems items) : IItemStock
{
    private readonly Dictionary<string, Dictionary<string, int>?> _yields = new(StringComparer.OrdinalIgnoreCase);

    public int CountOf(string id)
    {
        long total = 0;
        foreach (var stack in items.Stacks)
        {
            if (string.Equals(stack.Id, id, StringComparison.OrdinalIgnoreCase)) total += stack.Count;
            else if (YieldOf(stack.Id) is { } y && y.TryGetValue(id, out var per)) total += (long)per * stack.Count;
        }
        return (int)Math.Min(total, int.MaxValue);
    }

    public bool Consume(IReadOnlyList<WorkshopMaterial> needs)
    {
        if (Plan(needs) is not { } plan) return false;
        var before = items.Entries.ToArray();
        foreach (var (id, count) in plan.Taken)
            if (!items.Remove(id, count))
            {
                items.ReplaceEntries(before);
                return false;
            }
        foreach (var (id, count) in plan.Refund)
            items.Add(id, count);
        return true;
    }

    // Constraint: each component pays out its scrap item; a component with no scrap item pays nothing.
    public void Credit(IReadOnlyList<WorkshopMaterial> parts)
    {
        foreach (var part in parts.Where(part => part.Needed > 0))
            if (RecordRows.Link(reader, part.Id, RecordRows.ComponentScrapItemPath) is { } scrap)
                items.Add(scrap, part.Needed);
    }

    private Dictionary<string, int>? YieldOf(string itemId)
    {
        if (_yields.TryGetValue(itemId, out var cached)) return cached;
        var parts = ScrapYield.Of(reader, itemId);
        return _yields[itemId] = parts is { Count: > 0 } ? parts.ToDictionary(part => part.Id, part => part.Needed, StringComparer.OrdinalIgnoreCase) : null;
    }

    // Constraint: order is UNVERIFIED (the game's plan builder is unnamed); surplus first, then direct units, then junk with the fewest components, then the highest yield, then id.
    private ConsumePlan? Plan(IReadOnlyList<WorkshopMaterial> needs)
    {
        var held = items.Stacks.ToDictionary(stack => stack.Id, stack => stack.Count, StringComparer.OrdinalIgnoreCase);
        var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var surplus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var need in needs.Where(n => n.Needed > 0).GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
        {
            var id = need.Key;
            var remaining = need.Sum(n => n.Needed);
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
                var per = chosen.Yield[id];
                var units = Math.Min(held[chosen.Id], (remaining + per - 1) / per);
                held[chosen.Id] -= units;
                taken[chosen.Id] = taken.GetValueOrDefault(chosen.Id) + units;
                foreach (var (component, amount) in chosen.Yield)
                    surplus[component] = surplus.GetValueOrDefault(component) + amount * units;
                remaining -= Draw(surplus, id, remaining);
            }
        }
        var refund = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (component, count) in surplus.Where(pair => pair.Value > 0))
            if (RecordRows.Link(reader, component, RecordRows.ComponentScrapItemPath) is { } scrap)
                refund[scrap] = refund.GetValueOrDefault(scrap) + count;
        return new ConsumePlan(taken, refund);
    }

    private static int Draw(Dictionary<string, int> surplus, string id, int wanted)
    {
        var take = Math.Min(surplus.GetValueOrDefault(id), wanted);
        if (take > 0) surplus[id] -= take;
        return take;
    }

    private sealed record Junk(string Id, Dictionary<string, int> Yield);

    private sealed record ConsumePlan(Dictionary<string, int> Taken, Dictionary<string, int> Refund);
}
