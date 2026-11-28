using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record KnownSettlement(string Id, string Name);

// Requires: main thread only; a settlement is listed once the player has opened its workshop, keyed by the workshop reference key.
public sealed class PlayerSettlements
{
    public const string SectionName = "player.settlements.json";

    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public static PlayerSettlements Current { get; } = new();

    public bool IsEmpty => _names.Count == 0;

    public IReadOnlyList<KnownSettlement> All =>
        _names.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new KnownSettlement(pair.Key, pair.Value)).ToArray();

    // Invariant: an empty id or name is refused; a repeat visit renames the row instead of adding one.
    public bool Note(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return false;
        _names[id] = name;
        return true;
    }

    public void Clear() => _names.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_names.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(All.Select(known => (JsonNode)new JsonObject { ["id"] = known.Id, ["name"] = known.Name }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every settlement; throws FormatException on a malformed section and leaves the list untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("player settlements are not a list"))
                {
                    var id = node?["id"]?.GetValue<string>();
                    var name = node?["name"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new FormatException("player settlement row is malformed");
                    held[id] = name;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player settlements are malformed", ex); }
        }
        _names.Clear();
        foreach (var (id, name) in held) _names[id] = name;
    }
}
