using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum ScopeStyle
{
    Crosshair,
    Duplex,
    Dot,
    Ladder,
}

public readonly record struct ReconMark(string Id, double X, double Y, double? Distance = null, bool Hostile = false);

public sealed record ScopeState(
    ScopeStyle Style,
    double Zoom,
    double ZoomMin = 0,
    double ZoomMax = 0,
    double ReticleScale = 1,
    IReadOnlyList<ReconMark>? Recon = null,
    string Label = "");

public sealed record ScopeOptions(uint? Tint = null, bool ShowReconDistances = true);

public interface IScopeSource
{
    ScopeState? Current { get; }
}

public sealed class InMemoryScopeSource : IScopeSource
{
    public ScopeState? Current { get; private set; }

    public void Set(ScopeState? state) => Current = state;
}

public enum ReticleShapeKind
{
    Line,
    Dot,
    Ring,
}

public readonly record struct ReticleShape(ReticleShapeKind Kind, double X1, double Y1, double X2, double Y2, double Thickness, double Alpha = 1);

public readonly record struct ScopePoint(double X, double Y);

public readonly record struct ScopeEdgeRing(double Radius, double Thickness, double Alpha);

public readonly record struct ScopeMarkView(string Id, double X, double Y, bool Hostile, string DistanceText);

public sealed record ScopeGeometry(
    double Width,
    double Height,
    double CentreX,
    double CentreY,
    double Radius,
    double Unit,
    IReadOnlyList<IReadOnlyList<ScopePoint>> Mask,
    IReadOnlyList<ScopeEdgeRing> EdgeRings,
    IReadOnlyList<ReticleShape> Reticle,
    string ZoomText,
    double ZoomMarker,
    string Label,
    double LabelMaxWidth,
    IReadOnlyList<ScopeMarkView> Marks);
