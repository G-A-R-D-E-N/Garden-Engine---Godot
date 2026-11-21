using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Combat;

public static class HazardSave
{
    public const string SectionName = "combat.hazards.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(IReadOnlyList<HazardRow> rows) =>
        rows.Count == 0
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(rows)) };

    // Invariant: null means the save has no section, and the live field is left cleared; a malformed section throws FormatException before anything is applied.
    public static IReadOnlyList<HazardRow>? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    public static string Serialize(IReadOnlyList<HazardRow> rows)
    {
        var array = new JsonArray();
        foreach (var row in rows)
        {
            var node = new JsonObject
            {
                ["token"] = row.Token,
                ["base"] = row.Base,
                ["x"] = row.Position.X,
                ["y"] = row.Position.Y,
                ["z"] = row.Position.Z,
                ["age"] = row.Age,
                ["enabled"] = row.Enabled,
            };
            if (row.Source.Length > 0) node["source"] = row.Source;
            array.Add(node);
        }
        return new JsonObject { ["version"] = CurrentVersion, ["hazards"] = array }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static IReadOnlyList<HazardRow> Deserialize(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new FormatException("the hazard section must be a JSON object");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the hazard section is not valid JSON: {ex.Message}", ex);
        }
        if (Get(root, "version", n => n.GetValue<int>()) != CurrentVersion) throw new FormatException("the hazard section version is not supported");
        var array = root["hazards"] as JsonArray ?? throw new FormatException("the hazard section has no hazards array");
        if (array.Count > HazardField.Capacity) throw new FormatException($"the hazard section holds {array.Count} hazards, the limit is {HazardField.Capacity}");
        return array.Select(node => Row(node as JsonObject ?? throw new FormatException("a hazard row must be an object"))).ToList();
    }

    private static HazardRow Row(JsonObject node)
    {
        var token = Get(node, "token", n => n.GetValue<string>());
        var baseKey = Get(node, "base", n => n.GetValue<string>());
        if (string.IsNullOrEmpty(baseKey)) throw new FormatException("a hazard row needs a base record key");
        var position = new Vector3(Finite(node, "x"), Finite(node, "y"), Finite(node, "z"));
        var age = Get(node, "age", n => n.GetValue<double>());
        if (!double.IsFinite(age) || age < 0) throw new FormatException("a hazard's age must be a finite number of seconds, zero or more");
        var source = node["source"] is null ? "" : Get(node, "source", n => n.GetValue<string>());
        return new HazardRow(token, baseKey, position, age, Get(node, "enabled", n => n.GetValue<bool>()), source);
    }

    private static float Finite(JsonObject node, string name)
    {
        var value = Get(node, name, n => n.GetValue<float>());
        return float.IsFinite(value) ? value : throw new FormatException($"'{name}' must be a finite number");
    }

    private static T Get<T>(JsonNode node, string name, Func<JsonNode, T> read)
    {
        var value = node[name] ?? throw new FormatException($"missing '{name}'");
        try
        {
            return read(value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new FormatException($"'{name}' has the wrong type: {ex.Message}", ex);
        }
    }
}
