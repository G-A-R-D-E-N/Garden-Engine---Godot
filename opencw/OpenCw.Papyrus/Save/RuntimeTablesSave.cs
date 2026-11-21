using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record RuntimeTables(IReadOnlyList<TimerRow> Timers, IReadOnlyList<RegistrationRow> Registrations);

public sealed record RuntimeTablesReport(int Timers, int Registrations);

public static class RuntimeTablesSave
{
    public const string SectionName = "papyrus.runtime.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(ScriptTimers timers, ReferenceAnimations animations) =>
        new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(new RuntimeTables(timers.Snapshot(), animations.RegistrationRows()))) };

    // Invariant: null means the save has no section (made before the tables were saved), and the caller leaves the live tables empty.
    public static RuntimeTables? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    public static RuntimeTablesReport Apply(RuntimeTables tables, ScriptTimers timers, ReferenceAnimations animations, IReadOnlyList<ScriptInstance> instances) =>
        new(timers.Replace(tables.Timers, instances), animations.Replace(tables.Registrations, instances));

    public static string Serialize(RuntimeTables tables)
    {
        var timers = new JsonArray();
        foreach (var row in tables.Timers)
            timers.Add(new JsonObject { ["owner"] = row.Owner, ["script"] = row.Script, ["id"] = row.Id, ["remaining"] = row.Remaining });
        var registrations = new JsonArray();
        foreach (var row in tables.Registrations)
            registrations.Add(new JsonObject { ["owner"] = row.Owner, ["script"] = row.Script, ["source"] = row.SourceKey, ["event"] = row.EventName });
        return new JsonObject { ["version"] = CurrentVersion, ["timers"] = timers, ["animationRegistrations"] = registrations }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static RuntimeTables Deserialize(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new FormatException("the runtime tables must be a JSON object");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the runtime tables are not valid JSON: {ex.Message}", ex);
        }
        if (Get(root, "version", n => n.GetValue<int>()) != CurrentVersion) throw new FormatException("the runtime tables version is not supported");
        var timers = Rows(root, "timers", ScriptTimers.Capacity, node => new TimerRow(
            Text(node, "owner"), Text(node, "script"), Get(node, "id", n => n.GetValue<int>()), Finite(Get(node, "remaining", n => n.GetValue<double>()))));
        var registrations = Rows(root, "animationRegistrations", ReferenceAnimations.RegistrationCapacity, node => new RegistrationRow(
            Text(node, "owner"), Text(node, "script"), Text(node, "source"), Text(node, "event")));
        return new RuntimeTables(timers, registrations);
    }

    private static List<T> Rows<T>(JsonObject root, string name, int capacity, Func<JsonObject, T> row)
    {
        var array = root[name] as JsonArray ?? throw new FormatException($"the runtime tables have no {name} array");
        if (array.Count > capacity) throw new FormatException($"{name} holds {array.Count} rows, the limit is {capacity}");
        return array.Select(node => row(node as JsonObject ?? throw new FormatException($"a {name} row must be an object"))).ToList();
    }

    private static string Text(JsonObject node, string name)
    {
        var value = Get(node, name, n => n.GetValue<string>());
        return string.IsNullOrEmpty(value) ? throw new FormatException($"'{name}' must not be empty") : value;
    }

    private static double Finite(double value) =>
        double.IsFinite(value) && value >= 0 ? value : throw new FormatException("a timer's remaining time must be a finite number of seconds, zero or more");

    private static T Get<T>(JsonNode node, string name, Func<JsonNode, T> read)
    {
        var value = node[name] ?? throw new FormatException($"missing '{name}'");
        try
        {
            return read(value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new FormatException($"'{name}' has the wrong type: {ex.Message}", ex);
        }
    }
}
