using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Workbench;

public sealed class RecordBenchRecipes : IWorkbenchRecipes
{
    public const string TargetId = "bench";
    public const string ModSignature = "OMOD";
    public const string UnreadableText = "Item records could not be read.";
    public const string NoRoomText = "No room for the result.";
    public const string LackingText = "Not enough materials.";
    public const string NothingToCraftText = "Nothing you can craft here.";
    public const string ModText = "Needs an item to modify.";
    public const string NotApplicableText = "This mod does not fit that item.";
    public const string ItemTargetPrefix = "item:";
    public const string DetachSlot = "Remove mods";
    public const string DetachPrefix = "detach:";
    private const string LooseModPath = "OMOD\\LooseMod";

    private static readonly Dictionary<string, string> ItemFormTypes = new(StringComparer.Ordinal)
    {
        ["WEAP"] = ItemRecordTypes.FormType("WEAP"),
        ["ARMO"] = ItemRecordTypes.FormType("ARMO"),
    };

    private readonly IRecordReader _reader;
    private readonly PlayerItems _items;
    private readonly ComponentPool _pool;
    private readonly IRecipeFacts? _facts;
    private readonly ObjectModRules _rules;
    private readonly string? _modSignature;
    private readonly string? _pieceKeyword;
    private readonly Dictionary<string, bool> _pieceOk = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Recipe> _recipes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _attachPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _modSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>?> _itemKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _targetKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Category> _categories = [];
    private int _stamp;

    public RecordBenchRecipes(IRecordReader reader, PlayerItems items, IReadOnlyCollection<string> benchKeywords, string title, IRecipeFacts? facts = null, int? benchType = null, string? pieceKeyword = null, ObjectModRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(benchKeywords);
        _reader = reader;
        _items = items;
        _pool = new ComponentPool(reader, items);
        _facts = facts;
        _rules = rules ?? ObjectModRules.Current;
        _modSignature = BenchKinds.ItemSignature(benchType);
        _pieceKeyword = pieceKeyword;
        Title = title;
        Build(benchKeywords);
    }

    public string Title { get; }

    public string ApplyVerb => "Craft";

    public string Stamp => _stamp.ToString();

    public IReadOnlyList<WorkbenchTarget> Targets
    {
        get
        {
            var targets = new List<WorkbenchTarget>();
            if (_modSignature is null || _recipes.Values.Any(r => !r.IsMod)) targets.Add(new WorkbenchTarget(TargetId, Title));
            var anyMod = _recipes.Values.Any(r => r.IsMod);
            foreach (var entry in _items.Entries.Where(e => (anyMod || e.State.Mods.Count > 0) && IsModTarget(e.Id)))
                targets.Add(new WorkbenchTarget(TargetOf(entry), ItemName(entry)));
            return targets;
        }
    }

    public event Action<IReadOnlyList<string>>? Crafted;

    public int RecipeCount => _recipes.Count;

    public int HiddenCount { get; private set; }

    public string? Notice => _recipes.Count == 0 && HiddenCount > 0 ? NothingToCraftText : null;

    public IReadOnlyList<WorkshopMaterial>? Scrap(string itemId) => ScrapYield.Of(_reader, itemId);

