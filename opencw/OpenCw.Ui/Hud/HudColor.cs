using System;
using System.Globalization;
using System.IO;

namespace OpenCommonwealth.Services.Ui.Hud;

public readonly record struct HudColor(double R, double G, double B)
{
    public const double Min = 0.005;
    public const double Max = 1;

    // Constraint: initial value of the iHUDColor settings in Fallout4.exe 1.11.240; evidence in vault Project/HUD colour default evidence.md, entry in CodeNotes:Garden Engine/history/docs/code-notes/ui-vanilla.md.
    public static readonly HudColor Default = new(18 / 255.0, 1.0, 21 / 255.0);

    public static HudColor FromBytes(int r, int g, int b) => new(Channel(r), Channel(g), Channel(b));

    private static double Channel(int value) => Math.Clamp(value / 255.0, Min, Max);
}

public static class HudPrefs
{
    public const string Section = "Interface";
    public const string KeyR = "iHUDColorR";
    public const string KeyG = "iHUDColorG";
    public const string KeyB = "iHUDColorB";
    public const string PathVariable = "GE_FO4_PREFS";

    public static HudColor Parse(string? iniText)
    {
        var d = HudColor.Default;
        if (string.IsNullOrEmpty(iniText)) return d;
        int? r = null, g = null, b = null;
        bool inside = false;
        foreach (var raw in iniText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[')
            {
                inside = line.TrimEnd().Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inside) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            if (!int.TryParse(line[(eq + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) continue;
            if (key.Equals(KeyR, StringComparison.OrdinalIgnoreCase)) r = value;
            else if (key.Equals(KeyG, StringComparison.OrdinalIgnoreCase)) g = value;
            else if (key.Equals(KeyB, StringComparison.OrdinalIgnoreCase)) b = value;
        }
        var parsed = HudColor.FromBytes(r ?? 0, g ?? 0, b ?? 0);
        return new HudColor(r is null ? d.R : parsed.R, g is null ? d.G : parsed.G, b is null ? d.B : parsed.B);
    }

    public static string? PrefsPath()
    {
        var overridden = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrEmpty(documents) ? null : Path.Combine(documents, "My Games", "Fallout4", "Fallout4Prefs.ini");
    }

    public static HudColor Load(string? path = null)
    {
        path ??= PrefsPath();
        try
        {
            return path is not null && File.Exists(path) ? Parse(File.ReadAllText(path)) : HudColor.Default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return HudColor.Default;
        }
    }
}
