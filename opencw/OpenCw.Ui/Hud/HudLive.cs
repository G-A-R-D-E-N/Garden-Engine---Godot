using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Hud;

// Requires: main thread; the caller supplies facts it actually has, and a missing fact clears the readout instead of inventing a value.
public static class HudLive
{
    public const string Hidden = "HIDDEN";
    public const string Caution = "CAUTION";
    public const string Detected = "DETECTED";

    public static (string Text, double Detection) Sneak(bool sneaking, bool seen, double awareness)
    {
        if (!sneaking) return ("", 0);
        double level = double.IsFinite(awareness) ? Math.Clamp(awareness, 0, 1) : 0;
        if (seen || level >= 1) return (Detected, 100);
        return level > 0 ? (Caution, level * 100) : (Hidden, 0);
    }

    public static void ApplySneak(HudInstruments instruments, bool sneaking, bool seen, double awareness)
    {
        var (text, detection) = Sneak(sneaking, seen, awareness);
        instruments.SetSneak(text, detection);
    }

    public static void ApplyRadiation(HudInstruments instruments, double? fraction) =>
        instruments.SetRadiation(fraction is { } value && double.IsFinite(value) ? value : 0);

    public static void ApplyHealth(HudState state, double? current, double? max)
    {
        if (current is { } now && max is { } top && double.IsFinite(now) && double.IsFinite(top) && top > 0) state.SetHealth(now, top);
        else state.ClearHealth();
    }

    public static bool ApplyEnemy(HudInstruments instruments, string? name, double? health, double? max, int? level, bool legendary = false)
    {
        if (string.IsNullOrWhiteSpace(name) || health is not { } now || max is not { } top || !double.IsFinite(now) || !double.IsFinite(top) || top <= 0 || now <= 0)
        {
            instruments.ClearEnemy();
            return false;
        }
        instruments.SetEnemy(name, now / top);
        instruments.SetEnemyDetails(level, legendary);
        return true;
    }

    public const int MaxMarkers = 8;

    public static double Bearing(double dx, double dz) => CompassModel.Normalize360(Math.Atan2(dx, -dz) * 180 / Math.PI);

    public static void ApplyMarkers(HudState state, IEnumerable<(string Id, double Dx, double Dz, CompassMarkerKind Kind)> actors)
    {
        var markers = actors
            .Where(a => double.IsFinite(a.Dx) && double.IsFinite(a.Dz))
            .OrderBy(a => a.Dx * a.Dx + a.Dz * a.Dz)
            .Take(MaxMarkers)
            .Select(a => new CompassMarker(a.Id, Bearing(a.Dx, a.Dz), a.Kind))
            .ToArray();
        if (markers.Length == 0 && state.Markers.Count == 0) return;
        state.SetMarkers(markers);
    }

    public static void ApplyEffects(HudInstruments instruments, IReadOnlyList<string> lines)
    {
        if (!instruments.Effects.SequenceEqual(lines)) instruments.SetEffects(lines);
    }
}
