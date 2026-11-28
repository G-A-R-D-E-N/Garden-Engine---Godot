using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

// Invariant: a game-time timer is named by owner key, script name and id; Remaining is in game hours and an ownerless script is not saved.
public static class GameTimersSave
{
    public const string SectionName = "script.gametimers.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(ScriptTimers gameTimers) =>
        new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(Capture(gameTimers))) };

    // Requires: a save without the section answers null and leaves the running timers alone.
    public static IReadOnlyList<SavedTimer>? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    public static IReadOnlyList<SavedTimer> Capture(ScriptTimers gameTimers) =>
        gameTimers.Scheduled.Where(t => t.Instance.Owner is not null).Select(t => new SavedTimer(t.Instance.Owner!.Value.Key, t.Instance.ScriptName, t.Id, t.Remaining)).GroupBy(t => (t.Owner, t.Script, t.Id)).Select(group => group.Last()).ToArray();

    // Requires: main thread, after the script instances were restored; the table is replaced and an entry with no live script is dropped and counted.
    public static int Restore(IReadOnlyList<SavedTimer> saved, IReadOnlyList<ScriptInstance> live, ScriptTimers gameTimers)
    {
        gameTimers.Clear();
        var dropped = 0;
        foreach (var timer in saved)
            if (live.FirstOrDefault(i => !i.Detached && i.ScriptName == timer.Script && i.Owner?.Key == timer.Owner) is { } instance && gameTimers.Start(instance, timer.Remaining, timer.Id)) continue; else dropped++;
        return dropped;
    }

    public static string Serialize(IReadOnlyList<SavedTimer> timers) => new JsonObject
    {
        ["version"] = CurrentVersion,
        ["timers"] = new JsonArray(timers.Select(t => (JsonNode)new JsonObject { ["owner"] = t.Owner, ["script"] = t.Script, ["id"] = t.Id, ["remaining"] = t.Remaining }).ToArray()),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });

    // Requires: throws FormatException on malformed, duplicate or oversized data and touches nothing.
    public static IReadOnlyList<SavedTimer> Deserialize(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject ?? throw new FormatException("the game timers must be a JSON object"); }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the game timers are not valid JSON", error); }
        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the game timers need an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported game timers version {version}");
        var timers = SceneStateSave.Items(root, "timers").Select(n => new SavedTimer(SceneStateSave.Text(n, "owner"), SceneStateSave.Text(n, "script"), SceneStateSave.Int(n, "id"), ScriptEventsSave.Remaining(n))).ToArray();
        if (timers.Length > ScriptTimers.Capacity) throw new FormatException($"the game timers list {timers.Length} timers, more than the {ScriptTimers.Capacity} the table holds");
        if (timers.GroupBy(t => (t.Owner, t.Script, t.Id)).Any(group => group.Count() > 1)) throw new FormatException("a game timer appears twice");
        return timers;
    }
}
