using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: main thread only; one owner per object, at most one work object and one bed per settler, and an object is in only one table.
public sealed class SettlerAssignments
{
    public const string SectionName = "workshop.settlers.json";

    private readonly Dictionary<string, string> _work = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _beds = new(StringComparer.Ordinal);

    public static SettlerAssignments Current { get; } = new();

    public bool IsEmpty => _work.Count == 0 && _beds.Count == 0;

    public string? OwnerOf(string objectId) =>
        _work.TryGetValue(objectId, out var work) ? work : _beds.TryGetValue(objectId, out var bed) ? bed : null;

    public string? WorkObjectOf(string settlerId) => FindObject(_work, settlerId);

    public string? BedOf(string settlerId) => FindObject(_beds, settlerId);

    public string? Assign(string settlerId, string objectId, bool bed)
    {
        ArgumentException.ThrowIfNullOrEmpty(settlerId);
        ArgumentException.ThrowIfNullOrEmpty(objectId);
        if (Conflicts(objectId, bed)) throw new InvalidOperationException("the object is already assigned as " + (bed ? "a work object" : "a bed"));
        var table = bed ? _beds : _work;
        table.TryGetValue(objectId, out var previous);
        foreach (var held in table.Where(pair => pair.Value == settlerId).Select(pair => pair.Key).ToList()) table.Remove(held);
        table[objectId] = settlerId;
        return previous is not null && previous != settlerId ? previous : null;
    }

    public bool Conflicts(string objectId, bool bed) => (bed ? _work : _beds).ContainsKey(objectId);

    public bool Unassign(string settlerId)
    {
        var held = _work.Where(pair => pair.Value == settlerId).Select(pair => pair.Key).ToList();
        foreach (var objectId in held) _work.Remove(objectId);
        return held.Count > 0;
    }

    public bool UnassignBed(string settlerId)
    {
        var held = _beds.Where(pair => pair.Value == settlerId).Select(pair => pair.Key).ToList();
        foreach (var objectId in held) _beds.Remove(objectId);
        return held.Count > 0;
    }

    public bool ObjectGone(string objectId) => _work.Remove(objectId) | _beds.Remove(objectId);

    public bool SettlerGone(string settlerId) => Unassign(settlerId) | UnassignBed(settlerId);

    public void Clear()
    {
        _work.Clear();
        _beds.Clear();
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (IsEmpty) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_work.Select(pair => (pair.Key, pair.Value, Bed: false)).Concat(_beds.Select(pair => (pair.Key, pair.Value, Bed: true)))
            .OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => Row(row.Key, row.Value, row.Bed)).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Requires: a malformed section throws FormatException and leaves the store untouched; a missing section clears it.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var work = new Dictionary<string, string>(StringComparer.Ordinal);
        var beds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("settler assignments are not a list"))
                {
                    var settler = Text(node, "settler");
                    var objectId = Text(node, "object");
                    var table = node?["bed"]?.GetValue<bool>() == true ? beds : work;
                    if (table.ContainsKey(objectId) || table.ContainsValue(settler))
                        throw new FormatException("settler assignment is duplicated");
                    table[objectId] = settler;
                }
                if (work.Keys.Any(beds.ContainsKey)) throw new FormatException("settler assignment object is both a work object and a bed");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("settler assignments are malformed", ex); }
        }
        _work.Clear();
        _beds.Clear();
        foreach (var pair in work) _work[pair.Key] = pair.Value;
        foreach (var pair in beds) _beds[pair.Key] = pair.Value;
    }

    private static string? FindObject(Dictionary<string, string> table, string settlerId) =>
        table.Where(pair => pair.Value == settlerId).Select(pair => pair.Key).FirstOrDefault();

    private static JsonNode Row(string objectId, string settlerId, bool bed) =>
        new JsonObject { ["settler"] = settlerId, ["object"] = objectId, ["bed"] = bed };

    private static string Text(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException($"settler assignment {name} is missing");
}
