using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: main thread; a base object yields the components of the lowest-keyed recipe at a workshop workbench keyword that creates it, else the components of its MISC junk record, else it is not scrappable.
public sealed class WorkshopScrapRules(IRecordReader reader, IReadOnlyCollection<string> workbenchKeywordIds)
{
    private Dictionary<string, WorkshopMaterial[]>? _recipes;
    private readonly Dictionary<string, WorkshopMaterial[]?> _parts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkshopMaterial>? PartsOf(string baseKey)
    {
        if (_parts.TryGetValue(baseKey, out var cached)) return cached;
        var before = reader.FailureCount;
        var index = _recipes ?? Index();
        var parts = index.TryGetValue(baseKey, out var recipe) ? recipe : ScrapYield.Of(reader, baseKey)?.ToArray();
        var result = parts is { Length: > 0 } ? parts : null;
        if (reader.FailureCount != before) return result;
        _recipes = index;
        return _parts[baseKey] = result;
    }

    public WorkshopTarget? TargetFor(string targetId, string baseKey, bool blocked = false, string? recipeId = null, string? blockedReason = null, int count = 1)
    {
        var parts = recipeId is null ? null : PartsOfRecipe(recipeId);
        parts ??= PartsOf(baseKey);
        if (parts is null) return null;
        return new WorkshopTarget(targetId, reader.Name(baseKey) ?? reader.EditorId(baseKey) ?? baseKey)
        {
            Scrappable = true,
            ScrapBlocked = blocked,
            ScrapBlockedReason = blocked ? blockedReason : null,
            Yield = count > 1 ? parts.Select(part => part with { Needed = (int)Math.Min((long)part.Needed * count, int.MaxValue) }).ToArray() : parts,
        };
    }

    // Constraint: the yield of the one recipe a player-built object was made with; null when its row is unreadable or lists no components, so the caller falls back to the base yield.
    public IReadOnlyList<WorkshopMaterial>? PartsOfRecipe(string recipeId) =>
        RecordRows.RecipeBody(reader, recipeId) is { } cobj && PartsOfBody(cobj) is { Length: > 0 } parts ? parts : null;

    // Constraint: a ledger copy decides when there is one; otherwise a never-opened world container holds its seeded stacks and a never-opened built object holds nothing; an unreadable seed counts as holding items.
    public static bool HoldsItems(PlayerItems? held, bool built, Func<IReadOnlyList<InventoryStack>?> seed)
    {
        if (held is not null) return held.Stacks.Any(stack => stack.Count > 0);
        if (built) return false;
        return seed() is not { } stacks || stacks.Any(stack => stack.Count > 0);
    }

    private WorkshopMaterial[] PartsOfBody(RecipeRecord cobj) =>
        cobj.Components.Where(part => part.Count > 0).GroupBy(part => part.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkshopMaterial(group.Key, reader.Name(group.Key) ?? reader.EditorId(group.Key) ?? group.Key,
                (int)Math.Min(group.Sum(part => (long)part.Count), int.MaxValue))).ToArray();

    private Dictionary<string, WorkshopMaterial[]> Index()
    {
        var index = new Dictionary<string, WorkshopMaterial[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in reader.Winners("COBJ").OrderBy(id => id, StringComparer.Ordinal))
        {
            if (RecordRows.RecipeHeader(reader, recipe) is not ({ } bench, { } key)
                || !workbenchKeywordIds.Contains(bench, StringComparer.OrdinalIgnoreCase)
                || index.ContainsKey(key)
                || RecordRows.RecipeBody(reader, recipe) is not { } cobj) continue;
            var parts = PartsOfBody(cobj);
            if (parts.Length > 0) index[key] = parts;
        }
        return index;
    }
}
