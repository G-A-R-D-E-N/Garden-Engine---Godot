using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Quest;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record QuestStateEntry(string Quest, QuestState? State, int? Stage, IReadOnlyList<int> Done, bool Active = false);

public sealed record QuestStateSnapshot(IReadOnlyList<QuestStateEntry> Quests)
{
    public static QuestStateSnapshot Empty { get; } = new(Array.Empty<QuestStateEntry>());

    public bool IsEmpty => Quests.Count == 0;

    public void Validate()
    {
        foreach (var entry in Quests)
            if (entry.Stage is { } stage && !entry.Done.Contains(stage))
                throw new FormatException($"quest {entry.Quest} has stage {stage} that is not in its done set");
    }
}

public static class QuestStateSave
{
    public const string SectionName = "quest.state.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(QuestStateMachine? machine)
    {
        var snapshot = machine?.Capture() ?? QuestStateSnapshot.Empty;
        return snapshot.IsEmpty
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(snapshot)) };
    }

    public static QuestStateSnapshot Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : QuestStateSnapshot.Empty;

    public static string Serialize(QuestStateSnapshot snapshot)
    {
        var quests = new JsonArray();
        foreach (var entry in snapshot.Quests.OrderBy(entry => entry.Quest, StringComparer.Ordinal))
        {
            var node = new JsonObject { ["quest"] = entry.Quest };
            if (entry.State is { } state) node["state"] = state.ToString();
            if (entry.Stage is { } stage) node["stage"] = stage;
            node["done"] = new JsonArray(entry.Done.Order().Select(done => (JsonNode)done).ToArray());
            if (entry.Active) node["active"] = true;
            quests.Add(node);
        }
        return new JsonObject { ["version"] = CurrentVersion, ["quests"] = quests }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static QuestStateSnapshot Deserialize(string json)
    {
        JsonObject root;
        var options = new JsonDocumentOptions { AllowDuplicateProperties = false };
        try
        {
            root = JsonNode.Parse(json, documentOptions: options) as JsonObject ?? throw new FormatException("the quest state must be a JSON object");
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the quest state is not valid JSON", error); }

        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the quest state needs an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported quest state version {version}");
        if (root["quests"] is not JsonArray entries) throw new FormatException("the quest state needs a quests array");

        var quests = new List<QuestStateEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            if (node is not JsonObject entry) throw new FormatException("a quest state must be an object");
            var quest = entry["quest"] is JsonValue q && q.TryGetValue<string>(out var key) && key.Length > 0 ? key : throw new FormatException("a quest state needs a non-empty text 'quest'");
            if (!seen.Add(quest)) throw new FormatException($"quest {quest} appears twice");
            quests.Add(new QuestStateEntry(quest, State(entry, quest), Stage(entry, quest), Done(entry, quest), Active(entry, quest)));
        }
        var snapshot = new QuestStateSnapshot(quests);
        snapshot.Validate();
        return snapshot;
    }

    private static QuestState? State(JsonObject entry, string quest)
    {
        if (!entry.ContainsKey("state")) return null;
        return entry["state"] is JsonValue value && value.TryGetValue<string>(out var text)
            && Enum.TryParse<QuestState>(text, ignoreCase: false, out var state) && Enum.IsDefined(state) && state.ToString() == text
            ? state : throw new FormatException($"quest {quest} has an unknown state");
    }

    private static int? Stage(JsonObject entry, string quest)
    {
        if (!entry.ContainsKey("stage")) return null;
        return entry["stage"] is JsonValue value && value.TryGetValue<int>(out var stage)
            ? stage : throw new FormatException($"quest {quest} needs an integer stage");
    }

    private static bool Active(JsonObject entry, string quest)
    {
        if (!entry.ContainsKey("active")) return false;
        return entry["active"] is JsonValue value && value.TryGetValue<bool>(out var active)
            ? active : throw new FormatException($"quest {quest} needs a boolean active");
    }

    private static int[] Done(JsonObject entry, string quest)
    {
        if (entry["done"] is not JsonArray array) throw new FormatException($"quest {quest} needs a done array");
        var done = array.Select(node => node is JsonValue value && value.TryGetValue<int>(out var stage)
            ? stage : throw new FormatException($"quest {quest} done holds a non-integer")).ToArray();
        if (done.Distinct().Count() != done.Length) throw new FormatException($"quest {quest} lists a done stage twice");
        return done;
    }
}
