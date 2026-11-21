using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; keyed by the canonical reference form key, saved as one section; an entry with nothing set is not kept.
public sealed class ActorScriptState
{
    public const string SectionName = "script.actors.json";

    private sealed class Entry
    {
        public bool CanSpeakWhenDown;
        public bool NoBleedoutRecovery;
        public Dictionary<string, bool> Perks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty => !CanSpeakWhenDown && !NoBleedoutRecovery && Perks.Count == 0;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static ActorScriptState Current { get; } = new();

    public bool IsEmpty => _entries.Count == 0;

    public bool CanSpeakWhenDown(string refKey) => Find(refKey)?.CanSpeakWhenDown == true;

    public bool NoBleedoutRecovery(string refKey) => Find(refKey)?.NoBleedoutRecovery == true;

    // Invariant: true means the script added the perk, false that it removed one the base record may list, null that the script changed nothing.
    public bool? PerkOverride(string refKey, string perk) =>
        Find(refKey) is { } entry && entry.Perks.TryGetValue(PlayerPerks.Normalise(perk), out var held) ? held : null;

    public void SetCanSpeakWhenDown(string refKey, bool value) => Change(refKey, entry => entry.CanSpeakWhenDown = value);

    public void SetNoBleedoutRecovery(string refKey, bool value) => Change(refKey, entry => entry.NoBleedoutRecovery = value);

    public void SetPerk(string refKey, string perk, bool held)
    {
        var key = PlayerPerks.Normalise(perk);
        if (key.Length == 0) throw new ArgumentException("the perk key is empty", nameof(perk));
        Change(refKey, entry => entry.Perks[key] = held);
    }

    public void Clear() => _entries.Clear();

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_entries.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray();
        foreach (var (key, entry) in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var row = new JsonObject { ["ref"] = key };
            if (entry.CanSpeakWhenDown) row["speak"] = true;
            if (entry.NoBleedoutRecovery) row["noRecovery"] = true;
            if (entry.Perks.Count > 0)
            {
                var perks = new JsonObject();
                foreach (var (perk, held) in entry.Perks.OrderBy(pair => pair.Key, StringComparer.Ordinal)) perks[perk] = held;
                row["perks"] = perks;
            }
            rows.Add(row);
        }
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(new JsonObject { ["actors"] = rows }.ToJsonString()) };
    }

    // Constraint: replaces every entry; throws FormatException on a malformed section and leaves the current entries untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var read = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var document = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("script actor state is not an object");
                foreach (var node in document["actors"] as JsonArray ?? throw new FormatException("script actor state has no list"))
                {
                    var row = node as JsonObject ?? throw new FormatException("a script actor row is not an object");
                    var key = row["ref"]?.GetValue<string>() is { Length: > 0 } text ? RecordKey.Canonical(text) : throw new FormatException("a script actor row has no ref");
                    if (read.ContainsKey(key)) throw new FormatException($"script actor {key} appears twice");
                    var entry = new Entry
                    {
                        CanSpeakWhenDown = row["speak"]?.GetValue<bool>() ?? false,
                        NoBleedoutRecovery = row["noRecovery"]?.GetValue<bool>() ?? false,
                    };
                    if (row["perks"] is { } perks)
                        foreach (var (perk, held) in perks as JsonObject ?? throw new FormatException("script actor perks are not an object"))
                        {
                            var normal = PlayerPerks.Normalise(perk);
                            if (normal.Length == 0) throw new FormatException("a script actor perk has no key");
                            entry.Perks[normal] = held?.GetValue<bool>() ?? throw new FormatException("a script actor perk has no value");
                        }
                    read[key] = entry;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("script actor state is malformed", ex); }
        }
        _entries.Clear();
        foreach (var (key, entry) in read)
            if (!entry.IsEmpty) _entries[key] = entry;
    }

    private Entry? Find(string refKey) => refKey.Length > 0 && _entries.TryGetValue(RecordKey.Canonical(refKey), out var entry) ? entry : null;

    private void Change(string refKey, Action<Entry> change)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        var key = RecordKey.Canonical(refKey);
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
        change(entry);
        if (entry.IsEmpty) _entries.Remove(key);
    }
}
