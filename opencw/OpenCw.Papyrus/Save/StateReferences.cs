using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record SavedInstanceId(int Kind, string Base, string Discriminator, string Script)
{
    public string Name => $"{Script} on {Base}{(Discriminator.Length > 0 ? ":" + Discriminator : "")}";
}

public sealed record SavedReference(SavedInstanceId Holder, string Variable, string Type, string State, SavedInstanceId Target, bool Nested)
{
    public string Name => $"{Holder.Name}.{Variable} -> {Target.Name}{(Nested ? " (inside an array or struct)" : "")}";
}

public static class StateReferences
{
    private const int MaxDepth = 64;

    // Invariant: only an object value that names an instance of the same blob is listed; engine references and none values are not script-to-script references.
    public static IReadOnlyList<SavedReference> Scan(string blob)
    {
        if (JsonNode.Parse(blob) is not JsonObject root || root["instances"] is not JsonArray instances) return Array.Empty<SavedReference>();
        var pool = root["pool"] as JsonArray ?? new JsonArray();
        var ids = instances.Select(Identity).ToArray();
        var found = new List<SavedReference>();
        for (var index = 0; index < instances.Count; ++index)
        {
            if (ids[index] is not { } holder || instances[index]?["variables"] is not JsonArray variables) continue;
            foreach (var pair in variables)
            {
                if (pair is not JsonArray { Count: 2 } entry || entry[0] is not JsonValue name || !name.TryGetValue<string>(out var variable)) continue;
                Visit(entry[1], holder, variable, 0, ids, pool, new HashSet<int>(), found);
            }
        }
        return found;
    }

    private static void Visit(JsonNode? value, SavedInstanceId holder, string variable, int depth, SavedInstanceId?[] ids, JsonArray pool, HashSet<int> seen, List<SavedReference> found)
    {
        if (value is not JsonObject node || node["t"] is not JsonValue tag || !tag.TryGetValue<string>(out var kind)) return;
        if (kind == "o")
        {
            if (Number(node["i"]) is { } target && target >= 0 && target < ids.Length && ids[target] is { } id)
                found.Add(new SavedReference(holder, variable, Text(node["type"]), Text(node["state"]), id, depth > 0));
            return;
        }
        if (depth >= MaxDepth || kind is not ("a" or "st") || Number(node["r"]) is not { } slot || slot < 0 || slot >= pool.Count || !seen.Add(slot)) return;
        if (pool[slot] is not JsonObject entry) return;
        if (entry["e"] is JsonArray elements)
            foreach (var element in elements) Visit(element, holder, variable, depth + 1, ids, pool, seen, found);
        if (entry["m"] is JsonArray members)
            foreach (var member in members)
                if (member is JsonArray { Count: 2 } pair) Visit(pair[1], holder, variable, depth + 1, ids, pool, seen, found);
    }

    private static SavedInstanceId? Identity(JsonNode? node)
    {
        if (node?["owner"] is not JsonObject owner || Number(owner["kind"]) is not { } kind) return null;
        var script = Text(node["script"]);
        return script.Length == 0 ? null : new SavedInstanceId(kind, Text(owner["base"]), Text(owner["disc"]), script);
    }

    private static int? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
