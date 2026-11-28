using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using FormKey = Radicle.Interop.FormKey;

namespace OpenCw.Ui.Tests;

sealed class FakeRecord
{
    public string Signature { get; init; } = string.Empty;

    public string? EditorId { get; init; }

    public string? Name { get; init; }

    public Dictionary<string, string> Scalars { get; } = new();

    public Dictionary<string, string[]> Links { get; } = new();

    public Dictionary<string, string[]> Lists { get; } = new();

    public Dictionary<uint, string> RawForms { get; } = new();

    public RecipeRecord? Recipe { get; set; }

    public int? Conditions { get; set; } = 0;

    public FakeRecord Parts(params (string Id, uint Count)[] parts)
    {
        Lists[ScrapYield.ObjectPath] = parts.Select(part => part.Id).ToArray();
        Lists[ScrapYield.CountPath] = parts.Select(part => part.Count.ToString()).ToArray();
        return this;
    }

    public LockData? Lock { get; set; }

    public List<AliasDisplay> Aliases { get; } = new();

    public List<string> MapMarkers { get; } = new();
}

sealed class FakeRecordReader : IRecordReader
{
    public Dictionary<string, FakeRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SignatureCalls { get; private set; }

    public long FailureCount { get; private set; }

    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    private bool Fails(string id, string? path = null)
    {
        if (!Failing.Contains(id) && (path is null || !Failing.Contains(id + "|" + path))) return false;
        FailureCount++;
        return true;
    }

    public static FormKey Key(string id)
    {
        RecordKey.TryParse(id, out var plugin, out var local);
        return new FormKey(plugin, local);
    }

    public FakeRecord Add(string id, string signature, string? editorId = null, string? name = null)
    {
        var record = new FakeRecord { Signature = signature, EditorId = editorId, Name = name };
        Records[id] = record;
        return record;
    }

    public FakeRecord AddRecipe(string id, string created, string bench, string[] categories, params (string Part, uint Count)[] parts)
    {
        var record = Add(id, "COBJ", "co_" + id);
        record.Recipe = new RecipeRecord(bench, created, categories, parts.Select(part => new ComponentPart(part.Part, part.Count)).ToArray(), 1, [], string.Empty);
        return record;
    }

    public string? Signature(string id)
    {
        SignatureCalls++;
        return Fails(id) ? null : Records.GetValueOrDefault(id)?.Signature;
    }

    public string? EditorId(string id) => Records.GetValueOrDefault(id)?.EditorId;

    public string? Name(string id) => Records.GetValueOrDefault(id)?.Name;

    public string? Scalar(string id, string path) =>
        !Fails(id, path) && Records.GetValueOrDefault(id) is { } record ? record.Scalars.GetValueOrDefault(path) ?? RecipeScalar(record.Recipe, path) : null;

    static string? RecipeScalar(RecipeRecord? recipe, string path) => path switch
    {
        RecordRows.RecipeBenchPath => recipe?.Bench,
        RecordRows.RecipeCreatedPath => recipe?.Created,
        RecordRows.RecipeCountPath => recipe?.CreatedCount.ToString(),
        RecordRows.RecipeDescriptionPath => recipe?.Description,
        _ => null,
    };

    static string[]? RecipeList(RecipeRecord? recipe, string path) => recipe is null ? null : path switch
    {
        RecordRows.RecipeCategoryPath => recipe.Categories.ToArray(),
        RecordRows.RecipeComponentObjectPath => recipe.Components.Select(part => part.Id).ToArray(),
        RecordRows.RecipeComponentCountPath => recipe.Components.Select(part => part.Count.ToString()).ToArray(),
        _ => null,
    };

    public string? Text(string id, string path) => Scalar(id, path);

    public IReadOnlyList<string>? List(string id, string path) =>
        !Fails(id, path) && Records.GetValueOrDefault(id) is { } record ? record.Lists.GetValueOrDefault(path) ?? RecipeList(record.Recipe, path) : null;

    public string? ResolveFormId(string id, uint rawFormId) =>
        Records.GetValueOrDefault(id)?.RawForms.GetValueOrDefault(rawFormId);

    public IReadOnlyList<string> FormLinks(string id, string subrecord) =>
        Fails(id, subrecord) ? [] : Records.GetValueOrDefault(id)?.Links.GetValueOrDefault(subrecord) ?? [];

    public IReadOnlyList<RecordCondition>? RecipeConditions(string id) => Records.GetValueOrDefault(id)?.Recipe?.Conditions;

    public int? ConditionCount(string id) => Records.GetValueOrDefault(id)?.Conditions;

    public LockData? Lock(string id) => Records.GetValueOrDefault(id)?.Lock;

    public IReadOnlyList<AliasDisplay> AliasDisplays(string questId) =>
        Records.GetValueOrDefault(questId)?.Aliases ?? [];

    public IReadOnlyList<string> WorldspaceMapMarkers(string worldspaceId) =>
        Records.GetValueOrDefault(worldspaceId)?.MapMarkers ?? [];

    public IReadOnlyList<string> Winners(string signature) =>
        Records.Where(pair => pair.Value.Signature == signature).Select(pair => pair.Key).ToArray();

    public string? FindEditorId(string editorId, string? signature = null) =>
        Records.FirstOrDefault(pair => pair.Value.EditorId == editorId && (signature is null || pair.Value.Signature == signature)).Key;
}

sealed class FakeStock : IItemStock
{
    public Dictionary<string, int> Counts { get; } = new();

    public int CountOf(string itemId) => Counts.GetValueOrDefault(itemId);
}
