using System.Collections.Generic;
using System.Text.Json;

namespace OpenCommonwealth.Services.Ui.Layout;

public sealed record MenuSwfRef(string Menu, string? Archive, string Entry, IReadOnlyDictionary<string, string> Rows);

public static class MenuSwfMapping
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static IReadOnlyList<MenuSwfRef> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"menu layout mapping is not valid JSON: {ex.Message}", ex);
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("menus", out var menus)
                || menus.ValueKind != JsonValueKind.Array)
                throw new FormatException("menu layout mapping needs a \"menus\" array");
            var refs = new List<MenuSwfRef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in menus.EnumerateArray())
            {
                var entry = ReadRef(item);
                if (!seen.Add(entry.Menu))
                    throw new FormatException($"menu layout mapping lists menu \"{entry.Menu}\" twice");
                refs.Add(entry);
            }
            return refs;
        }
    }

    private static MenuSwfRef ReadRef(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new FormatException("each menu layout mapping entry must be an object");
        var menu = RequiredString(item, "menu");
        var entry = RequiredString(item, "entry");
        var archive = OptionalString(item, "archive", menu);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.TryGetProperty("rows", out var rowMap))
        {
            if (rowMap.ValueKind != JsonValueKind.Object)
                throw new FormatException($"menu \"{menu}\": \"rows\" must be an object");
            foreach (var row in rowMap.EnumerateObject())
            {
                if (row.Value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(row.Value.GetString()))
                    throw new FormatException($"menu \"{menu}\": row \"{row.Name}\" must map to a non-empty path");
                rows[row.Name] = row.Value.GetString()!;
            }
        }
        return new MenuSwfRef(menu, archive, entry, rows);
    }

    private static string RequiredString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new FormatException($"menu layout mapping entry needs a non-empty \"{name}\"");
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement item, string name, string menu)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new FormatException($"menu \"{menu}\": \"{name}\" must be a string");
        var text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
