using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class ContainerContents
{
    public static ulong SeedOf(string refKey)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in refKey)
            hash = (hash ^ char.ToUpperInvariant(c)) * 1099511628211UL;
        return hash;
    }

    // Constraint: pick receives the list id, the entry count and the reference seed, and returns the stacks that list rolls to.
    public static IReadOnlyList<InventoryStack> Expand(
        IEnumerable<InventoryStack> items, string refKey, Func<string, bool> isLeveled,
        Func<string, int, ulong, IReadOnlyList<InventoryStack>> pick)
    {
        var seed = SeedOf(refKey);
        var order = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(InventoryStack stack)
        {
            if (stack.Count <= 0 || string.IsNullOrWhiteSpace(stack.Id)) return;
            if (!totals.TryAdd(stack.Id, stack.Count)) totals[stack.Id] = (int)Math.Min((long)totals[stack.Id] + stack.Count, int.MaxValue);
            else order.Add(stack.Id);
        }
        foreach (var item in items)
        {
            if (item.Count <= 0) continue;
            if (!isLeveled(item.Id)) Add(item);
            else foreach (var rolled in pick(item.Id, item.Count, seed)) Add(rolled);
        }
        return order.ConvertAll(id => new InventoryStack(id, totals[id]));
    }

    public const int LeveledPickLimit = 32767;

    // Constraint: LeveledPickCount takes at most LeveledPickLimit per call, so a larger entry count is rolled in chunks with the seed offset by the chunk index; the first chunk keeps the plain seed.
    public static IReadOnlyList<InventoryStack> PickChunked(int count, ulong seed, Func<int, ulong, IReadOnlyList<InventoryStack>> pick)
    {
        var all = new List<InventoryStack>();
        var chunk = 0UL;
        for (var left = Math.Max(count, 1); left > 0; left -= LeveledPickLimit, chunk++)
            all.AddRange(pick(Math.Min(left, LeveledPickLimit), seed + chunk));
        return all;
    }

    // Constraint: each stack is the entry's LVLO row count times the pick multiplier, clamped to int; a row of zero or less is dropped.
    public static IReadOnlyList<InventoryStack> Stacks(IReadOnlyList<(string Key, int Row)> entries, uint multiplier)
    {
        var stacks = new List<InventoryStack>(entries.Count);
        foreach (var (key, row) in entries)
            if (row > 0) stacks.Add(new InventoryStack(key, (int)Math.Min((long)row * multiplier, int.MaxValue)));
        return stacks;
    }
}
