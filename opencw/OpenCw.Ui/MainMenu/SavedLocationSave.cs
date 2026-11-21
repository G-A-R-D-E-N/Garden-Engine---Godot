using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed record SavedLocation(string Scene, string Cell, double X, double Y, double Z, double Yaw);

public static class SavedLocationSave
{
    public const string SectionName = "player.location.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(SavedLocation? location)
    {
        if (location is null) return new Dictionary<string, byte[]>();
        if (!Valid(location)) throw new ArgumentException("the player location is not a scene and finite coordinates", nameof(location));
        var root = new JsonObject
        {
            ["version"] = CurrentVersion, ["scene"] = location.Scene, ["cell"] = location.Cell,
            ["x"] = location.X, ["y"] = location.Y, ["z"] = location.Z, ["yaw"] = location.Yaw,
        };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: callers treat a FormatException as a refused section; null means the save has no location section.
    public static SavedLocation? Read(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(SectionName, out var payload)) return null;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("the player location is not an object");
            if (root["version"]?.GetValue<int>() != CurrentVersion) throw new FormatException("the player location has an unknown version");
            var location = new SavedLocation(Text(root, "scene"), root["cell"]?.GetValue<string>() ?? "", Number(root, "x"), Number(root, "y"), Number(root, "z"), Number(root, "yaw"));
            return Valid(location) ? location : throw new FormatException("the player location has no scene");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new FormatException("the player location is malformed", ex);
        }
    }

    private static bool Valid(SavedLocation location) =>
        location.Scene.Length > 0 && IsFloat(location.X) && IsFloat(location.Y) && IsFloat(location.Z) && IsFloat(location.Yaw);

    private static bool IsFloat(double value) => double.IsFinite(value) && Math.Abs(value) <= float.MaxValue;

    private static string Text(JsonObject node, string name) =>
        node[name]?.GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException($"the player location needs '{name}'");

    private static double Number(JsonObject node, string name) =>
        node[name]?.GetValue<double>() is { } value && IsFloat(value) ? value : throw new FormatException($"the player location needs a finite number '{name}'");
}

public static class SaveSlotNames
{
    // Invariant: a new manual save never reuses an existing id, so it never overwrites a save.
    public static string NewManual(IEnumerable<string> existing, DateTimeOffset now)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var stem = "save-" + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var id = stem;
        for (var n = 2; taken.Contains(id); n++) id = stem + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return id;
    }
}
