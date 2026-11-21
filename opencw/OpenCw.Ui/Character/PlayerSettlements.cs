using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record KnownSettlement(string Id, string Name, int? People = null, int? Beds = null, double? MapX = null, double? MapY = null, string? World = null);

// Requires: main thread only; a settlement is listed once the player has opened its workshop, keyed by the workshop reference key.
public sealed class PlayerSettlements
{
    public const string SectionName = "player.settlements.json";

    private sealed record Entry(string Name, int? People = null, int? Beds = null, double? X = null, double? Y = null, string? World = null);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static PlayerSettlements Current { get; } = new();

    public bool IsEmpty => _entries.Count == 0;

    public IReadOnlyList<KnownSettlement> All =>
        _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KnownSettlement(pair.Key, pair.Value.Name, pair.Value.People, pair.Value.Beds, pair.Value.X, pair.Value.Y, pair.Value.World)).ToArray();

    // Invariant: an empty id or name is refused; a repeat visit renames the row instead of adding one and keeps what was measured.
    public bool Note(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return false;
        _entries[id] = _entries.TryGetValue(id, out var held) ? held with { Name = name } : new Entry(name);
        return true;
    }

    // Invariant: only a settlement already noted is measured, a negative count or a null argument leaves that field as it was.
    public bool Measure(string id, int? people = null, int? beds = null)
    {
        if (!_entries.TryGetValue(id, out var held)) return false;
        _entries[id] = held with { People = people is >= 0 ? people : held.People, Beds = beds is >= 0 ? beds : held.Beds };
        return true;
    }

    // Invariant: the map position is in map space (FO4 X and negated Y) and a non-finite one is refused.
    public bool Place(string id, double x, double y, string? world = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !_entries.TryGetValue(id, out var held)) return false;
        _entries[id] = held with { X = x, Y = y, World = string.IsNullOrEmpty(world) ? null : world };
        return true;
    }

    public void Clear() => _entries.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_entries.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(All.Select(known => (JsonNode)Row(known)).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every settlement; throws FormatException on a malformed section and leaves the list untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("player settlements are not a list"))
                {
                    var id = node?["id"]?.GetValue<string>();
                    var name = node?["name"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new FormatException("player settlement row is malformed");
                    var x = node?["x"]?.GetValue<double>();
                    var y = node?["y"]?.GetValue<double>();
                    var place = x is { } px && y is { } py && double.IsFinite(px) && double.IsFinite(py);
                    held[id] = new Entry(name, Count(node?["people"]?.GetValue<int>()), Count(node?["beds"]?.GetValue<int>()), place ? x : null, place ? y : null, place ? node?["world"]?.GetValue<string>() : null);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player settlements are malformed", ex); }
        }
        _entries.Clear();
        foreach (var (id, entry) in held) _entries[id] = entry;
    }

    private static int? Count(int? value) => value is >= 0 ? value : null;

    private static JsonObject Row(KnownSettlement known)
    {
        var row = new JsonObject { ["id"] = known.Id, ["name"] = known.Name };
        if (known.People is { } people) row["people"] = people;
        if (known.Beds is { } beds) row["beds"] = beds;
        if (known.MapX is { } x && known.MapY is { } y)
        {
            row["x"] = x;
            row["y"] = y;
            if (known.World is { } world) row["world"] = world;
        }
        return row;
    }
}
