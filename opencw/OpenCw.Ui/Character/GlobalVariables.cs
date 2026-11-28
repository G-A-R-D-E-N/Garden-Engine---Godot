using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class GlobalVariables
{
    public const string SectionName = "globals.json";
    public const int SectionVersion = 1;

    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

    public static GlobalVariables Current { get; } = new();

    public bool IsEmpty => _values.Count == 0;

    public bool Set(string global, double value)
    {
        var key = PlayerFactions.Normalise(global);
        if (key.Length == 0 || !double.IsFinite(value)) return false;
        _values[key] = value;
        return true;
    }

    public double? Get(string global) => _values.TryGetValue(PlayerFactions.Normalise(global), out var value) ? value : null;

    public void Clear() => _values.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_values.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["global"] = pair.Key, ["value"] = pair.Value }).ToArray());
        var root = new JsonObject { ["version"] = SectionVersion, ["rows"] = rows };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: throws FormatException on a malformed section and touches nothing; a save without the section reads as no overlay.
    public static Dictionary<string, double> Parse(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!sections.TryGetValue(SectionName, out var payload)) return held;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("global variables are not an object");
            if ((int?)root["version"] != SectionVersion) throw new FormatException("unsupported global variables version");
            foreach (var node in root["rows"] as JsonArray ?? throw new FormatException("global variables have no list"))
            {
                var global = node?["global"]?.GetValue<string>();
                var value = node?["value"]?.GetValue<double>();
                if (string.IsNullOrWhiteSpace(global) || value is not { } number || !double.IsFinite(number)) throw new FormatException("global variable row is malformed");
                held[PlayerFactions.Normalise(global)] = number;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("global variables are malformed", ex); }
        return held;
    }

    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = Parse(sections);
        _values.Clear();
        foreach (var (global, value) in held) _values[global] = value;
    }
}
