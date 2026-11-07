using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>The value kinds a compiled reference's script attachment can carry.</summary>
public enum ScriptValueKind
{
    Int,
    Float,
    Bool,
    String,
    Object,

    /// <summary>A value Garden cannot reproduce. It is reported, never defaulted.</summary>
    Unsupported,
}

/// <summary>One typed VMAD property value of a script attachment.</summary>
public sealed record ScriptValue(string Name, ScriptValueKind Kind, string? Value, string? Reason)
{
    public bool IsSupported => Kind != ScriptValueKind.Unsupported;
}

/// <summary>One script attached to a compiled reference, with its typed values.</summary>
public sealed record ScriptAttachment(string Name, IReadOnlyList<ScriptValue> Values);

/// <summary>
/// The script attachments of one compiled reference.
/// </summary>
/// <remarks>
/// The compiled cell stores the attachments as metadata in the same JSON shape the cell pipeline
/// already produces, so nothing is reparsed from a Bethesda binary here and a compiled cell
/// round-trips its attachments exactly. A value Garden cannot reproduce is marked
/// <see cref="ScriptValueKind.Unsupported"/> with a reason instead of being dropped or replaced by a
/// PEX default, duplicate script names on one owner are counted so the cell reports them, and floats
/// are always rendered with the invariant culture so a locale cannot change a value.
/// </remarks>
public static class ScriptAttachments
{
    /// <summary>The metadata key a compiled reference node uses for its attachments.</summary>
    public const string MetadataKey = "fo4_scripts";

    public const string UnsupportedUnknownKindPrefix = "value kind is not reproducible: ";
    public const string UnsupportedFloatPrefix = "float value is not reproducible: ";
    public const string UnsupportedEmptyObject = "object value is empty";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Reads the attachments from the properties JSON. Blank input is no attachments.</summary>
    public static IReadOnlyList<ScriptAttachment> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ScriptAttachment>();
        }

        JsonNode root;
        try
        {
            root = JsonNode.Parse(json) ?? throw new FormatException("the script attachments were empty");
        }
        catch (JsonException error)
        {
            throw new FormatException("the script attachments are not valid JSON", error);
        }
        if (root is not JsonArray array)
        {
            throw new FormatException("script attachments must be a JSON array");
        }

        var attachments = new List<ScriptAttachment>(array.Count);
        foreach (var entry in array)
        {
            if (entry is not JsonObject script)
            {
                throw new FormatException("a script attachment must be a JSON object");
            }
            var name = RequiredName(script["name"], "script name");
            if (script["properties"] is not JsonArray properties)
                throw new FormatException($"script {name} properties must be a JSON array");
            var values = new List<ScriptValue>();
            foreach (var property in properties)
            {
                if (property is not JsonObject value)
                    throw new FormatException($"script {name} property must be a JSON object");
                values.Add(ReadValue(value));
            }
            attachments.Add(new ScriptAttachment(name, values));
        }
        return attachments;
    }

    /// <summary>Renders the attachments as the metadata a compiled reference node carries.</summary>
    public static string ToMetadataJson(IReadOnlyList<ScriptAttachment> attachments)
    {
        var array = new JsonArray();
        foreach (var attachment in attachments)
        {
            var properties = new JsonArray();
            foreach (var value in attachment.Values)
            {
                var entry = new JsonObject
                {
                    ["name"] = value.Name,
                    ["type"] = TypeName(value.Kind),
                    ["value"] = value.Value,
                };
                if (!value.IsSupported)
                {
                    entry["unsupported"] = value.Reason ?? UnsupportedUnknownKindPrefix + value.Kind;
                }
                properties.Add(entry);
            }
            array.Add(new JsonObject
            {
                ["name"] = attachment.Name,
                ["properties"] = properties,
            });
        }
        return array.ToJsonString(Options);
    }

    /// <summary>How many script names appear more than once on one owner.</summary>
    public static int CountDuplicateNames(IReadOnlyList<ScriptAttachment> attachments) =>
        attachments
            .GroupBy(attachment => attachment.Name, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

    /// <summary>How many values a compiled cell cannot reproduce.</summary>
    public static int CountUnsupportedValues(IReadOnlyList<ScriptAttachment> attachments) =>
        attachments.Sum(attachment => attachment.Values.Count(value => !value.IsSupported));

    /// <summary>
    /// Normalizes a float to its invariant round-trip form. A producer that wrote the value under a
    /// comma-decimal culture is accepted, but the emitted value is always invariant.
    /// </summary>
    public static bool TryNormalizeFloat(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && !double.IsNaN(value))
        {
            normalized = value.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }

    private static ScriptValue ReadValue(JsonObject property)
    {
        var name = RequiredName(property["name"], "property name");
        var kind = KindOf(Text(property["type"]));
        var raw = Text(property["value"]);
        var reported = Text(property["unsupported"]);
        if (!string.IsNullOrEmpty(reported)) return Unsupported(name, reported, reported);
        if ((kind is ScriptValueKind.String or ScriptValueKind.Object) &&
            (property["value"] is not JsonValue text || !text.TryGetValue<string>(out _)))
            return Unsupported(name, reported, "value must be JSON text");
        switch (kind)
        {
            case ScriptValueKind.Int:
                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    ? new ScriptValue(name, kind, integer.ToString(CultureInfo.InvariantCulture), null)
                    : Unsupported(name, reported, UnsupportedUnknownKindPrefix + raw);
            case ScriptValueKind.Float:
                return TryNormalizeFloat(raw, out var real)
                    ? new ScriptValue(name, kind, real, null)
                    : Unsupported(name, reported, UnsupportedFloatPrefix + raw);
            case ScriptValueKind.Bool:
                return bool.TryParse(raw, out var flag)
                    ? new ScriptValue(name, kind, flag ? "true" : "false", null)
                    : Unsupported(name, reported, UnsupportedUnknownKindPrefix + raw);
            case ScriptValueKind.String:
                return new ScriptValue(name, kind, raw, null);
            case ScriptValueKind.Object:
                return string.IsNullOrWhiteSpace(raw)
                    ? Unsupported(name, reported, UnsupportedEmptyObject)
                    : new ScriptValue(name, kind, raw, null);
            default:
                return Unsupported(name, reported, UnsupportedUnknownKindPrefix + Text(property["type"]));
        }
    }

    private static ScriptValue Unsupported(string name, string reported, string fallback) =>
        new(name, ScriptValueKind.Unsupported, null, string.IsNullOrEmpty(reported) ? fallback : reported);

    private static ScriptValueKind KindOf(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "int" or "integer" => ScriptValueKind.Int,
        "float" or "double" or "real" => ScriptValueKind.Float,
        "bool" or "boolean" => ScriptValueKind.Bool,
        "string" => ScriptValueKind.String,
        "object" => ScriptValueKind.Object,
        _ => ScriptValueKind.Unsupported,
    };

    private static string TypeName(ScriptValueKind kind) => kind switch
    {
        ScriptValueKind.Int => "Int",
        ScriptValueKind.Float => "Float",
        ScriptValueKind.Bool => "Bool",
        ScriptValueKind.String => "String",
        ScriptValueKind.Object => "Object",
        _ => "Unsupported",
    };

    private static string Text(JsonNode? node) =>
        node is null || node is JsonArray || node is JsonObject ? "" : node.ToString();

    private static string RequiredName(JsonNode? node, string field) =>
        node is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new FormatException($"{field} must be nonblank JSON text");
}
