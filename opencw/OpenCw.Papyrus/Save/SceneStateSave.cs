using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Scenes;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record SceneStateSnapshot(IReadOnlyList<SceneRunState> Runs, IReadOnlyList<ForcedAlias> Aliases, IReadOnlyList<AliasCollection>? Collections = null)
{
    public static SceneStateSnapshot Empty { get; } = new(Array.Empty<SceneRunState>(), Array.Empty<ForcedAlias>());

    public IReadOnlyList<AliasCollection> CollectionList => Collections ?? Array.Empty<AliasCollection>();

    public bool IsEmpty => Runs.Count == 0 && Aliases.Count == 0 && CollectionList.Count == 0;
}

// Invariant: Restore is the one load sequence; the empty snapshot clears runs and forced aliases.
public static class SceneStateSave
{
    public const string SectionName = "scene.state.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(IEnumerable<SceneRunState> runs, QuestAliasResolver? aliases)
    {
        var snapshot = new SceneStateSnapshot(runs.DistinctBy(run => run.Scene).ToArray(), aliases?.Capture() ?? Array.Empty<ForcedAlias>(), aliases?.CaptureCollections());
        return snapshot.IsEmpty
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(snapshot)) };
    }

    public static SceneStateSnapshot Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : SceneStateSnapshot.Empty;

    // Requires: called after the quest stages were restored, on the main thread, outside a native callback.
    public static void Restore(SceneStateSnapshot snapshot, SceneRuntime? scenes, QuestAliasResolver? aliases, SceneResumeQueue? resumes)
    {
        scenes?.StopAll();
        scenes?.ForgetReports();
        resumes?.Clear();
        aliases?.ClearAll();
        aliases?.Restore(snapshot.Aliases, snapshot.CollectionList);
        resumes?.Enqueue(snapshot.Runs);
    }

    public static string Serialize(SceneStateSnapshot snapshot)
    {
        var runs = new JsonArray(snapshot.Runs.OrderBy(run => run.Scene, StringComparer.Ordinal).Select(run => (JsonNode)new JsonObject { ["scene"] = run.Scene, ["phase"] = run.Phase }).ToArray());
        var aliases = new JsonArray(snapshot.Aliases.Select(alias =>
        {
            var node = new JsonObject { ["quest"] = alias.Quest, ["alias"] = alias.Alias };
            if (alias.RefKey is not null) node["ref"] = alias.RefKey;
            return (JsonNode)node;
        }).ToArray());
        var result = new JsonObject { ["version"] = CurrentVersion, ["runs"] = runs, ["aliases"] = aliases };
        if (snapshot.CollectionList.Count > 0)
            result["collections"] = new JsonArray(snapshot.CollectionList.Select(collection => (JsonNode)new JsonObject
            {
                ["quest"] = collection.Quest, ["alias"] = collection.Alias, ["refs"] = new JsonArray(collection.Refs.Select(key => (JsonNode)JsonValue.Create(key)!).ToArray()),
            }).ToArray());
        return result
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static SceneStateSnapshot Deserialize(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject ?? throw new FormatException("the scene state must be a JSON object"); }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the scene state is not valid JSON", error); }
        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the scene state needs an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported scene state version {version}");
        var runs = new List<SceneRunState>();
        foreach (var node in Items(root, "runs"))
        {
            var scene = Text(node, "scene");
            var phase = Int(node, "phase");
            if (phase < -1) throw new FormatException($"scene {scene} has phase {phase}");
            if (runs.Any(run => run.Scene == scene)) throw new FormatException($"scene {scene} appears twice");
            runs.Add(new SceneRunState(scene, phase));
        }
        var aliases = new List<ForcedAlias>();
        foreach (var node in Items(root, "aliases"))
        {
            var quest = Text(node, "quest");
            var alias = Int(node, "alias");
            if (aliases.Any(entry => entry.Quest == quest && entry.Alias == alias)) throw new FormatException($"alias {alias} of quest {quest} appears twice");
            aliases.Add(new ForcedAlias(quest, alias, node.ContainsKey("ref") ? Text(node, "ref") : null));
        }
        var collections = new List<AliasCollection>();
        if (root.ContainsKey("collections"))
            foreach (var node in Items(root, "collections"))
            {
                var quest = Text(node, "quest");
                var alias = Int(node, "alias");
                if (collections.Any(entry => entry.Quest == quest && entry.Alias == alias)) throw new FormatException($"collection {alias} of quest {quest} appears twice");
                var refs = (node["refs"] as JsonArray ?? throw new FormatException("a collection needs a refs array"))
                    .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : throw new FormatException("a collection ref must be non-empty text")).ToArray();
                if (refs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != refs.Length) throw new FormatException($"collection {alias} of quest {quest} lists a reference twice");
                collections.Add(new AliasCollection(quest, alias, refs));
            }
        return new SceneStateSnapshot(runs, aliases, collections);
    }

    internal static IEnumerable<JsonObject> Items(JsonObject root, string key)
    {
        if (root[key] is not JsonArray array) throw new FormatException($"the scene state needs a {key} array");
        return array.Select(node => node as JsonObject ?? throw new FormatException($"a {key} entry must be an object")).ToArray();
    }

    internal static string Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : throw new FormatException($"a scene state entry needs a non-empty text '{key}'");

    internal static int Int(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new FormatException($"a scene state entry needs an integer '{key}'");
}
