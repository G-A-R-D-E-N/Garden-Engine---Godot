using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Records;

public readonly record struct ComponentPart(string Id, uint Count);

public static class RecordRows
{
    public const string RecipeBenchPath = "COBJ\\WorkbenchKeyword";
    public const string RecipeCreatedPath = "COBJ\\CreatedObject";
    public const string RecipeCountPath = "COBJ\\CreatedObjectCount";
    public const string RecipeCategoryPath = "COBJ\\Category";
    public const string RecipeComponentObjectPath = "COBJ\\ComponentObject";
    public const string RecipeComponentCountPath = "COBJ\\ComponentCount";
    public const string RecipeDescriptionPath = "COBJ\\Description";
    public const string ComponentScrapItemPath = "CMPO\\ScrapItem";

    // Invariant: a link is null when the row is absent, empty, unparsable or has local id 0.
    public static string? Link(IRecordReader reader, string id, string path) =>
        Format(reader.Scalar(id, path));

    public static string? Format(string? text) =>
        RecordKey.TryParse(text, out var plugin, out var local) && local != 0 ? RecordKey.Format(plugin, local) : null;

    // Requires: the object and count rows are one counted array, so equal lengths; an empty or unparsable link or count makes the list unreadable (null), never a cheaper or smaller one.
    public static List<ComponentPart>? Components(IRecordReader reader, string id, string objectPath, string countPath)
    {
        if (reader.List(id, objectPath) is not { } objects || reader.List(id, countPath) is not { } counts || objects.Count != counts.Count) return null;
        var parts = new List<ComponentPart>();
        for (var i = 0; i < objects.Count; i++)
        {
            if (Format(objects[i]) is not { } component || !uint.TryParse(counts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return null;
            parts.Add(new ComponentPart(component, count));
        }
        return parts;
    }

    public static (string? Bench, string Created)? RecipeHeader(IRecordReader reader, string id) =>
        Link(reader, id, RecipeCreatedPath) is { } created ? (Link(reader, id, RecipeBenchPath), created) : null;

    // Invariant: null when the header, components or categories are unreadable, so the caller skips the recipe; null Conditions are kept so the gate hides and counts it.
    public static RecipeRecord? RecipeBody(IRecordReader reader, string id)
    {
        if (RecipeHeader(reader, id) is not { } header
            || Components(reader, id, RecipeComponentObjectPath, RecipeComponentCountPath) is not { } components
            || reader.List(id, RecipeCategoryPath) is not { } categories) return null;
        var count = int.TryParse(reader.Scalar(id, RecipeCountPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var created) ? created : 1;
        return new RecipeRecord(header.Bench, header.Created, categories.Select(Format).OfType<string>().ToArray(), components, Math.Max(1, count), reader.RecipeConditions(id),
            reader.Text(id, RecipeDescriptionPath) ?? string.Empty);
    }

    // Requires: the item and count rows are one CNTO run, so equal lengths; a null or unparsable item or count makes the inventory unreadable (null), as the typed decode threw.
    public static List<InventoryStack>? NpcItems(IRecordReader reader, string id)
    {
        if (reader.List(id, "NPC_\\ItemObject") is not { } items || reader.List(id, "NPC_\\ItemCount") is not { } counts || items.Count != counts.Count) return null;
        var stacks = new List<InventoryStack>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (Format(items[i]) is not { } item || !int.TryParse(counts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return null;
            stacks.Add(new InventoryStack(item, count));
        }
        return stacks;
    }

    // Requires: the faction and rank rows are one SNAM run, so equal lengths; an empty faction or an unparsable rank makes the list unreadable (null), as the typed decode threw.
    public static List<(string Faction, int Rank)>? NpcFactions(IRecordReader reader, string id)
    {
        if (reader.List(id, "NPC_\\Faction") is not { } factions || reader.List(id, "NPC_\\FactionRank") is not { } ranks || factions.Count != ranks.Count) return null;
        var memberships = new List<(string Faction, int Rank)>(factions.Count);
        for (var i = 0; i < factions.Count; i++)
        {
            if (Format(factions[i]) is not { } faction || !int.TryParse(ranks[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank)) return null;
            memberships.Add((faction, rank));
        }
        return memberships;
    }

    // Invariant: APPR is read through the row of the item's own signature; any other signature or an unreadable row has no slots.
    public static IReadOnlyList<string> AttachParentSlots(IRecordReader reader, string id)
    {
        var path = reader.Signature(id) switch { "ARMO" => "ARMO\\AttachParentSlots", "WEAP" => "WEAP\\AttachParentSlots", _ => null };
        return path is not null && reader.List(id, path) is { } slots ? slots.Select(Format).OfType<string>().ToArray() : [];
    }

    // Invariant: OMOD\AttachParentSlot rows are raw u32 words, resolved against the OMOD's own masters; a null, unparsable or unresolved word is dropped and an unreadable row is no slots.
    public static IReadOnlyList<string> ModAttachParentSlots(IRecordReader reader, string mod) =>
        reader.List(mod, "OMOD\\AttachParentSlot") is { } words
            ? words.Select(word => uint.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ? Format(reader.ResolveFormId(mod, raw)) : null).OfType<string>().ToArray()
            : [];

    // Invariant: null when the item is not an ARMO or WEAP or its keyword row is unreadable; null links are dropped.
    public static IReadOnlyList<string>? ItemKeywords(IRecordReader reader, string id) =>
        reader.Signature(id) switch { "ARMO" => "ARMO\\Keywords", "WEAP" => "WEAP\\Keywords", _ => null } is { } path && reader.List(id, path) is { } keywords
            ? keywords.Select(Format).OfType<string>().ToArray() : null;

    // Invariant: null when the target keyword row is unreadable; null links are dropped.
    public static IReadOnlyList<string>? ModTargetKeywords(IRecordReader reader, string mod) =>
        reader.List(mod, "OMOD\\TargetOMODKeywords") is { } keywords ? keywords.Select(Format).OfType<string>().ToArray() : null;

    // Requires: the keyword and reference lists are one XLKR array, so equal lengths; ACHR names its keyword row LinkedReferenceKeyword and REFR names it LinkedKeyword, and a row of the other signature fails.
    private static (IReadOnlyList<string> Keywords, IReadOnlyList<string> References)? LinkedRows(IRecordReader reader, string id)
    {
        var (keywordPath, referencePath) = reader.Signature(id) switch
        {
            "ACHR" => ("ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference"),
            "REFR" => ("REFR\\LinkedKeyword", "REFR\\LinkedReference"),
            _ => (null, null),
        };
        if (keywordPath is null || referencePath is null
            || reader.List(id, keywordPath) is not { } keywords || reader.List(id, referencePath) is not { } references
            || keywords.Count != references.Count) return null;
        return (keywords, references);
    }

    public static List<(string Keyword, string Reference)> LinkedRefs(IRecordReader reader, string id)
    {
        var links = new List<(string Keyword, string Reference)>();
        if (LinkedRows(reader, id) is not { } rows) return links;
        for (var i = 0; i < rows.Keywords.Count; i++)
            if (Format(rows.Keywords[i]) is { } keyword && Format(rows.References[i]) is { } reference)
                links.Add((keyword, reference));
        return links;
    }

    // Invariant: the complement of LinkedRefs, the XLKR entries with no keyword (empty or zero-local) and a real reference, in link order.
    public static List<string> DefaultLinkedRefs(IRecordReader reader, string id)
    {
        var links = new List<string>();
        if (LinkedRows(reader, id) is not { } rows) return links;
        for (var i = 0; i < rows.Keywords.Count; i++)
            if (Format(rows.Keywords[i]) is null && Format(rows.References[i]) is { } reference)
                links.Add(reference);
        return links;
    }
}
