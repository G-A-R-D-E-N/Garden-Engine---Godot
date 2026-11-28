using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; keys are faction form keys, compared ignoring case after RecordKey normalisation. A stored rank of -1 means the player left a faction the base record lists.
public sealed class PlayerFactions
{
    public const string SectionName = "player.factions.json";
    public const int SectionVersion = 1;
    public const int NotMember = -1;

    private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

    public static PlayerFactions Current { get; } = new();

    public bool IsEmpty => _ranks.Count == 0;

    public IReadOnlyDictionary<string, int> Overrides => _ranks;

    public static string Normalise(string faction) =>
        RecordKey.TryParse(faction?.Trim(), out var plugin, out var local) ? RecordKey.Format(plugin, local) : faction?.Trim() ?? string.Empty;

    public bool SetRank(string faction, int rank)
    {
        var key = Normalise(faction);
        if (key.Length == 0 || rank < 0) return false;
        _ranks[key] = rank;
        return true;
    }

    public bool Leave(string faction)
    {
        var key = Normalise(faction);
        if (key.Length == 0) return false;
        _ranks[key] = NotMember;
        return true;
    }

    public void Clear() => _ranks.Clear();

    public IReadOnlyDictionary<FormKey, int> Merge(IReadOnlyDictionary<FormKey, int>? baseMemberships)
    {
        var merged = new Dictionary<string, KeyValuePair<FormKey, int>>(StringComparer.OrdinalIgnoreCase);
        if (baseMemberships is not null)
            foreach (var pair in baseMemberships) merged[RecordKey.Format(pair.Key)] = pair;
        foreach (var (faction, rank) in _ranks)
        {
            if (rank < 0) merged.Remove(faction);
            else if (RecordKey.TryParse(faction, out var plugin, out var local)) merged[faction] = new KeyValuePair<FormKey, int>(new FormKey(plugin, local), rank);
        }
        return merged.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public int Rank(string faction, IReadOnlyDictionary<FormKey, int>? baseMemberships)
    {
        var key = Normalise(faction);
        foreach (var pair in Merge(baseMemberships))
            if (string.Equals(RecordKey.Format(pair.Key), key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return NotMember;
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_ranks.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_ranks.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["faction"] = pair.Key, ["rank"] = pair.Value }).ToArray());
        var root = new JsonObject { ["version"] = SectionVersion, ["factions"] = rows };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: replaces the whole overlay; throws FormatException on a malformed section and leaves the current overlay untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player factions are not an object");
                if ((int?)root["version"] != SectionVersion) throw new FormatException("unsupported player factions version");
                foreach (var node in root["factions"] as JsonArray ?? throw new FormatException("player factions have no list"))
                {
                    var faction = node?["faction"]?.GetValue<string>();
                    var rank = node?["rank"]?.GetValue<int>();
                    if (string.IsNullOrWhiteSpace(faction) || rank is not >= NotMember) throw new FormatException("player faction row is malformed");
                    held[Normalise(faction)] = rank.Value;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player factions are malformed", ex); }
        }
        _ranks.Clear();
        foreach (var (key, rank) in held) _ranks[key] = rank;
    }
}
