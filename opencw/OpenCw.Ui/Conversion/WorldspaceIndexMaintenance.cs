using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenCommonwealth.Services;

public static class WorldspaceIndexMaintenance
{
    public const string ManifestFile = "worldspace.json";
    public const string IndexFile = "index.json";
    public const string TempJsonSuffix = ".tmp.json";

    public static void Update(string worldspacesRootAbs, string wsDirName, string formKey, string? editorId, string? name)
    {
        Directory.CreateDirectory(worldspacesRootAbs);
        var indexPath = Path.Combine(worldspacesRootAbs, IndexFile);
        var root = new JObject();
        JArray list;
        if (!File.Exists(indexPath))
        {
            list = RebuildIndexList(worldspacesRootAbs);
        }
        else if (TryReadIndex(indexPath, out var parsedRoot, out var parsedList))
        {
            root = parsedRoot;
            list = parsedList;
        }
        else
        {
            PreserveUnreadableIndex(indexPath);
            list = RebuildIndexList(worldspacesRootAbs);
        }

        var kept = new JArray();
        foreach (var e in list)
            if ((string?)e["dir"] != wsDirName) kept.Add(e);
        kept.Add(IndexEntry(wsDirName, formKey, editorId, name));
        root["worldspaces"] = kept;

        var indexStem = Path.Combine(worldspacesRootAbs, "index");
        ProcessTemp.SweepDeadInOnce(worldspacesRootAbs, TempJsonSuffix, unparsedIsLegacy: true);
        var tmp = ProcessTemp.PathFor(indexStem, TempJsonSuffix);
        File.WriteAllText(tmp, root.ToString());
        ProcessTemp.MoveWithRetry(tmp, indexPath);
    }

    private static bool TryReadIndex(string indexPath, out JObject root, out JArray list)
    {
        root = new JObject();
        list = new JArray();
        try
        {
            var parsed = JObject.Parse(File.ReadAllText(indexPath));
            var node = parsed["worldspaces"];
            if (node == null) { root = parsed; return true; }
            if (node is not JArray entries) return false;
            root = parsed;
            list = entries;
            return true;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }

    private static void PreserveUnreadableIndex(string indexPath)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var backup = $"{indexPath}.corrupt-{stamp}";
        for (int n = 2; File.Exists(backup); n++) backup = $"{indexPath}.corrupt-{stamp}-{n}";
        File.Move(indexPath, backup);
    }

    private static JArray RebuildIndexList(string worldspacesRootAbs)
    {
        var list = new JArray();
        foreach (var dirAbs in Directory.EnumerateDirectories(worldspacesRootAbs))
        {
            var dirName = Path.GetFileName(dirAbs);
            if (dirName.StartsWith('_') || dirName.StartsWith('.')) continue;
            var manifestAbs = Path.Combine(dirAbs, ManifestFile);
            if (!File.Exists(manifestAbs)) continue;
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(manifestAbs));
                var formKey = (manifest["formKey"] as JValue)?.Value<string>();
                if (string.IsNullOrEmpty(formKey)) continue;
                list.Add(IndexEntry(dirName, formKey, (manifest["editorId"] as JValue)?.Value<string>(), (manifest["name"] as JValue)?.Value<string>()));
            }
            catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or FormatException or InvalidCastException) { }
        }
        return list;
    }

    private static JObject IndexEntry(string dirName, string formKey, string? editorId, string? name) =>
        new()
        {
            ["dir"] = dirName,
            ["formKey"] = formKey,
            ["editorId"] = editorId,
            ["name"] = name,
            ["manifest"] = $"{dirName}/{ManifestFile}",
        };
}