    public IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId) =>
        CategoriesOf(targetId).Select(c => new WorkbenchSlot(c.Name, c.Name)).Concat(
            ItemAt(targetId) is { State.Mods.Count: > 0 } ? [new WorkbenchSlot(DetachSlot, DetachSlot)] : []).ToArray();

    public IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId) =>
        slotId == DetachSlot ? (ItemAt(targetId)?.State.Mods ?? []).Select(mod => new WorkbenchOption(DetachPrefix + mod, ModName(mod))).ToArray()
        : CategoriesOf(targetId).FirstOrDefault(c => c.Name == slotId) is { } category
            ? category.Recipes.Select(r => new WorkbenchOption(r.Id, r.Name) { Description = r.Description }).ToArray()
            : [];

    public WorkbenchOptionState StateOf(string targetId, string slotId, string optionId) =>
        TryDetach(optionId, out var detachMod) ? (ItemAt(targetId) is { } held && held.State.Has(detachMod) ? WorkbenchOptionState.Available : WorkbenchOptionState.Unavailable)
        : !_recipes.TryGetValue(optionId, out var recipe) || (recipe.IsMod && !Applies(targetId, recipe)) ? WorkbenchOptionState.Unavailable
        : !_pool.CanConsume(recipe.Materials) ? WorkbenchOptionState.LackingMaterials
        : WorkbenchOptionState.Available;

    public IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId) =>
        TryDetach(optionId, out _) ? [] : _recipes.TryGetValue(optionId, out var recipe) ? recipe.Materials : [];

    public int AvailableCount(string materialId) => _pool.CountOf(materialId);

    public ActionResult Craft(string targetId, string slotId, string optionId)
    {
        if (TryDetach(optionId, out var detachMod)) return Detach(targetId, detachMod);
        if (!_recipes.TryGetValue(optionId, out var recipe)) return new ActionResult(false);
        var item = recipe.IsMod ? ItemAt(targetId) : null;
        if (recipe.IsMod && item is null) return new ActionResult(false, ModText);
        var beforeRead = _reader.FailureCount;
        if (recipe.IsMod && !Applies(targetId, recipe)) return new ActionResult(false, NotApplicableText);
        if (item is not null && recipe.Materials.Any(m => string.Equals(m.Id, item.Id, StringComparison.OrdinalIgnoreCase))) return new ActionResult(false, NotApplicableText);
        if (recipe.IsMod)
        {
            var replaced = ModsAt(item!.State, recipe.Attach);
            var modded = replaced.Aggregate(item.State, (s, m) => s.Without(m)).With(recipe.Created);
            var returns = replaced.Select(LooseReturn).OfType<string>().Select(id => (id, 1)).ToArray();
            if (_reader.FailureCount != beforeRead) return new ActionResult(false, UnreadableText);
            var result = Transform(item, modded, recipe.Materials, returns);
            if (!result.Ok) return result;
        }
        else if (!_pool.Consume(recipe.Materials, [(recipe.Created, recipe.CreatedCount)]))
            return new ActionResult(false, _pool.CanConsume(recipe.Materials) ? NoRoomText : LackingText);
        _stamp++;
        Crafted?.Invoke(recipe.Materials.Select(m => m.Id).ToArray());
        return new ActionResult(true);
    }

    private string[] ModsAt(ItemState state, string? point) =>
        point is null ? [] : state.Mods.Where(m => string.Equals(AttachPointOf(m), point, StringComparison.OrdinalIgnoreCase)).ToArray();

    private string? LooseReturn(string mod) =>
        RecordRows.Format(_reader.Scalar(mod, LooseModPath)) is { } loose && _reader.Signature(loose) is not null ? loose : null;

    private ActionResult Transform(ItemEntry item, ItemState modded, IReadOnlyList<WorkshopMaterial> materials, IReadOnlyList<(string Item, int Count)> returns)
    {
        var restore = _pool.Checkpoint();
        if (!_pool.Consume(materials, returns))
            return new ActionResult(false, _pool.CanConsume(materials) ? NoRoomText : LackingText);
        if (!item.State.Equals(modded) && !_items.CanAdd(item.Id, modded, 1))
        {
            restore();
            return new ActionResult(false, NoRoomText);
        }
        if (_items.Transform(item.Id, item.State, modded)) return new ActionResult(true);
        restore();
        return new ActionResult(false, NotApplicableText);
    }

    private ActionResult Detach(string targetId, string mod)
    {
        if (ItemAt(targetId) is not { } item) return new ActionResult(false, ModText);
        if (!item.State.Has(mod)) return new ActionResult(false, NotApplicableText);
        var beforeRead = _reader.FailureCount;
        var loose = LooseReturn(mod);
        if (_reader.FailureCount != beforeRead) return new ActionResult(false, UnreadableText);
        var result = Transform(item, item.State.Without(mod), [], loose is null ? [] : [(loose, 1)]);
        if (!result.Ok) return result;
        _stamp++;
        return new ActionResult(true);
    }

    private static bool TryDetach(string optionId, out string mod)
    {
        var isDetach = optionId.StartsWith(DetachPrefix, StringComparison.Ordinal);
        mod = isDetach ? optionId[DetachPrefix.Length..] : string.Empty;
        return isDetach;
    }

    private string ModName(string mod) => _reader.Name(mod) ?? _reader.EditorId(mod) ?? mod;

    private IEnumerable<Category> CategoriesOf(string targetId)
    {
        if (targetId == TargetId) return _categories.Select(c => c with { Recipes = c.Recipes.Where(r => !r.IsMod).ToArray() }).Where(c => c.Recipes.Count > 0);
        if (ItemAt(targetId) is null) return [];
        return _categories.Select(c => c with { Recipes = c.Recipes.Where(r => r.IsMod && Applies(targetId, r)).ToArray() }).Where(c => c.Recipes.Count > 0);
    }

    private static string TargetOf(ItemEntry e) =>
        string.Create(CultureInfo.InvariantCulture, $"{ItemTargetPrefix}{e.Id}#{string.Join(',', e.State.Mods)}#{e.State.Condition:R}#{e.State.Rank}");

    private ItemEntry? ItemAt(string targetId) =>
        targetId.StartsWith(ItemTargetPrefix, StringComparison.Ordinal)
        && _items.Entries.FirstOrDefault(e => TargetOf(e) == targetId) is { } entry
        && IsModTarget(entry.Id)
            ? entry : null;

    private bool IsModTarget(string id) =>
        _reader.Signature(id) is { } signature && ItemFormTypes.ContainsKey(signature) && (_modSignature is null || signature == _modSignature) && IsPiece(id);

    // Constraint: a non-null piece keyword restricts mod targets to items whose KWDA holds it; "" matches nothing.
    private bool IsPiece(string id)
    {
        if (_pieceKeyword is null) return true;
        return Cached(_pieceOk, id, () => _reader.FormLinks(id, "KWDA").Contains(_pieceKeyword, StringComparer.OrdinalIgnoreCase));
    }

    private T Cached<T>(Dictionary<string, T> cache, string key, Func<T> read)
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        var before = _reader.FailureCount;
        var value = read();
        if (_reader.FailureCount == before) cache[key] = value;
        return value;
    }

    // Constraint: the OMOD form type is checked, its attach point must be an APPR slot of the item or a slot offered by an attached mod, and (ObjectModRules.MatchTargetKeywords) one MNAM keyword must be an item keyword; the keyword rule is UNVERIFIED.
    private bool Applies(string targetId, Recipe mod) =>
        ItemAt(targetId) is { } item
        && !item.State.Has(mod.Created)
        && ItemFormTypes.TryGetValue(_reader.Signature(item.Id) ?? string.Empty, out var formType)
        && _reader.Scalar(mod.Created, "OMOD\\FormType") == formType
        && (mod.Attach is not { } point || Reachable(item, point))
        && (!_rules.MatchTargetKeywords || TargetKeywordsFit(item.Id, mod.Created));

    // Invariant: a mod with no readable target keywords, or an item whose keyword row is unreadable, is never hidden.
    private bool TargetKeywordsFit(string itemId, string mod)
    {
        var targets = Cached(_targetKeywords, mod, () => new HashSet<string>(RecordRows.ModTargetKeywords(_reader, mod) ?? [], StringComparer.OrdinalIgnoreCase));
        if (targets.Count == 0) return true;
        var keywords = Cached(_itemKeywords, itemId, () => RecordRows.ItemKeywords(_reader, itemId) is { } read ? new HashSet<string>(read, StringComparer.OrdinalIgnoreCase) : null);
        return keywords is null || targets.Overlaps(keywords);
    }

    // Invariant: an attached mod on the candidate's own attach point is the one a craft replaces, so the slots it offers do not count.
    private bool Reachable(ItemEntry item, string point) =>
        AttachPointsOf(item.Id).Contains(point)
        || item.State.Mods.Any(m => !string.Equals(AttachPointOf(m), point, StringComparison.OrdinalIgnoreCase) && ModSlotsOf(m).Contains(point));

    private HashSet<string> ModSlotsOf(string mod) =>
        Cached(_modSlots, mod, () => new HashSet<string>(RecordRows.ModAttachParentSlots(_reader, mod), StringComparer.OrdinalIgnoreCase));

    private HashSet<string> AttachPointsOf(string itemId) =>
        Cached(_attachPoints, itemId, () => new HashSet<string>(RecordRows.AttachParentSlots(_reader, itemId), StringComparer.OrdinalIgnoreCase));

    private string? AttachPointOf(string mod) =>
        RecordKey.TryParse(_reader.Scalar(mod, "OMOD\\AttachPoint"), out var plugin, out var local) && local != 0 ? RecordKey.Format(plugin, local) : null;

    private bool IsBenchMod(string created) =>
        _modSignature is not null && _reader.Signature(created) == ModSignature
        && _reader.Scalar(created, "OMOD\\FormType") == ItemFormTypes[_modSignature];

    private string ItemName(ItemEntry item) =>
        (_reader.Name(item.Id) ?? _reader.EditorId(item.Id) ?? item.Id) + (item.State.Mods.Count > 0 ? $" (+{item.State.Mods.Count})" : string.Empty)
        + (item.Count > 1 ? $" x{item.Count}" : string.Empty);

    private void Build(IReadOnlyCollection<string> benchKeywords)
    {
        var byCategory = new SortedDictionary<string, List<Recipe>>(StringComparer.Ordinal);
        foreach (var id in _reader.Winners("COBJ"))
        {
            var beforeRead = _reader.FailureCount;
            if (RecordRows.RecipeHeader(_reader, id) is not { } header)
                continue;
            var (bench, created) = header;
            var onBench = bench is not null && benchKeywords.Contains(bench, StringComparer.OrdinalIgnoreCase);
            if (!onBench && !(bench is null && IsBenchMod(created)))
                continue;
            if (RecordRows.RecipeBody(_reader, id) is not { } cobj)
                continue;
            if (!RecipeConditionGate.Passes(cobj.Conditions, _facts))
            {
                HiddenCount++;
                continue;
            }
            var materials = WorkshopMaterial.FromParts(_reader, cobj.Components, out var oversized);
            if (oversized) continue;
            var isMod = _reader.Signature(created) == ModSignature;
            var attach = isMod && _modSignature is not null ? AttachPointOf(created) : null;
            if (beforeRead != _reader.FailureCount) continue;
            var recipe = new Recipe(id, _reader.Name(created) ?? _reader.EditorId(created) ?? _reader.EditorId(id) ?? id, created)
            {
                Description = cobj.Description,
                CreatedCount = cobj.CreatedCount,
                IsMod = isMod,
                Attach = attach,
                Materials = materials,
            };
            var category = cobj.Categories
                .Select(keyword => RecordWorkshopCatalog.CategoryPath(_reader.EditorId(keyword)))
                .OrderByDescending(path => path.Count).FirstOrDefault() ?? [RecordWorkshopCatalog.OtherCategory];
            var name = attach is { } slot ? _reader.Name(slot) ?? _reader.EditorId(slot) ?? slot : string.Join(" > ", category);
            if (!byCategory.TryGetValue(name, out var list)) byCategory[name] = list = [];
            list.Add(recipe);
            _recipes[id] = recipe;
        }
        foreach (var (name, list) in byCategory)
            _categories.Add(new Category(name, list.OrderBy(r => r.Name, StringComparer.Ordinal).ToArray()));
    }

    private sealed record Recipe(string Id, string Name, string Created)
    {
        public string Description { get; init; } = string.Empty;

        public int CreatedCount { get; init; } = 1;

        public bool IsMod { get; init; }

        public string? Attach { get; init; }

        public IReadOnlyList<WorkshopMaterial> Materials { get; init; } = [];
    }

    private sealed record Category(string Name, IReadOnlyList<Recipe> Recipes);
}
