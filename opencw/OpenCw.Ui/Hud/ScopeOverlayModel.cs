using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Hud;

public sealed class ScopeOverlayModel
{
    public const double FadeInSeconds = 0.15;
    public const double FadeOutSeconds = 0.1;
    public const double LensFraction = 0.9;
    public const double MinReticleScale = 0.5;
    public const double MaxReticleScale = 2;
    public const double ReachFraction = 0.7;
    public const double LabelWidthFraction = 0.6;
    public const double SoftEdgeUnits = 6;
    public const double ReferenceHeight = 1080;
    public const double DistanceMinHeight = 1000;
    public const int MaxMarks = 16;
    public const int ArcSegments = 24;
    public const int EdgeSteps = 6;

    private sealed record Norm(ScopeStyle Style, double Zoom, double ZoomMin, double ZoomMax, double Scale, ReconMark[] Recon, string Label)
    {
        public bool SameAs(Norm o) =>
            Style == o.Style && Zoom.Equals(o.Zoom) && ZoomMin.Equals(o.ZoomMin) && ZoomMax.Equals(o.ZoomMax)
            && Scale == o.Scale && Label == o.Label && Recon.SequenceEqual(o.Recon);
    }

    private Norm? _state;
    private ScopeOptions _options = new();
    private ScopeGeometry? _cache;
    private int _version;
    private int _cacheVersion = -1;
    private double _alpha;

    public bool Visible { get; private set; }

    public double Alpha => _alpha;

    public ScopeStyle Style => _state?.Style ?? ScopeStyle.Crosshair;

    public double ReticleScale => _state?.Scale ?? 1;

    public ScopeOptions Options
    {
        get => _options;
        set
        {
            value ??= new ScopeOptions();
            if (value == _options) return;
            _options = value;
            _version++;
        }
    }

    public uint ReticleRgba(HudPalette palette) => _options.Tint ?? palette.Accent;

    public uint MarkRgba(bool hostile, HudPalette palette) => hostile ? palette.Warning : ReticleRgba(palette);

    public void Sync(IScopeSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Apply(source.Current);
    }

    public void Apply(ScopeState? state)
    {
        if (state is null)
        {
            Visible = false;
            return;
        }
        var next = Normalise(state);
        if (Visible && _state is not null && next.SameAs(_state)) return;
        _state = next;
        Visible = true;
        _version++;
    }

    public void Tick(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return;
        _alpha = Visible ? Math.Min(1, _alpha + dt / FadeInSeconds) : Math.Max(0, _alpha - dt / FadeOutSeconds);
    }

    public ScopeGeometry? Layout(double width, double height)
    {
        if (_state is null || !double.IsFinite(width) || !double.IsFinite(height) || !(width > 0) || !(height > 0)) return null;
        if (_cache is not null && _cacheVersion == _version && _cache.Width == width && _cache.Height == height) return _cache;
        _cacheVersion = _version;
        _cache = Build(_state, width, height);
        return _cache;
    }

    public static string Ellipsize(string text, double maxWidth, Func<string, double> measure)
    {
        if (text.Length == 0 || measure(text) <= maxWidth) return text;
        const string tail = "...";
        for (int n = text.Length - 1; n > 0; n--)
        {
            string candidate = text[..n].TrimEnd() + tail;
            if (measure(candidate) <= maxWidth) return candidate;
        }
        return tail;
    }

    private static Norm Normalise(ScopeState state) => new(
        Enum.IsDefined(state.Style) ? state.Style : ScopeStyle.Crosshair,
        state.Zoom,
        state.ZoomMin,
        state.ZoomMax,
        double.IsNaN(state.ReticleScale) ? 1 : Math.Clamp(state.ReticleScale, MinReticleScale, MaxReticleScale),
        state.Recon?.ToArray() ?? Array.Empty<ReconMark>(),
        state.Label ?? "");

    private ScopeGeometry Build(Norm s, double width, double height)
    {
        double cx = width / 2;
        double cy = height / 2;
        double r = LensFraction * Math.Min(width, height) / 2;
        double u = height / ReferenceHeight;
        var (zoomText, marker) = ZoomReadout(s);
        return new ScopeGeometry(width, height, cx, cy, r, u, Mask(width, height, cx, cy, r), EdgeRings(r, u),
            Reticle(s, cx, cy, r, u), zoomText, marker, s.Label, LabelWidthFraction * 2 * r, Marks(s, width, height, cx, cy, r));
    }

