using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Trade;

// Requires: main thread only. Rows are keyed by actor reference key, ignoring case; a null faction is a recorded "reports to nobody", not an absent row.
public sealed class CrimeFactionOverrides
{
    private readonly Dictionary<string, string?> _rows = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _rows.Count == 0;

    public IReadOnlyDictionary<string, string?> Rows => _rows;

    public void Set(string actor, string? faction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        _rows[actor] = string.IsNullOrWhiteSpace(faction) ? null : faction;
    }

    public bool TryGet(string actor, out string? faction)
    {
        faction = null;
        return !string.IsNullOrWhiteSpace(actor) && _rows.TryGetValue(actor, out faction);
    }

    public void Clear() => _rows.Clear();

    public void ReplaceWith(CrimeFactionOverrides other)
    {
        var rows = other._rows.ToArray();
        _rows.Clear();
        foreach (var (actor, faction) in rows) _rows[actor] = faction;
    }

    public JsonArray ToJson() => new(_rows.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
    {
        var row = new JsonObject { ["actor"] = pair.Key };
        if (pair.Value is { } faction) row["faction"] = faction;
        return (JsonNode)row;
    }).ToArray());

    // Requires: throws FormatException on a node that is not an array of rows with a non-blank actor; the rows already held are replaced only on success.
    public void LoadJson(JsonNode node)
    {
        if (node is not JsonArray array) throw new FormatException("crime faction overrides are not an array");
        var loaded = new CrimeFactionOverrides();
        foreach (var row in array)
        {
            var actor = row?["actor"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(actor)) throw new FormatException("crime faction override row is malformed");
            loaded.Set(actor, row!["faction"]?.GetValue<string>());
        }
        ReplaceWith(loaded);
    }
}
