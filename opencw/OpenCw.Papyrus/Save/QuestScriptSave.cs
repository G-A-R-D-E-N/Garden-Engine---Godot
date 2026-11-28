using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record QuestScriptProperty(string Name, string Type, object Value);

public sealed record QuestScriptInstance(string Script, IReadOnlyList<QuestScriptProperty> Properties);

public sealed record QuestScriptEntry(string Quest, IReadOnlyList<QuestScriptInstance> Scripts);

public sealed record QuestScriptSnapshot(IReadOnlyList<QuestScriptEntry> Quests)
{
    public static QuestScriptSnapshot Empty { get; } = new(Array.Empty<QuestScriptEntry>());

    public bool IsEmpty => Quests.Count == 0;
}

public sealed record QuestScriptRestoreReport(int Written, int Reset, int DroppedQuests, int DroppedScripts, int DroppedProperties, int NotInSave)
{
    public string? ToLine() => Written + Reset + DroppedQuests + DroppedScripts + DroppedProperties + NotInSave == 0 ? null
        : $"quest scripts: wrote {Written} propert(ies), reset {Reset} to initial values, dropped {DroppedQuests} quest(s), {DroppedScripts} script(s) and {DroppedProperties} propert(ies) not in this session, {NotInSave} started quest(s) not in the save";
}

public static class QuestScriptSave
{
    public const string SectionName = "quest.scripts.json";
    public const int CurrentVersion = 1;
    public const string UndoFailureKey = "scriptUndo";

    private static readonly string[] Types = { "Int", "Float", "Bool", "String" };

