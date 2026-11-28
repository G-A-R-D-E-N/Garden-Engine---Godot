using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: category names and nesting come from keyword editor ids, which are not record data.
public sealed partial class RecordWorkshopCatalog : IWorkshopCatalog
{
    public const string CategoryPrefix = "WorkshopRecipeFilter";
    public const string OtherCategory = "Other";
    public const int MaxBuildable = 999;

    private readonly HashSet<string> _oversized = new(StringComparer.Ordinal);
    private readonly IItemStock _stock;
    private readonly Dictionary<string, WorkshopItem> _items = new(StringComparer.Ordinal);

    private static readonly ushort? HasPerkFunction = Radicle.Interop.ConditionFunctions.Id("HasPerk");

    private readonly IRecipeFacts? _facts;

    public RecordWorkshopCatalog(IRecordReader reader, IItemStock stock, IReadOnlyCollection<string> workbenchKeywordIds, bool freeBuild = false, IRecipeFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(workbenchKeywordIds);
        _stock = stock;
        _facts = facts;
        FreeBuild = freeBuild;
        Roots = Build(reader, workbenchKeywordIds);
    }

    public IReadOnlyList<WorkshopNode> Roots { get; }

    public bool FreeBuild { get; }

    public int AvailableCount(string materialId) => _stock.CountOf(materialId);

    public int BuildableCount(string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item) || _oversized.Contains(itemId))
            return 0;
        if (_stock is ComponentPool pool) return pool.CountPlacements(item.Materials, MaxBuildable);
        var count = item.Materials.Where(material => material.Needed > 0)
            .Select(material => _stock.CountOf(material.Id) / material.Needed).DefaultIfEmpty(MaxBuildable).Min();
        return Math.Clamp(count, 0, MaxBuildable);
    }

    public WorkshopItemStatus StatusOf(string itemId) =>
        !_items.TryGetValue(itemId, out var item) ? WorkshopItemStatus.Available
        : item.Perks.Any(perk => !perk.Met) ? WorkshopItemStatus.PerkRequired
        : !FreeBuild && (_oversized.Contains(itemId) || (_stock is ComponentPool pool ? !pool.CanConsume(item.Materials) : item.Materials.Any(material => _stock.CountOf(material.Id) < material.Needed))) ? WorkshopItemStatus.LackingMaterials
        : WorkshopItemStatus.Available;

    public static IReadOnlyList<string> CategoryPath(string? keywordEditorId)
    {
        if (string.IsNullOrWhiteSpace(keywordEditorId))
            return [OtherCategory];
        var name = keywordEditorId.StartsWith(CategoryPrefix, StringComparison.Ordinal)
            ? keywordEditorId[CategoryPrefix.Length..]
            : keywordEditorId;
        var parts = LevelBreak().Split(name).Where(part => part.Length > 0).Select(Words).ToArray();
        return parts.Length == 0 ? [OtherCategory] : parts;
    }

    private IReadOnlyList<WorkshopNode> Build(IRecordReader reader, IReadOnlyCollection<string> workbenchKeywordIds)
    {
        var root = new Branch(string.Empty, string.Empty);
        foreach (var recipe in reader.Winners("COBJ"))
        {
            if (RecordRows.RecipeHeader(reader, recipe) is not ({ } bench, { } created)
                || !workbenchKeywordIds.Contains(bench, StringComparer.OrdinalIgnoreCase)
                || RecordRows.RecipeBody(reader, recipe) is not { } cobj)
                continue;
            if (Perks(reader, cobj) is not { } perks)
                continue;
            var materials = WorkshopMaterial.FromParts(reader, cobj.Components, out var oversized);
            if (oversized) _oversized.Add(recipe);
            var item = new WorkshopItem(recipe, reader.Name(created) ?? reader.EditorId(created) ?? reader.EditorId(recipe) ?? recipe)
            {
                Description = cobj.Description,
                Materials = materials,
                Perks = perks,
            };
            _items[recipe] = item;
            var path = cobj.Categories.Select(keyword => CategoryPath(reader.EditorId(keyword)))
                .OrderByDescending(candidate => candidate.Count).FirstOrDefault() ?? [OtherCategory];
            var branch = root;
            foreach (var part in path)
                branch = branch.Child(part);
            branch.Leaves.Add(WorkshopNode.Leaf(item));
        }
        return root.Children.Values.OrderBy(child => child.Name, StringComparer.Ordinal).Select(child => child.ToNode()).ToArray();
    }

    // Constraint: with facts, a recipe passing its conditions lists plain; one that would pass if every HasPerk held lists with those perks unmet; only a definite False hides it (unknown results list it). Rank is not in the condition row, so it shows as 1 (UNVERIFIED).
    private IReadOnlyList<WorkshopPerk>? Perks(IRecordReader reader, RecipeRecord cobj)
    {
        if (_facts is null || RecipeConditionGate.Passes(cobj.Conditions, _facts))
            return [];
        if (RecipeConditionGate.Evaluate(cobj.Conditions, new PerksAssumed(_facts)) == RuleVerdict.False)
            return null;
        return (cobj.Conditions ?? []).Where(c => c.Row.Function == HasPerkFunction && c.Param1 is not null)
            .Select(c => c.Param1!.Value).DistinctBy(RecordKey.Format)
            .Select(perk => new WorkshopPerk(reader.Name(RecordKey.Format(perk)) ?? reader.EditorId(RecordKey.Format(perk)) ?? RecordKey.Format(perk), 1, _facts.HasPerk(perk) == true)).ToArray();
    }

    private sealed class PerksAssumed(IRecipeFacts inner) : IRecipeFacts
    {
        public double? Level => inner.Level;

        public bool? HasPerk(FormKey perk) => true;

        public bool? HasKeyword(FormKey keyword) => inner.HasKeyword(keyword);

        public double? Global(FormKey global) => inner.Global(global);

        public int ItemCount(FormKey item) => inner.ItemCount(item);
    }

    private static string Words(string part) => CamelBreak().Replace(part, " ");

    [GeneratedRegex(@"\d{2}")]
    private static partial Regex LevelBreak();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBreak();

    private sealed class Branch(string path, string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, Branch> Children { get; } = new(StringComparer.Ordinal);

        public List<WorkshopNode> Leaves { get; } = [];

        public Branch Child(string childName)
        {
            if (!Children.TryGetValue(childName, out var child))
                Children[childName] = child = new Branch(path + "/" + childName, childName);
            return child;
        }

        public WorkshopNode ToNode() => new(path, Name)
        {
            Children = Children.Values.OrderBy(child => child.Name, StringComparer.Ordinal).Select(child => child.ToNode())
                .Concat(Leaves.OrderBy(leaf => leaf.Name, StringComparer.Ordinal)).ToArray(),
        };
    }
}
