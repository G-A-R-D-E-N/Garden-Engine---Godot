using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; keys are global form keys normalised by RecordKey, and a value here overrides the record's stored value.
public sealed class GameGlobals
{
    public const string SectionName = "game.globals.json";
    public const int SectionVersion = 1;

    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

    public static GameGlobals Current { get; } = new();

    public long Version { get; private set; }

    public bool IsEmpty => _values.Count == 0;

    public static string Normalise(string key) =>
        RecordKey.TryParse(key?.Trim(), out var plugin, out var local) ? RecordKey.Format(plugin, local) : key?.Trim() ?? string.Empty;

    public double? Value(string key) => _values.TryGetValue(Normalise(key), out var value) ? value : null;

    public bool Set(string key, double value)
    {
        var name = Normalise(key);
        if (name.Length == 0 || !double.IsFinite(value)) return false;
        if (_values.TryGetValue(name, out var held) && held == value) return true;
        _values[name] = value;
        Version++;
        return true;
    }

    public void Clear()
    {
        if (_values.Count == 0) return;
        _values.Clear();
        Version++;
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_values.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["global"] = pair.Key, ["value"] = pair.Value }).ToArray());
        var root = new JsonObject { ["version"] = SectionVersion, ["globals"] = rows };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: replaces the whole set; throws FormatException on a malformed section and leaves the current values untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("game globals are not an object");
                if ((int?)root["version"] != SectionVersion) throw new FormatException("unsupported game globals version");
                foreach (var node in root["globals"] as JsonArray ?? throw new FormatException("game globals have no list"))
                {
                    var key = node?["global"]?.GetValue<string>();
                    var value = node?["value"]?.GetValue<double>();
                    if (string.IsNullOrWhiteSpace(key) || value is not { } v || !double.IsFinite(v)) throw new FormatException("game global row is malformed");
                    held[Normalise(key)] = v;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("game globals are malformed", ex); }
        }
        _values.Clear();
        foreach (var (key, value) in held) _values[key] = value;
        Version++;
    }
}
