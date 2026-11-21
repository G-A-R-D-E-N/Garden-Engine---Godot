using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; keyed by the bare reference form key.
public sealed class EnableOverrides
{
    public const string SectionName = "script.enable.json";

    private readonly Dictionary<string, bool> _changed = new(StringComparer.Ordinal);

    public int Count => _changed.Count;

    public IReadOnlyDictionary<string, bool> All => _changed;

    public void Set(string refKey, bool enabled) => _changed[refKey] = enabled;

    public bool Clear(string refKey) => _changed.Remove(refKey);

    public void ClearAll() => _changed.Clear();

    // Invariant: the keys that had an override before and have none now, in their original order.
    public static IReadOnlyList<string> Dropped(IEnumerable<string> before, IReadOnlyDictionary<string, bool> now) =>
        before.Where(key => !now.ContainsKey(key)).Distinct(StringComparer.Ordinal).ToArray();

    public bool? StateOf(string refKey) => _changed.TryGetValue(refKey, out var enabled) ? enabled : null;

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_changed.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_changed.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["ref"] = pair.Key, ["enabled"] = pair.Value }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every override; throws FormatException on a malformed section and leaves the current overrides untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("enable overrides are not a list"))
                    held[node?["ref"]?.GetValue<string>() is { Length: > 0 } key ? key : throw new FormatException("enable override has no ref")] =
                        node["enabled"]?.GetValue<bool>() ?? throw new FormatException("enable override has no state");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("enable overrides are malformed", ex); }
        }
        _changed.Clear();
        foreach (var (key, enabled) in held) _changed[key] = enabled;
    }
}
