using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
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
        _recipes ??= Index();
        var parts = _recipes.TryGetValue(baseKey, out var recipe) ? recipe : ScrapYield.Of(reader, baseKey)?.ToArray();
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

    private Dictionary<string, WorkshopMaterial[]> Index()
    {
        var index = new Dictionary<string, WorkshopMaterial[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in reader.Winners("COBJ").OrderBy(id => id, StringComparer.Ordinal))
        {
            if (RecordRows.RecipeHeader(reader, recipe) is not ({ } bench, { } key)
                || !workbenchKeywordIds.Contains(bench, StringComparer.OrdinalIgnoreCase)
                || index.ContainsKey(key)
                || RecordRows.RecipeBody(reader, recipe) is not { } cobj) continue;
            var parts = cobj.Components.Where(part => part.Count > 0).GroupBy(part => part.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => new WorkshopMaterial(group.Key, reader.Name(group.Key) ?? reader.EditorId(group.Key) ?? group.Key,
                    (int)Math.Min(group.Sum(part => (long)part.Count), int.MaxValue))).ToArray();
            if (parts.Length > 0) index[key] = parts;
        }
        return index;
    }
}
