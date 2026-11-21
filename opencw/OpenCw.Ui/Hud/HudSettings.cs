using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum HudElement
{
    Health,
    ActionPoints,
    Ammo,
    Compass,
    Crosshair,
    Objective,
    Interaction,
    Toasts,
    Subtitles,
}

public enum HudAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum HudAlign
{
    Start,
    Middle,
    End,
}

public static class HudAnchors
{
    public static HudAlign Horizontal(HudAnchor anchor) => ((int)anchor % 3) switch
    {
        0 => HudAlign.Start,
        1 => HudAlign.Middle,
        _ => HudAlign.End,
    };

    public static HudAlign Vertical(HudAnchor anchor) => ((int)anchor / 3) switch
    {
        0 => HudAlign.Start,
        1 => HudAlign.Middle,
        _ => HudAlign.End,
    };
}

public readonly record struct HudElementPlacement(HudAnchor? Anchor, double OffsetX, double OffsetY);

public sealed class HudSettings
{
    public const double MinScale = 0.5;
    public const double MaxScale = 2.0;
    public const double MaxSafeMargin = 120;
    public const double MaxOffset = 1280;

    private readonly HashSet<HudElement> _hidden = new();
    private readonly Dictionary<HudElement, HudElementPlacement> _placement = new();
    private double _scale = 1;
    private double _safeMargin;
    private HudPaletteKind _palette = HudPaletteKind.Standard;

    public int Version { get; private set; }

    public double Scale
    {
        get => _scale;
        set => Apply(ref _scale, Math.Clamp(value, MinScale, MaxScale), value);
    }

    public double SafeMargin
    {
        get => _safeMargin;
        set => Apply(ref _safeMargin, Math.Clamp(value, 0, MaxSafeMargin), value);
    }

    public HudPaletteKind Palette
    {
        get => _palette;
        set
        {
            if (_palette == value) return;
            _palette = value;
            Version++;
        }
    }

    public bool IsEnabled(HudElement element) => !_hidden.Contains(element);

    public void SetEnabled(HudElement element, bool enabled)
    {
        if (enabled ? _hidden.Remove(element) : _hidden.Add(element)) Version++;
    }

    public HudElementPlacement PlacementOf(HudElement element) =>
        _placement.TryGetValue(element, out var placement) ? placement : default;

    public void SetOffset(HudElement element, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        var current = PlacementOf(element);
        Store(element, current with { OffsetX = Math.Clamp(x, -MaxOffset, MaxOffset), OffsetY = Math.Clamp(y, -MaxOffset, MaxOffset) });
    }

    public void SetAnchor(HudElement element, HudAnchor? anchor) =>
        Store(element, PlacementOf(element) with { Anchor = anchor });

    public void ResetPlacement(HudElement element)
    {
        if (_placement.Remove(element)) Version++;
    }

    public void ResetAll()
    {
        _hidden.Clear();
        _placement.Clear();
        _scale = 1;
        _safeMargin = 0;
        _palette = HudPaletteKind.Standard;
        Version++;
    }

    private void Store(HudElement element, HudElementPlacement placement)
    {
        if (placement == PlacementOf(element)) return;
        if (placement == default) _placement.Remove(element);
        else _placement[element] = placement;
        Version++;
    }

    private void Apply(ref double field, double clamped, double requested)
    {
        if (!double.IsFinite(requested) || field == clamped) return;
        field = clamped;
        Version++;
    }
}
