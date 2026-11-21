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

    private readonly IItemStock _stock;
    private readonly IRecordReader _reader;
    private readonly Dictionary<string, WorkshopItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<RecordCondition>?> _conditions = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<WorkshopNode> _all;

    private static readonly ushort? HasPerkFunction = Radicle.Interop.ConditionFunctions.Id("HasPerk");

    private readonly IRecipeFacts? _facts;

    public RecordWorkshopCatalog(IRecordReader reader, IItemStock stock, IReadOnlyCollection<string> workbenchKeywordIds, bool freeBuild = false, IRecipeFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(workbenchKeywordIds);
        _stock = stock;
        _reader = reader;
        _facts = facts;
        FreeBuild = freeBuild;
        _all = Build(reader, workbenchKeywordIds);
    }

    public IReadOnlyList<WorkshopNode> Roots => Prune(_all);

    public bool FreeBuild { get; }

    public int AvailableCount(string materialId) => _stock.CountOf(materialId);

    public int BuildableCount(string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item))
            return 0;
        var upper = Math.Clamp(item.Materials.Where(material => material.Needed > 0)
            .Select(material => _stock.CountOf(material.Id) / material.Needed).DefaultIfEmpty(MaxBuildable).Min(), 0, MaxBuildable);
        var low = 0;
        while (low < upper)
        {
            var mid = low + (upper - low + 1) / 2;
            if (_stock.CanSpend(item.Materials.Select(material => material with { Needed = (int)Math.Min((long)material.Needed * mid, int.MaxValue) }).ToArray())) low = mid;
            else upper = mid - 1;
        }
        return low;
    }

    public IReadOnlyList<WorkshopPerk> PerksOf(WorkshopItem item) =>
        _conditions.TryGetValue(item.Id, out var conditions) ? Perks(_reader, conditions) ?? [] : item.Perks;

    public WorkshopItemStatus StatusOf(string itemId) =>
        !_items.TryGetValue(itemId, out var item) ? WorkshopItemStatus.Available
        : !Listed(itemId) ? WorkshopItemStatus.Unavailable
        : PerksOf(item).Count > 0 && Verdict(itemId) == RuleVerdict.False ? WorkshopItemStatus.PerkRequired
        : Verdict(itemId) == RuleVerdict.Unevaluable ? WorkshopItemStatus.ConditionsUnknown
        : !FreeBuild && !_stock.CanSpend(item.Materials) ? WorkshopItemStatus.LackingMaterials
        : WorkshopItemStatus.Available;

    // Constraint: conditions are re-evaluated against the live facts on every call; a recipe lists when they pass, or would pass if every HasPerk held, or there are no facts.
    private bool Listed(string itemId) =>
        _facts is null || !_conditions.TryGetValue(itemId, out var conditions) || Meets(itemId)
        || RecipeConditionGate.Evaluate(conditions, new PerksAssumed(_facts)) != RuleVerdict.False;

    private RuleVerdict Verdict(string itemId) =>
        _facts is null || !_conditions.TryGetValue(itemId, out var conditions) ? RuleVerdict.True : RecipeConditionGate.Evaluate(conditions, _facts);

    private bool Meets(string itemId) =>
        _facts is null || !_conditions.TryGetValue(itemId, out var conditions) || RecipeConditionGate.Passes(conditions, _facts);

    private IReadOnlyList<WorkshopNode> Prune(IReadOnlyList<WorkshopNode> nodes)
    {
        var kept = new List<WorkshopNode>();
        foreach (var node in nodes)
        {
            if (node.Item is { } item)
            {
                if (Listed(item.Id)) kept.Add(node);
                continue;
            }
            var children = Prune(node.Children);
            if (children.Count > 0) kept.Add(node with { Children = children });
        }
        return kept;
    }

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
            var perks = Perks(reader, cobj.Conditions) ?? [];
            _conditions[recipe] = cobj.Conditions;
            var item = new WorkshopItem(recipe, reader.Name(created) ?? reader.EditorId(created) ?? reader.EditorId(recipe) ?? recipe)
            {
                Description = cobj.Description,
                Materials = cobj.Components.GroupBy(part => part.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => Material(reader, new ComponentPart(group.Key, (uint)Math.Min(group.Sum(part => (long)part.Count), uint.MaxValue)))).ToArray(),
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
    private IReadOnlyList<WorkshopPerk>? Perks(IRecordReader reader, IReadOnlyList<RecordCondition>? conditions)
    {
        if (_facts is null || RecipeConditionGate.Passes(conditions, _facts))
            return [];
        if (RecipeConditionGate.Evaluate(conditions, new PerksAssumed(_facts)) == RuleVerdict.False)
            return null;
        return (conditions ?? []).Where(c => c.Row.Function == HasPerkFunction && c.Param1 is not null)
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

    private static WorkshopMaterial Material(IRecordReader reader, ComponentPart part) =>
        new(part.Id, reader.Name(part.Id) ?? reader.EditorId(part.Id) ?? part.Id, (int)Math.Min(part.Count, int.MaxValue));

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
