using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; keyed by the bare reference form key; saved as one section.
public sealed class PlayerTeammates
{
    public const string SectionName = "script.teammates.json";

    private readonly HashSet<string> _teammates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _xpGivers = new(StringComparer.Ordinal);

    public int Count => _teammates.Count;

    public void Set(string refKey, bool teammate)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        refKey = RecordKey.Canonical(refKey);
        if (teammate) _teammates.Add(refKey);
        else
        {
            _teammates.Remove(refKey);
            _xpGivers.Remove(refKey);
        }
    }

    // Invariant: only a current teammate can give XP, so clearing the teammate flag also drops the share.
    public void SetGivesXp(string refKey, bool gives)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        refKey = RecordKey.Canonical(refKey);
        if (gives && _teammates.Contains(refKey)) _xpGivers.Add(refKey);
        else _xpGivers.Remove(refKey);
    }

    public bool GivesXp(string refKey) => refKey.Length > 0 && _xpGivers.Contains(RecordKey.Canonical(refKey));

    public bool IsTeammate(string refKey) => refKey.Length > 0 && _teammates.Contains(RecordKey.Canonical(refKey));

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_teammates.Count == 0) return new Dictionary<string, byte[]>();
        var document = new JsonObject
        {
            ["teammates"] = new JsonArray(_teammates.OrderBy(key => key, StringComparer.Ordinal).Select(key => (JsonNode)key).ToArray()),
            ["xp"] = new JsonArray(_xpGivers.OrderBy(key => key, StringComparer.Ordinal).Select(key => (JsonNode)key).ToArray()),
        };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(document.ToJsonString()) };
    }

    // Constraint: replaces both sets; throws FormatException on a malformed section and leaves the current sets untouched; an XP giver who is not a teammate is dropped.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var teammates = new HashSet<string>(StringComparer.Ordinal);
        var givers = new HashSet<string>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var document = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("teammates are not an object");
                foreach (var node in document["teammates"] as JsonArray ?? throw new FormatException("teammates have no list"))
                    teammates.Add(node?.GetValue<string>() is { Length: > 0 } key ? RecordKey.Canonical(key) : throw new FormatException("teammate has no ref"));
                if (document["xp"] is JsonArray shared)
                    foreach (var node in shared)
                        if (node?.GetValue<string>() is { Length: > 0 } key && teammates.Contains(RecordKey.Canonical(key))) givers.Add(RecordKey.Canonical(key));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("teammates are malformed", ex); }
        }
        _teammates.Clear();
        _teammates.UnionWith(teammates);
        _xpGivers.Clear();
        _xpGivers.UnionWith(givers);
    }

    public void Clear()
    {
        _teammates.Clear();
        _xpGivers.Clear();
    }
}
