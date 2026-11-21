using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

// Position runs from -1 (left edge) to 1 (right edge), 0 is straight ahead.
public readonly record struct CompassTick(double Position, string? Label, double Alpha, bool Major);

public readonly record struct CompassMarkerView(string Id, CompassMarkerKind Kind, double Position, double Alpha, bool Pinned);

public sealed class CompassModel
{
    private const double TickStep = 15;
    private const double FadeBand = 0.25;

    private static readonly string[] Names = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    private readonly List<CompassTick> _ticks = new();
    private readonly List<CompassMarkerView> _views = new();
    private readonly Dictionary<string, int> _pinSide = new();
    private readonly HashSet<string> _pinnedNow = new();
    private HashSet<string> _visible = new();
    private HashSet<string> _scratch = new();

    public CompassModel(double visibleSpanDegrees = 180, double hysteresisDegrees = 4)
    {
        if (!(visibleSpanDegrees > 0 && visibleSpanDegrees <= 360)) throw new ArgumentOutOfRangeException(nameof(visibleSpanDegrees));
        if (!(hysteresisDegrees >= 0)) throw new ArgumentOutOfRangeException(nameof(hysteresisDegrees));
        Span = visibleSpanDegrees;
        Hysteresis = hysteresisDegrees;
    }

    public double Span { get; }

    public double Hysteresis { get; }

    public IReadOnlyList<CompassTick> Ticks => _ticks;

    public IReadOnlyList<CompassMarkerView> Markers => _views;

    public static double Normalize360(double degrees)
    {
        double r = degrees % 360;
        if (r < 0) r += 360;
        return r >= 360 ? 0 : r;
    }

    public static double Delta(double bearing, double heading)
    {
        double d = (bearing - heading) % 360;
        if (d >= 180) d -= 360;
        else if (d < -180) d += 360;
        return d;
    }

    public static int WholeDegrees(double heading) => (int)Math.Round(Normalize360(heading)) % 360;

    public static string Cardinal(double heading) => Names[(int)Math.Round(Normalize360(heading) / 45, MidpointRounding.AwayFromZero) % 8];

    public void Update(double heading, IReadOnlyList<CompassMarker> markers)
    {
        if (!double.IsFinite(heading)) heading = 0;
        double half = Span / 2;
        _ticks.Clear();
        _views.Clear();

        for (double bearing = 0; bearing < 360; bearing += TickStep)
        {
            double delta = Delta(bearing, heading);
            if (Math.Abs(delta) > half) continue;
            double position = delta / half;
            bool major = bearing % 45 == 0;
            _ticks.Add(new CompassTick(position, major ? Names[(int)(bearing / 45)] : null, EdgeFade(position), major));
        }

        _scratch.Clear();
        _pinnedNow.Clear();
        foreach (var marker in markers)
        {
            if (!double.IsFinite(marker.Bearing)) continue;
            double delta = Delta(marker.Bearing, heading);
            double distance = Math.Abs(delta);
            bool inRange = _visible.Contains(marker.Id) ? distance <= half + Hysteresis : distance <= half;
            if (inRange)
            {
                _scratch.Add(marker.Id);
                _pinSide.Remove(marker.Id);
                double position = Math.Clamp(delta / half, -1, 1);
                _views.Add(new CompassMarkerView(marker.Id, marker.Kind, position, EdgeFade(position), false));
            }
            else if (marker.Pinned)
            {
                int side = delta < 0 ? -1 : 1;
                if (_pinSide.TryGetValue(marker.Id, out int previous) && distance >= 180 - Hysteresis) side = previous;
                _pinSide[marker.Id] = side;
                _pinnedNow.Add(marker.Id);
                _views.Add(new CompassMarkerView(marker.Id, marker.Kind, side, 1, true));
            }
            else
            {
                _pinSide.Remove(marker.Id);
            }
        }

        foreach (string id in _pinSide.Keys)
        {
            if (!_pinnedNow.Contains(id)) _pinSide.Remove(id);
        }

        (_visible, _scratch) = (_scratch, _visible);
    }

    private static double EdgeFade(double position) => Math.Clamp((1 - Math.Abs(position)) / FadeBand, 0, 1);
}
