using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public static class SaveInfoFile
{
    public const string Suffix = ".info.json";

    public static string FileNameFor(string id) => id + Suffix;

    public static bool IsValidId(string id) =>
        id.Length > 0 && id.IndexOfAny(new[] { '.', '/', '\\', ':' }) < 0 && !string.IsNullOrWhiteSpace(id);

    public static string Serialize(SaveSlotInfo info)
    {
        var thumbnail = LeafName(info.ThumbnailPath);
        if (!IsOwnThumbnail(info.Id, thumbnail)) thumbnail = "";
        var values = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["name"] = info.Name,
            ["character"] = info.Character,
            ["level"] = info.Level,
            ["playtimeSeconds"] = info.Playtime.TotalSeconds,
            ["location"] = info.Location,
            ["timestamp"] = info.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            ["thumbnail"] = thumbnail,
            ["kind"] = info.Kind.ToString().ToLowerInvariant(),
        };
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    public static SaveSlotInfo? TryParse(string id, string json, string directory)
    {
        if (!IsValidId(id)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var thumbnail = Text(root, "thumbnail", "");
            if (!IsOwnThumbnail(id, thumbnail)) thumbnail = "";
            var seconds = root.TryGetProperty("playtimeSeconds", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var s) && double.IsFinite(s)
                ? Math.Clamp(s, 0, 1e9)
                : 0;
            var level = root.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var n) ? Math.Max(n, 0) : 0;
            var stamp = DateTimeOffset.TryParse(Text(root, "timestamp", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t
                : DateTimeOffset.MinValue;
            var kind = Enum.TryParse<SaveKind>(Text(root, "kind", ""), true, out var k) && Enum.IsDefined(k) ? k : SaveKind.Manual;
            return new SaveSlotInfo(id, Text(root, "name", id), Text(root, "character", ""), level, TimeSpan.FromSeconds(seconds),
                Text(root, "location", ""), stamp, thumbnail.Length == 0 ? "" : directory.TrimEnd('/', '\\') + "/" + thumbnail, kind);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsOwnThumbnail(string id, string name) =>
        name.Length >= id.Length + 4 && name.StartsWith(id + ".", StringComparison.Ordinal) &&
        name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 &&
        !name.Contains("..", StringComparison.Ordinal);

    public static string LeafName(string path)
    {
        var leaf = path[(path.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
        return leaf.Trim('.').Length == 0 ? "" : leaf;
    }

    private static string Text(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
}
