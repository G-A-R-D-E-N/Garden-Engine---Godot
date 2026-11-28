using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only.
public sealed class ActivationBlocks
{
    public const string SectionName = "activation.blocks.json";

    private readonly Dictionary<string, bool> _hideText = new(StringComparer.Ordinal);

    public void Set(string refKey, bool blocked, bool hideText)
    {
        if (blocked) _hideText[refKey] = hideText;
        else _hideText.Remove(refKey);
    }

    public bool IsBlocked(string refKey) => _hideText.ContainsKey(refKey);

    public bool HidesText(string refKey) => _hideText.TryGetValue(refKey, out var hide) && hide;

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_hideText.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_hideText.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)new JsonObject { ["ref"] = pair.Key, ["hideText"] = pair.Value }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every block; throws FormatException on a malformed section and leaves the current blocks untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("activation blocks are not a list"))
                    held[node?["ref"]?.GetValue<string>() is { Length: > 0 } key ? key : throw new FormatException("activation block has no ref")] = node["hideText"]?.GetValue<bool>() ?? false;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("activation blocks are malformed", ex); }
        }
        _hideText.Clear();
        foreach (var (key, hide) in held) _hideText[key] = hide;
    }
}
