using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; rows are keyed by actor reference key and faction form key, both compared ignoring case after RecordKey normalisation. A stored rank of -1 means the actor left a faction its base record lists.
public sealed class ActorFactions
{
    public const string SectionName = "actor.factions.json";
    public const int SectionVersion = 1;
    public const int NotMember = PlayerFactions.NotMember;

    private readonly Dictionary<string, Dictionary<string, int>> _rows = new(StringComparer.OrdinalIgnoreCase);

    public static ActorFactions Current { get; } = new();

    public bool IsEmpty => _rows.Count == 0;

    public bool SetRank(string actor, string faction, int rank)
    {
        var key = PlayerFactions.Normalise(faction);
        if (string.IsNullOrWhiteSpace(actor) || key.Length == 0 || rank < 0) return false;
        Row(actor)[key] = rank;
        return true;
    }

    public bool Leave(string actor, string faction)
    {
        var key = PlayerFactions.Normalise(faction);
        if (string.IsNullOrWhiteSpace(actor) || key.Length == 0) return false;
        Row(actor)[key] = NotMember;
        return true;
    }

    public void Clear() => _rows.Clear();

    public int? Changed(string actor, string faction) =>
        _rows.TryGetValue(PlayerFactions.Normalise(actor), out var row) && row.TryGetValue(PlayerFactions.Normalise(faction), out var changed) ? changed : null;

    public int Rank(string actor, string faction, IReadOnlyDictionary<FormKey, int> baseMemberships)
    {
        var key = PlayerFactions.Normalise(faction);
        if (Changed(actor, faction) is { } changed) return changed;
        foreach (var pair in baseMemberships)
            if (string.Equals(RecordKey.Format(pair.Key), key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return NotMember;
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_rows.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_rows.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(actor =>
            actor.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (JsonNode)new JsonObject { ["actor"] = actor.Key, ["faction"] = pair.Key, ["rank"] = pair.Value })).ToArray());
        var root = new JsonObject { ["version"] = SectionVersion, ["rows"] = rows };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: throws FormatException on a malformed section and touches nothing; a save without the section reads as no overlay.
    public static Dictionary<string, Dictionary<string, int>> Parse(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        if (!sections.TryGetValue(SectionName, out var payload)) return held;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("actor factions are not an object");
            if ((int?)root["version"] != SectionVersion) throw new FormatException("unsupported actor factions version");
            foreach (var node in root["rows"] as JsonArray ?? throw new FormatException("actor factions have no list"))
            {
                var actor = node?["actor"]?.GetValue<string>();
                var faction = node?["faction"]?.GetValue<string>();
                var rank = node?["rank"]?.GetValue<int>();
                if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(faction) || rank is not >= NotMember) throw new FormatException("actor faction row is malformed");
                var key = PlayerFactions.Normalise(actor);
                if (!held.TryGetValue(key, out var row)) held[key] = row = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                row[PlayerFactions.Normalise(faction)] = rank.Value;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("actor factions are malformed", ex); }
        return held;
    }

    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = Parse(sections);
        _rows.Clear();
        foreach (var (actor, row) in held) _rows[actor] = row;
    }

    private Dictionary<string, int> Row(string actor)
    {
        var key = PlayerFactions.Normalise(actor);
        if (!_rows.TryGetValue(key, out var row)) _rows[key] = row = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return row;
    }
}
