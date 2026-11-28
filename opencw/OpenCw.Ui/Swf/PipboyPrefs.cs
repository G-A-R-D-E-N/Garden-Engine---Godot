using System.Globalization;
using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class PipboyPrefs
{
    public const string Section = "Pipboy";
    public const string KeyR = "fPipboyEffectColorR";
    public const string KeyG = "fPipboyEffectColorG";
    public const string KeyB = "fPipboyEffectColorB";

    public static readonly HudColor Default = new(0.08, 1.0, 0.09);

    public static HudColor Parse(string? iniText)
    {
        if (string.IsNullOrEmpty(iniText))
            return Default;
        double? r = null, g = null, b = null;
        var inside = false;
        foreach (var raw in iniText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;
            if (line[0] == '[')
            {
                inside = line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (!inside || eq <= 0 || !double.TryParse(line[(eq + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                continue;
            var key = line[..eq].Trim();
            if (key.Equals(KeyR, StringComparison.OrdinalIgnoreCase)) r = value;
            else if (key.Equals(KeyG, StringComparison.OrdinalIgnoreCase)) g = value;
            else if (key.Equals(KeyB, StringComparison.OrdinalIgnoreCase)) b = value;
        }
        return new HudColor(Channel(r, Default.R), Channel(g, Default.G), Channel(b, Default.B));
    }

    public static HudColor Load(string? path = null)
    {
        path ??= HudPrefs.PrefsPath();
        try
        {
            return path is not null && File.Exists(path) ? Parse(File.ReadAllText(path)) : Default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    private static double Channel(double? value, double fallback) => value is { } v ? Math.Clamp(v, HudColor.Min, HudColor.Max) : fallback;
}
