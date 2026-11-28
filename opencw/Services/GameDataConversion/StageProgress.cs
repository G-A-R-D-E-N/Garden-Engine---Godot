using System.Text.Json;

namespace OpenCommonwealth.Services.GameDataConversion;

public sealed record StageProgressData(long Done, long Total, long Failed, bool Running, string Scope = "all", string Note = "", string Fingerprint = "");

public static class StageProgress
{
    public static string PathFor(string root, string key) => Path.Combine(root, $".convert_stage_{key}.json");

    public static void Write(string root, string key, StageProgressData data)
    {
        Directory.CreateDirectory(root);
        var path = PathFor(root, key);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, path, true);
    }

    public static StageProgressData? Read(string root, string key)
    {
        var path = PathFor(root, key);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<StageProgressData>(fs);
        }
        catch { return null; }
    }

    public static string? FailureSummary(string root, IEnumerable<string> keys)
    {
        var failed = keys.Select(key => (key, p: Read(root, key))).Where(s => s.p is { Failed: > 0 }).ToList();
        return failed.Count == 0 ? null : string.Join("; ", failed.Select(s => $"{s.key}: {s.p!.Failed} of {s.p.Total} failed" + (s.p.Note.Length > 0 ? $" (first: {s.p.Note})" : "")));
    }

    public static bool IsComplete(StageProgressData? p) => p is { Running: false, Failed: 0, Scope: "all" } && p.Total > 0 && p.Done >= p.Total;

    // Invariant: a stage is complete for a load order only when its recorded fingerprint equals the expected one; an empty or missing value on either side is stale.
    public static bool IsComplete(StageProgressData? p, string? expected) =>
        IsComplete(p) && !string.IsNullOrEmpty(expected) && string.Equals(p!.Fingerprint, expected, StringComparison.Ordinal);
}
