using System;
using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Menus;

public enum MenuTone
{
    Text,
    Accent,
    Good,
    Bad,
    Muted,
    PanelFill,
    PanelBorder,
    Selection,
}

// Requires: main thread only.
public static class MenuAccessibility
{
    public const double MinScale = 0.5;
    public const double MaxScale = 3;

    private static double _scale = 1;
    private static bool _highContrast;

    public static event Action? Changed;

    public static double TextScale
    {
        get => _scale;
        set
        {
            var next = double.IsFinite(value) ? Math.Clamp(value, MinScale, MaxScale) : 1;
            if (next == _scale) return;
            _scale = next;
            Changed?.Invoke();
        }
    }

    public static bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (value == _highContrast) return;
            _highContrast = value;
            Changed?.Invoke();
        }
    }

    public static int Size(int basePx) => Math.Max(1, (int)Math.Round(basePx * _scale, MidpointRounding.AwayFromZero));

    public static int Size(int basePx, int maxPx) => Math.Min(Size(basePx), Math.Max(basePx, maxPx));

    public static uint HighContrastRgba(MenuTone tone) => tone switch
    {
        MenuTone.Text or MenuTone.Muted => 0xFFFFFFFF,
        MenuTone.Accent => HudPalette.ColourBlindSafe.Accent,
        MenuTone.Good => HudPalette.ColourBlindSafe.Good,
        MenuTone.Bad => HudPalette.ColourBlindSafe.Critical,
        MenuTone.PanelFill => 0x000000F5,
        MenuTone.PanelBorder => 0xFFFFFFFF,
        _ => 0x0072B2FF,
    };

    public static uint ChoiceBand(int band) => band switch
    {
        <= 0 => 0xFFFFFFFF,
        1 => HudPalette.ColourBlindSafe.Good,
        2 => HudPalette.ColourBlindSafe.Warning,
        3 => 0xE69F00FF,
        _ => HudPalette.ColourBlindSafe.Critical,
    };
}
