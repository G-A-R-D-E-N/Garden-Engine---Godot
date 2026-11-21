using System;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum HudPaletteKind
{
    Standard,
    ColourBlindSafe,
}

// Colours are 0xRRGGBBAA, the same packing as TextStyle.Rgba.
public sealed record HudPalette(
    uint Text,
    uint Accent,
    uint Good,
    uint Warning,
    uint Critical,
    uint Hostile,
    uint Friendly,
    uint Panel,
    uint PanelEdge,
    uint Track)
{
    public const double LowFraction = 0.5;
    public const double CriticalFraction = 0.25;

    public static HudPalette Standard { get; } = new(
        Text: 0xE8F4EEFF,
        Accent: 0x5FD7A6FF,
        Good: 0x5FD7A6FF,
        Warning: 0xF2C14EFF,
        Critical: 0xE5533DFF,
        Hostile: 0xE5533DFF,
        Friendly: 0x5FD7A6FF,
        Panel: 0x0B1411B8,
        PanelEdge: 0x5FD7A680,
        Track: 0xFFFFFF26);

    public static HudPalette ColourBlindSafe { get; } = new(
        Text: 0xFFFFFFFF,
        Accent: 0x56B4E9FF,
        Good: 0x56B4E9FF,
        Warning: 0xF0E442FF,
        Critical: 0xD55E00FF,
        Hostile: 0xD55E00FF,
        Friendly: 0x56B4E9FF,
        Panel: 0x0B1411D0,
        PanelEdge: 0xFFFFFFA0,
        Track: 0xFFFFFF30);

    public static HudPalette For(HudPaletteKind kind) =>
        kind == HudPaletteKind.ColourBlindSafe ? ColourBlindSafe : Standard;

    public uint ForFraction(double fraction)
    {
        if (!(fraction > CriticalFraction)) return Critical;
        return fraction > LowFraction ? Good : Warning;
    }

    public static double Luminance(uint rgba)
    {
        static double Channel(uint value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel((rgba >> 24) & 0xFF) + 0.7152 * Channel((rgba >> 16) & 0xFF) + 0.0722 * Channel((rgba >> 8) & 0xFF);
    }

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
