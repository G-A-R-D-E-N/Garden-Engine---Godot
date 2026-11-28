using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record SavedTimer(string Owner, string Script, int Id, double Remaining);

public sealed record SavedRegistration(string Owner, string Script, string Kind, string Filter);

public sealed record SavedReceiver(string Owner, string Script, string Sender, string Event);

public sealed record ScriptEventsSnapshot(IReadOnlyList<SavedTimer> Timers, IReadOnlyList<SavedRegistration> Registrations, IReadOnlyList<SavedReceiver> Receivers)
{
    public static ScriptEventsSnapshot Empty { get; } = new(Array.Empty<SavedTimer>(), Array.Empty<SavedRegistration>(), Array.Empty<SavedReceiver>());
}

// Invariant: a script is named by owner key plus script name; an ownerless script is not saved.
public static class ScriptEventsSave
{
    public const string SectionName = "script.events.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(ScriptTimers timers, EventRegistrations registrations, CustomEventRegistry custom) =>
        new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(Capture(timers, registrations, custom))) };

    public static ScriptEventsSnapshot? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    public static ScriptEventsSnapshot Capture(ScriptTimers timers, EventRegistrations registrations, CustomEventRegistry custom) => new(
        timers.Scheduled.Where(t => t.Instance.Owner is not null).Select(t => new SavedTimer(t.Instance.Owner!.Value.Key, t.Instance.ScriptName, t.Id, t.Remaining)).ToArray(),
        registrations.All.Where(r => r.Receiver.Owner is not null).Select(r => new SavedRegistration(r.Receiver.Owner!.Value.Key, r.Receiver.ScriptName, r.Kind, r.Filter)).ToArray(),
        custom.Receivers.Where(r => r.Receiver.Owner is not null).Select(r => new SavedReceiver(r.Receiver.Owner!.Value.Key, r.Receiver.ScriptName, r.Sender, r.Event)).ToArray());

    // Requires: main thread, after the script instances were restored; an entry with no live script is dropped and counted.
    public static int Restore(ScriptEventsSnapshot snapshot, IReadOnlyList<ScriptInstance> live, ScriptTimers timers, EventRegistrations registrations, CustomEventRegistry custom)
    {
        timers.Clear();
        registrations.Clear();
        custom.ForgetAll();
        var dropped = 0;
        ScriptInstance? Find(string owner, string script) => live.FirstOrDefault(i => !i.Detached && i.ScriptName == script && i.Owner?.Key == owner);
        foreach (var t in snapshot.Timers)
            if (Find(t.Owner, t.Script) is { } instance && timers.Start(instance, t.Remaining, t.Id)) continue; else dropped++;
        foreach (var r in snapshot.Registrations)
            if (Find(r.Owner, r.Script) is { } instance) registrations.Add(instance, r.Kind, r.Filter); else dropped++;
        foreach (var r in snapshot.Receivers)
            if (Find(r.Owner, r.Script) is { } instance) custom.Register(instance, r.Sender, r.Event); else dropped++;
        return dropped;
    }

    public static string Serialize(ScriptEventsSnapshot snapshot) => new JsonObject
    {
        ["version"] = CurrentVersion,
        ["timers"] = new JsonArray(snapshot.Timers.Select(t => (JsonNode)new JsonObject { ["owner"] = t.Owner, ["script"] = t.Script, ["id"] = t.Id, ["remaining"] = t.Remaining }).ToArray()),
        ["registrations"] = new JsonArray(snapshot.Registrations.Select(r => (JsonNode)new JsonObject { ["owner"] = r.Owner, ["script"] = r.Script, ["kind"] = r.Kind, ["filter"] = r.Filter }).ToArray()),
        ["receivers"] = new JsonArray(snapshot.Receivers.Select(r => (JsonNode)new JsonObject { ["owner"] = r.Owner, ["script"] = r.Script, ["sender"] = r.Sender, ["event"] = r.Event }).ToArray()),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });

    public static ScriptEventsSnapshot Deserialize(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject ?? throw new FormatException("the script events must be a JSON object"); }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the script events are not valid JSON", error); }
        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the script events need an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported script events version {version}");
        return new ScriptEventsSnapshot(
            SceneStateSave.Items(root, "timers").Select(n => new SavedTimer(SceneStateSave.Text(n, "owner"), SceneStateSave.Text(n, "script"), SceneStateSave.Int(n, "id"), Remaining(n))).ToArray(),
            SceneStateSave.Items(root, "registrations").Select(n => new SavedRegistration(SceneStateSave.Text(n, "owner"), SceneStateSave.Text(n, "script"), SceneStateSave.Text(n, "kind"), Filter(n))).ToArray(),
            SceneStateSave.Items(root, "receivers").Select(n => new SavedReceiver(SceneStateSave.Text(n, "owner"), SceneStateSave.Text(n, "script"), SceneStateSave.Text(n, "sender"), SceneStateSave.Text(n, "event"))).ToArray());
    }

    internal static double Remaining(JsonObject node) =>
        node["remaining"] is JsonValue value && value.TryGetValue<double>(out var seconds) && double.IsFinite(seconds) && seconds >= 0 ? seconds : throw new FormatException("a timer needs a finite non-negative 'remaining'");

    private static string Filter(JsonObject node) =>
        node["filter"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new FormatException("a registration needs a text 'filter'");
}
