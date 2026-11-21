using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record PapyrusStateSnapshot(string Blob, string Digest, IReadOnlyList<HostEngineHandle> Engine, IReadOnlyList<PapyrusStateSnapshot>? Pending = null);

public sealed record PapyrusStateRestoreReport(int Instances, int Variables, int SkippedInstances, int SkippedVariables, int UnboundReferences, string? Refused)
{
    public string ToLine() => Refused is not null
        ? $"papyrus state: refused, instance state left as started: {Refused}"
        : $"papyrus state: restored {Variables} variable(s) on {Instances} instance(s), skipped {SkippedInstances} instance(s) and {SkippedVariables} variable(s) not in this session, {UnboundReferences} reference(s) left unbound";
}

public static class PapyrusStateSave
{
    public const string SectionName = "papyrus.state.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(IScriptHost? host)
    {
        if (host is not RadicleScriptHost radicle) return new Dictionary<string, byte[]>();
        var capture = radicle.CaptureState();
        var pending = PendingOf(radicle);
        if (pending.Count == 0 && JsonNode.Parse(capture.Blob)?["instances"] is not JsonArray { Count: > 0 }) return new Dictionary<string, byte[]>();
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(new PapyrusStateSnapshot(capture.Blob, capture.Digest, capture.Engine, pending))) };
    }

    // Invariant: the pending states are the blobs of earlier loads whose instances had not attached yet; carrying them keeps those instances' saved values through a save made before they attach.
    public static IReadOnlyList<PapyrusStateSnapshot> PendingOf(RadicleScriptHost radicle) =>
        radicle.PendingStates().Select(state => new PapyrusStateSnapshot(state.Blob, Sha256Hex.OfText(state.Blob), state.Engine)).ToList();

    public static PapyrusStateSnapshot? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    // Invariant: a Radicle refusal leaves the instances as they were, so it is reported and the caller carries on; a destroyed host or a callback in progress still throws.
    public static PapyrusStateRestoreReport Restore(PapyrusStateSnapshot snapshot, IScriptHost host)
    {
        if (host is not RadicleScriptHost radicle) return new PapyrusStateRestoreReport(0, 0, 0, 0, 0, "the session has no Radicle host");
        try
        {
            var report = radicle.RestoreState(snapshot.Blob, snapshot.Engine);
            foreach (var pending in snapshot.Pending ?? Array.Empty<PapyrusStateSnapshot>()) radicle.RetainState(pending.Blob, pending.Engine);
            return new PapyrusStateRestoreReport(report.Instances, report.Variables, report.SkippedInstances, report.SkippedVariables, report.UnboundReferences, null);
        }
        catch (Radicle.Interop.RadicleException refusal)
        {
            return new PapyrusStateRestoreReport(0, 0, 0, 0, 0, refusal.Message);
        }
    }

    public static string Serialize(PapyrusStateSnapshot snapshot)
    {
        var node = Node(snapshot);
        if (snapshot.Pending is { Count: > 0 } pending)
            node["pending"] = new JsonArray(pending.Select(entry => (JsonNode)Node(entry)).ToArray());
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    private static JsonObject Node(PapyrusStateSnapshot snapshot)
    {
        var engine = new JsonArray();
        foreach (var handle in snapshot.Engine.OrderBy(handle => handle.Handle))
        {
            var node = new JsonObject { ["handle"] = handle.Handle.ToString(CultureInfo.InvariantCulture), ["identity"] = handle.Identity };
            if (handle.Owner is { } owner)
                node["owner"] = new JsonObject { ["kind"] = owner.Kind.ToString(), ["base"] = owner.BaseFormKey, ["disc"] = owner.Discriminator };
            engine.Add(node);
        }
        return new JsonObject { ["version"] = CurrentVersion, ["digest"] = snapshot.Digest, ["blob"] = snapshot.Blob, ["engine"] = engine };
    }

    public static PapyrusStateSnapshot Deserialize(string json)
    {
        JsonObject root;
        var options = new JsonDocumentOptions { AllowDuplicateProperties = false };
        try
        {
            root = JsonNode.Parse(json, documentOptions: options) as JsonObject ?? throw new FormatException("the papyrus state must be a JSON object");
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the papyrus state is not valid JSON", error); }

        var snapshot = Entry(root);
        if (root["pending"] is not { } pendingNode) return snapshot;
        if (pendingNode is not JsonArray pendingEntries) throw new FormatException("the papyrus state's 'pending' must be an array");
        if (pendingEntries.Count > RadicleScriptHost.MaxRetainedStates) throw new FormatException($"the papyrus state holds {pendingEntries.Count} pending states, the limit is {RadicleScriptHost.MaxRetainedStates}");
        return snapshot with { Pending = pendingEntries.Select(entry => Entry(entry as JsonObject ?? throw new FormatException("a pending state must be an object"))).ToList() };
    }

    private static PapyrusStateSnapshot Entry(JsonObject root)
    {
        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the papyrus state needs an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported papyrus state version {version}");
        var blob = Text(root, "blob") ?? throw new FormatException("the papyrus state needs a non-empty text 'blob'");
        var digest = Text(root, "digest") ?? throw new FormatException("the papyrus state needs a non-empty text 'digest'");
        if (digest != Sha256Hex.OfText(blob)) throw new FormatException("the papyrus state does not match its digest");
        if (root["engine"] is not JsonArray entries) throw new FormatException("the papyrus state needs an engine array");

        var engine = new List<HostEngineHandle>();
        var seen = new HashSet<ulong>();
        foreach (var node in entries)
        {
            if (node is not JsonObject entry) throw new FormatException("an engine handle entry must be an object");
            var handle = Text(entry, "handle") is { } text && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : throw new FormatException("an engine handle entry needs a numeric text 'handle'");
            if (!seen.Add(handle)) throw new FormatException($"engine handle {handle} appears twice");
            var identity = Text(entry, "identity") ?? throw new FormatException($"engine handle {handle} needs a non-empty text 'identity'");
            engine.Add(new HostEngineHandle(handle, identity, Owner(entry, handle)));
        }
        return new PapyrusStateSnapshot(blob, digest, engine);
    }

    private static PapyrusOwnerId? Owner(JsonObject entry, ulong handle)
    {
        if (!entry.ContainsKey("owner")) return null;
        if (entry["owner"] is not JsonObject owner) throw new FormatException($"engine handle {handle} has an owner that is not an object");
        var kind = Text(owner, "kind");
        if (kind is null || !Enum.TryParse<PapyrusOwnerKind>(kind, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != kind)
            throw new FormatException($"engine handle {handle} has an unknown owner kind");
        var baseKey = Text(owner, "base") ?? throw new FormatException($"engine handle {handle} has an owner without a 'base'");
        var discriminator = owner["disc"] is JsonValue value && value.TryGetValue<string>(out var disc) ? disc : throw new FormatException($"engine handle {handle} has an owner without a 'disc'");
        var id = PapyrusOwnerId.From(parsed, baseKey, discriminator);
        return id.Key == Text(entry, "identity") ? id : throw new FormatException($"engine handle {handle} has an owner that does not match its identity");
    }

    internal static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
