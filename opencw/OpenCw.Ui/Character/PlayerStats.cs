using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; counters are the fixed set below, each raised by one existing gameplay event.
public sealed class PlayerStats
{
    public const string SectionName = "player.stats.json";
    public const string Kills = "kills";
    public const string LocksPicked = "locks_picked";

    public static readonly IReadOnlyList<(string Key, string Label)> Counters =
        [(Kills, "Actors killed"), (LocksPicked, "Locks picked")];

    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);

    public static PlayerStats Current { get; } = new();

    public bool IsEmpty => _counts.Count == 0;

    public long Count(string key) => _counts.TryGetValue(key, out var count) ? count : 0;

    // Invariant: only a known counter rises and only by a positive amount, so a stray key never reaches a save.
    public bool Add(string key, long amount = 1)
    {
        if (amount <= 0 || !Counters.Any(counter => counter.Key == key)) return false;
        var held = Count(key);
        _counts[key] = amount > long.MaxValue - held ? long.MaxValue : held + amount;
        return true;
    }

    public void Clear() => _counts.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_counts.Count == 0) return new Dictionary<string, byte[]>();
        var row = new JsonObject();
        foreach (var (key, count) in _counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)) row[key] = count;
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(row.ToJsonString()) };
    }

    // Constraint: replaces every counter; throws FormatException on a malformed section and leaves the counters untouched; a key this build does not know is dropped.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, long>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var (key, node) in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player stats are not an object"))
                {
                    var count = node is null ? throw new FormatException("player stat is empty") : node.GetValue<long>();
                    if (count < 0) throw new FormatException("player stat is negative");
                    if (count > 0 && Counters.Any(counter => counter.Key == key)) held[key] = count;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player stats are malformed", ex); }
        }
        _counts.Clear();
        foreach (var (key, count) in held) _counts[key] = count;
    }
}