    public static IReadOnlyDictionary<string, byte[]> Sections(QuestRuntime? quests, IScriptHost? host)
    {
        var snapshot = Capture(quests, host);
        return snapshot.IsEmpty
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(snapshot)) };
    }

    // Constraint: every started quest is recorded (restore validates against it and resets properties the save does not mention); scalar properties are read only for lifecycle-started quests, since the direct path carries object handles and arrays that have no save form.
    public static QuestScriptSnapshot Capture(QuestRuntime? quests, IScriptHost? host)
    {
        if (quests is null) return QuestScriptSnapshot.Empty;
        var radicle = host as RadicleScriptHost;
        var entries = new List<QuestScriptEntry>();
        foreach (var quest in quests.Started.Order(StringComparer.Ordinal))
        {
            var scripts = new List<QuestScriptInstance>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (radicle is not null && quests.IsLifecycle(quest))
            {
                foreach (var instance in quests.InstancesFor(quest))
                {
                    if (instance.Detached || !seen.Add(instance.ScriptName)) continue;
                    var properties = radicle.ReadScalarProperties(instance)
                        .Select(property => new QuestScriptProperty(property.Name, property.Type, property.Value)).ToArray();
                    if (properties.Length > 0) scripts.Add(new QuestScriptInstance(instance.ScriptName, properties));
                }
            }
            entries.Add(new QuestScriptEntry(quest, scripts));
        }
        return new QuestScriptSnapshot(entries);
    }

    // Invariant: nothing is written until every entry is checked against the live session; unknown quests, scripts and properties are counted and skipped, a declared property the save does not mention is reset to its initial value, and a failed write puts the earlier writes back before the exception leaves.
    public static QuestScriptRestoreReport Restore(QuestScriptSnapshot snapshot, QuestRuntime quests, IScriptHost host)
    {
        var radicle = host as RadicleScriptHost;
        var plan = new List<(ScriptInstance Instance, string Name, object Value, object Undo)>();
        int written = 0, droppedQuests = 0, droppedScripts = 0, droppedProperties = 0;
        var saved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Quests)
        {
            saved.Add(entry.Quest);
            if (!quests.Started.Contains(entry.Quest))
            {
                droppedQuests++;
                continue;
            }
            if (radicle is null || !quests.IsLifecycle(entry.Quest))
            {
                droppedScripts += entry.Scripts.Count;
                droppedProperties += entry.Scripts.Sum(script => script.Properties.Count);
                continue;
            }
            var live = quests.InstancesFor(entry.Quest).Where(candidate => !candidate.Detached).ToArray();
            var mentioned = new HashSet<(ScriptInstance, string)>();
            foreach (var script in entry.Scripts)
            {
                var instance = live.FirstOrDefault(candidate => string.Equals(candidate.ScriptName, script.Script, StringComparison.OrdinalIgnoreCase));
                if (instance is null)
                {
                    droppedScripts++;
                    droppedProperties += script.Properties.Count;
                    continue;
                }
                var declared = radicle.DeclaredScalarProperties(instance);
                foreach (var property in script.Properties)
                {
                    var match = declared.FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                    if (match is not null) mentioned.Add((instance, match.Name));
                    if (match is null || match.Type != property.Type) droppedProperties++;
                    else
                    {
                        plan.Add((instance, match.Name, property.Value, Current(radicle, instance, match)));
                        written++;
                    }
                }
            }
            foreach (var instance in live)
                foreach (var declared in radicle.DeclaredScalarProperties(instance))
                    if (!mentioned.Contains((instance, declared.Name)))
                        plan.Add((instance, declared.Name, declared.Default, Current(radicle, instance, declared)));
        }
        var done = 0;
        try
        {
            foreach (var (instance, name, value, _) in plan)
            {
                radicle!.WriteScalarProperty(instance, name, value);
                done++;
            }
        }
        catch (Exception error)
        {
            var undoFailures = new List<string>();
            for (var index = done - 1; index >= 0; --index)
            {
                try { radicle!.WriteScalarProperty(plan[index].Instance, plan[index].Name, plan[index].Undo); }
                catch (Exception undo) { undoFailures.Add($"{plan[index].Instance.ScriptName}.{plan[index].Name}: {undo.Message}"); }
            }
            if (undoFailures.Count > 0) error.Data[UndoFailureKey] = string.Join("; ", undoFailures);
            throw;
        }
        return new QuestScriptRestoreReport(written, plan.Count - written, droppedQuests, droppedScripts, droppedProperties,
            quests.Started.Count(quest => !saved.Contains(quest)));
    }

    private static object Current(RadicleScriptHost host, ScriptInstance instance, HostScalarDeclaration declared) =>
        host.TryReadScalarProperty(instance, declared.Name, out var value) ? value : declared.Default;

    public static QuestScriptSnapshot Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : QuestScriptSnapshot.Empty;

    public static string Serialize(QuestScriptSnapshot snapshot)
    {
        var quests = new JsonArray();
        foreach (var entry in snapshot.Quests.OrderBy(entry => entry.Quest, StringComparer.Ordinal))
        {
            var scripts = new JsonArray();
            foreach (var script in entry.Scripts.OrderBy(script => script.Script, StringComparer.Ordinal))
            {
                var properties = new JsonArray();
                foreach (var property in script.Properties.OrderBy(property => property.Name, StringComparer.Ordinal))
                    properties.Add(new JsonObject { ["name"] = property.Name, ["type"] = property.Type, ["value"] = Json(property) });
                scripts.Add(new JsonObject { ["script"] = script.Script, ["properties"] = properties });
            }
            quests.Add(new JsonObject { ["quest"] = entry.Quest, ["scripts"] = scripts });
        }
        return new JsonObject { ["version"] = CurrentVersion, ["quests"] = quests }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static QuestScriptSnapshot Deserialize(string json)
    {
        JsonObject root;
        var options = new JsonDocumentOptions { AllowDuplicateProperties = false };
        try
        {
            root = JsonNode.Parse(json, documentOptions: options) as JsonObject ?? throw new FormatException("the quest script state must be a JSON object");
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw new FormatException("the quest script state is not valid JSON", error); }

        var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : throw new FormatException("the quest script state needs an integer version");
        if (version != CurrentVersion) throw new FormatException($"unsupported quest script state version {version}");
        if (root["quests"] is not JsonArray entries) throw new FormatException("the quest script state needs a quests array");

        var quests = new List<QuestScriptEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            if (node is not JsonObject entry) throw new FormatException("a quest script state must be an object");
            var quest = PapyrusStateSave.Text(entry, "quest") ?? throw new FormatException("a quest script state needs a non-empty text 'quest'");
            if (!seen.Add(quest)) throw new FormatException($"quest {quest} appears twice");
            quests.Add(new QuestScriptEntry(quest, Scripts(entry, quest)));
        }
        return new QuestScriptSnapshot(quests);
    }

    private static List<QuestScriptInstance> Scripts(JsonObject entry, string quest)
    {
        if (entry["scripts"] is not JsonArray array) throw new FormatException($"quest {quest} needs a scripts array");
        var scripts = new List<QuestScriptInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in array)
        {
            if (node is not JsonObject script) throw new FormatException($"quest {quest} holds a script that is not an object");
            var name = PapyrusStateSave.Text(script, "script") ?? throw new FormatException($"quest {quest} has a script without a non-empty text 'script'");
            if (!seen.Add(name)) throw new FormatException($"quest {quest} lists script {name} twice");
            scripts.Add(new QuestScriptInstance(name, Properties(script, quest, name)));
        }
        return scripts;
    }

    private static List<QuestScriptProperty> Properties(JsonObject script, string quest, string name)
    {
        if (script["properties"] is not JsonArray array) throw new FormatException($"quest {quest} script {name} needs a properties array");
        var properties = new List<QuestScriptProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in array)
        {
            if (node is not JsonObject property) throw new FormatException($"quest {quest} script {name} holds a property that is not an object");
            var key = PapyrusStateSave.Text(property, "name") ?? throw new FormatException($"quest {quest} script {name} has a property without a non-empty text 'name'");
            if (!seen.Add(key)) throw new FormatException($"quest {quest} script {name} lists property {key} twice");
            var type = PapyrusStateSave.Text(property, "type");
            if (type is null || !Types.Contains(type)) throw new FormatException($"quest {quest} script {name} property {key} has an unknown type");
            properties.Add(new QuestScriptProperty(key, type, Value(property["value"], type, $"quest {quest} script {name} property {key}")));
        }
        return properties;
    }

    private static object Value(JsonNode? node, string type, string where)
    {
        if (node is JsonValue value)
        {
            switch (type)
            {
                case "Int" when value.TryGetValue<int>(out var integer): return integer;
                case "Float" when value.TryGetValue<double>(out var real) && double.IsFinite(real) && float.IsFinite((float)real): return (float)real;
                case "Bool" when value.TryGetValue<bool>(out var flag): return flag;
                case "String" when value.TryGetValue<string>(out var text): return text;
            }
        }
        throw new FormatException($"{where} has a value that is not a {type}");
    }

    private static JsonNode Json(QuestScriptProperty property) => property.Value switch
    {
        int integer when property.Type == "Int" => integer,
        float real when property.Type == "Float" && float.IsFinite(real) => real,
        bool flag when property.Type == "Bool" => flag,
        string text when property.Type == "String" => text,
        _ => throw new FormatException($"property {property.Name} does not hold a {property.Type}"),
    };
}
