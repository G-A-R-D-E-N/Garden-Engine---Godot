using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Events;

namespace OpenCommonwealth.Services.Papyrus.Save;

public static class AnimationTracksSave
{
    public const string SectionName = "papyrus.animation.tracks.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(ReferenceAnimations animations)
    {
        var rows = animations.TrackRows();
        return rows.Count == 0
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(Serialize(rows)) };
    }

    // Invariant: null means the save has no section (no running track, or made before tracks were saved).
    public static IReadOnlyList<TrackRow>? Read(IReadOnlyDictionary<string, byte[]> sections) =>
        sections.TryGetValue(SectionName, out var payload) ? Deserialize(Encoding.UTF8.GetString(payload)) : null;

    public static string Serialize(IReadOnlyList<TrackRow> rows)
    {
        var tracks = new JsonArray();
        foreach (var row in rows) tracks.Add(new JsonObject { ["ref"] = row.RefKey, ["animation"] = row.Animation, ["time"] = row.Time });
        return new JsonObject { ["version"] = CurrentVersion, ["tracks"] = tracks }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    public static IReadOnlyList<TrackRow> Deserialize(string json)
    {
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new FormatException("the animation tracks must be a JSON object");
            if ((root["version"] ?? throw new FormatException("missing 'version'")).GetValue<int>() != CurrentVersion)
                throw new FormatException("the animation tracks version is not supported");
            var array = root["tracks"] as JsonArray ?? throw new FormatException("the animation tracks have no tracks array");
            if (array.Count > ReferenceAnimations.Capacity) throw new FormatException($"tracks holds {array.Count} rows, the limit is {ReferenceAnimations.Capacity}");
            var rows = new List<TrackRow>();
            foreach (var node in array)
            {
                var row = node as JsonObject ?? throw new FormatException("a track row must be an object");
                var time = (row["time"] ?? throw new FormatException("missing 'time'")).GetValue<double>();
                if (!double.IsFinite(time) || time < 0) throw new FormatException("a track's time must be a finite number of seconds, zero or more");
                rows.Add(new TrackRow(Text(row, "ref"), Text(row, "animation"), time));
            }
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new FormatException($"the animation tracks are malformed: {ex.Message}", ex);
        }
    }

    private static string Text(JsonObject row, string name) =>
        (row[name] ?? throw new FormatException($"missing '{name}'")).GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException($"'{name}' must not be empty");
}
