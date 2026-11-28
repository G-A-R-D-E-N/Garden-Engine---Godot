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
        var beforeRead = reader.FailureCount;
        if (_recipes is null)
        {
            var recipes = Index();
            if (reader.FailureCount != beforeRead) return null;
            _recipes = recipes;
        }
        var parts = _recipes.TryGetValue(baseKey, out var recipe) ? recipe : ScrapYield.Of(reader, baseKey)?.ToArray();
        if (reader.FailureCount != beforeRead) return null;
        return _parts[baseKey] = parts is { Length: > 0 } ? parts : null;
    }

    public WorkshopTarget? TargetFor(string targetId, string baseKey, bool blocked = false)
    {
        if (PartsOf(baseKey) is not { } parts) return null;
        return new WorkshopTarget(targetId, reader.Name(baseKey) ?? reader.EditorId(baseKey) ?? baseKey)
        {
            Scrappable = true,
            ScrapBlocked = blocked,
            Yield = parts,
        };
    }

    public static bool HoldsItems(PlayerItems? held, bool built, Func<IReadOnlyList<InventoryStack>?> seed)
    {
        if (held is not null) return held.Entries.Any(entry => entry.Count > 0);
        return !built && (seed() is not { } stacks || stacks.Any(stack => stack.Count > 0));
    }

    private Dictionary<string, WorkshopMaterial[]> Index()
    {
        var index = new Dictionary<string, WorkshopMaterial[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in reader.Winners("COBJ").OrderBy(id => id, StringComparer.Ordinal))
        {
            if (RecordRows.RecipeHeader(reader, recipe) is not ({ } bench, { } key)
                || !workbenchKeywordIds.Contains(bench, StringComparer.OrdinalIgnoreCase)
                || index.ContainsKey(key)
                || RecordRows.RecipeBody(reader, recipe) is not { } cobj) continue;
            var parts = WorkshopMaterial.FromParts(reader, cobj.Components.Where(part => part.Count > 0), out var oversized);
            if (parts.Length > 0) index[key] = oversized ? [] : parts;
        }
        return index;
    }
}
