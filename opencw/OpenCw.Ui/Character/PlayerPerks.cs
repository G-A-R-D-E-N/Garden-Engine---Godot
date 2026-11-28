using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; keys are perk form keys, compared ignoring case after RecordKey normalisation.
public sealed class PlayerPerks
{
    public const string SectionName = "player.perks.json";

    private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

    public static PlayerPerks Current { get; } = new();

    public bool IsEmpty => _ranks.Count == 0;

    public IReadOnlyDictionary<string, int> Ranks => _ranks;

    public static string Normalise(string perk) =>
        RecordKey.TryParse(perk?.Trim(), out var plugin, out var local) ? RecordKey.Format(plugin, local) : perk?.Trim() ?? string.Empty;

    public int RankOf(string perk) => _ranks.GetValueOrDefault(Normalise(perk));

    public bool Has(string perk) => RankOf(perk) > 0;

    // Constraint: a perk already held is left as it is; the engine rule for a second AddPerk is UNVERIFIED.
    public bool Add(string perk)
    {
        var key = Normalise(perk);
        if (key.Length == 0 || _ranks.ContainsKey(key)) return false;
        _ranks[key] = 1;
        return true;
    }

    public bool Remove(string perk) => _ranks.Remove(Normalise(perk));

    public void Clear() => _ranks.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_ranks.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_ranks.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["perk"] = pair.Key, ["rank"] = pair.Value }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every perk; throws FormatException on a malformed section and leaves the current perks untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("player perks are not a list"))
                {
                    var perk = node?["perk"]?.GetValue<string>();
                    var rank = node?["rank"]?.GetValue<int>();
                    if (string.IsNullOrWhiteSpace(perk) || rank is not > 0) throw new FormatException("player perk row is malformed");
                    held[Normalise(perk)] = rank.Value;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player perks are malformed", ex); }
        }
        _ranks.Clear();
        foreach (var (key, rank) in held) _ranks[key] = rank;
    }
}
