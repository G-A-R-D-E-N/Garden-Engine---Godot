using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Launcher;

internal static class LauncherSettings
{
    public static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenCommonwealth", "settings.json");

    public static string? SetFallout4Path(string dir) => Set("Fallout4Path", dir);

    public static string? Get(string key)
    {
        try { return JsonNode.Parse(File.ReadAllText(Path))?[key]?.GetValue<string>(); }
        catch { return null; }
    }

    public static string? Set(string key, string value)
    {
        try
        {
            string file = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);

            JsonObject root;
            if (File.Exists(file))
            {
                try { root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject(); }
                catch { root = new JsonObject(); } // corrupt/hand-edited file: start clean rather than throw
            }
            else root = new JsonObject();

            root[key] = value;

            string tmp = file + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, file, overwrite: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