    private static List<IReadOnlyList<ScopePoint>> Mask(double width, double height, double cx, double cy, double r)
    {
        var mask = new List<IReadOnlyList<ScopePoint>>();
        void Rect(double x0, double y0, double x1, double y1)
        {
            if (x1 - x0 > 0 && y1 - y0 > 0)
                mask.Add(new[] { new ScopePoint(x0, y0), new ScopePoint(x1, y0), new ScopePoint(x1, y1), new ScopePoint(x0, y1) });
        }

        Rect(0, 0, cx - r, height);
        Rect(cx + r, 0, width, height);
        Rect(cx - r, 0, cx + r, cy - r);
        Rect(cx - r, cy + r, cx + r, height);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sy in new[] { -1, 1 })
        {
            var corner = new List<ScopePoint> { new(cx + sx * r, cy + sy * r) };
            for (int k = 0; k <= ArcSegments; k++)
            {
                double theta = Math.PI / 2 * k / ArcSegments;
                corner.Add(new ScopePoint(cx + sx * r * Math.Cos(theta), cy + sy * r * Math.Sin(theta)));
            }
            mask.Add(corner);
        }
        return mask;
    }

    private static List<ScopeEdgeRing> EdgeRings(double r, double u)
    {
        var rings = new List<ScopeEdgeRing>();
        for (int i = 0; i < EdgeSteps; i++)
        {
            double depth = (i + 0.5) / EdgeSteps;
            rings.Add(new ScopeEdgeRing(r - (i + 0.5) * SoftEdgeUnits / EdgeSteps * u, SoftEdgeUnits / EdgeSteps * u, 1 - depth));
        }
        return rings;
    }

    private static List<ReticleShape> Reticle(Norm s, double cx, double cy, double r, double u)
    {
        var shapes = new List<ReticleShape>();
        double ru = u * s.Scale;
        double t = Math.Max(1, 2 * ru);
        double reach = ReachFraction * r;
        double gap = Math.Min(6 * ru, reach / 2);

        void Arms(double from, double to, double thickness)
        {
            shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx - to, cy, cx - from, cy, thickness));
            shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx + from, cy, cx + to, cy, thickness));
            shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx, cy - to, cx, cy - from, thickness));
            shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx, cy + from, cx, cy + to, thickness));
        }

        switch (s.Style)
        {
            case ScopeStyle.Duplex:
                double mid = Math.Max(gap, 0.35 * r);
                Arms(gap, mid, t);
                Arms(mid, reach, 2 * t);
                shapes.Add(new ReticleShape(ReticleShapeKind.Dot, cx, cy, Math.Max(1, 2 * ru), 0, 0));
                break;
            case ScopeStyle.Dot:
                shapes.Add(new ReticleShape(ReticleShapeKind.Dot, cx, cy, Math.Max(1, 2 * ru), 0, 0));
                shapes.Add(new ReticleShape(ReticleShapeKind.Ring, cx, cy, 12 * ru, 0, Math.Max(1, ru), 0.4));
                break;
            case ScopeStyle.Ladder:
                shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx, cy - 48 * ru, cx, cy + 48 * ru, t));
                for (int k = -2; k <= 2; k++)
                {
                    double half = (k == 0 ? 12 : 8) * ru;
                    double y = cy + k * 24 * ru;
                    shapes.Add(new ReticleShape(ReticleShapeKind.Line, cx - half, y, cx + half, y, t));
                }
                break;
            default:
                Arms(gap, reach, t);
                break;
        }
        return shapes;
    }

    private static (string Text, double Marker) ZoomReadout(Norm s)
    {
        static string X(double zoom) => "x" + zoom.ToString("0.0", CultureInfo.InvariantCulture);

        if (!double.IsFinite(s.Zoom) || !(s.Zoom > 0)) return ("", 0);
        bool ranged = double.IsFinite(s.ZoomMin) && double.IsFinite(s.ZoomMax) && s.ZoomMin > 0 && s.ZoomMax > s.ZoomMin;
        if (!ranged) return (X(s.Zoom), 0);
        return ($"{X(s.ZoomMin)} - {X(s.ZoomMax)}", Math.Clamp((s.Zoom - s.ZoomMin) / (s.ZoomMax - s.ZoomMin), 0, 1));
    }

    private List<ScopeMarkView> Marks(Norm s, double width, double height, double cx, double cy, double r)
    {
        var marks = new List<ScopeMarkView>();
        bool distances = _options.ShowReconDistances && height >= DistanceMinHeight;
        foreach (var mark in s.Recon.Take(MaxMarks))
        {
            if (!double.IsFinite(mark.X) || !double.IsFinite(mark.Y)) continue;
            double x = cx - r + mark.X * 2 * r;
            double y = cy - r + mark.Y * 2 * r;
            double dx = x - cx;
            double dy = y - cy;
            if (dx * dx + dy * dy > r * r) continue;
            string text = distances && mark.Distance is { } d && double.IsFinite(d) && d >= 0
                ? $"{Math.Round(d, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} m"
                : "";
            marks.Add(new ScopeMarkView(mark.Id, x, y, mark.Hostile, text));
        }
        return marks;
    }
}
