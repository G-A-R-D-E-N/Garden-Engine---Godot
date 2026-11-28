using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record MapBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;
}

public sealed record MapMarker(string Id, string Name, string Type, double X, double Y, bool Discovered = true,
                               bool CanFastTravel = false);

public sealed record FastTravelRequest(string MarkerId, double X, double Y);

public sealed class MapModel : IPipBoyTabSource
{
    public const double SpecGutter = 0.06;

    private readonly List<MapMarker> _markers;
    private readonly HashSet<string> _hiddenTypes = new();
    private string[] _tokens = Array.Empty<string>();

    public MapModel(MapBounds bounds, double viewWidth, double viewHeight, IEnumerable<MapMarker>? markers = null,
                    double minZoom = 0.05, double maxZoom = 16)
    {
        if (!(bounds.Width > 0) || !(bounds.Height > 0)) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (!(viewWidth > 0) || !(viewHeight > 0)) throw new ArgumentOutOfRangeException(nameof(viewWidth));
        if (!(minZoom > 0) || !(maxZoom >= minZoom)) throw new ArgumentOutOfRangeException(nameof(minZoom));
        Bounds = bounds;
        ViewWidth = viewWidth;
        ViewHeight = viewHeight;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        _markers = (markers ?? Array.Empty<MapMarker>()).ToList();
        Zoom = Math.Clamp(Math.Min(viewWidth / bounds.Width, viewHeight / bounds.Height), minZoom, maxZoom);
        CenterX = (bounds.MinX + bounds.MaxX) / 2;
        CenterY = (bounds.MinY + bounds.MaxY) / 2;
        Clamp();
    }

    public PipBoyTab Tab => PipBoyTab.Map;

    public MapBounds Bounds { get; }

    public double ViewWidth { get; private set; }

    public double ViewHeight { get; private set; }

    public double MinZoom { get; }

    public double MaxZoom { get; }

    public double Zoom { get; private set; }

    public double GutterFraction { get; set; }

    public double ZoomT => MaxZoom > MinZoom ? (Zoom - MinZoom) / (MaxZoom - MinZoom) : 0;

    public double CenterX { get; private set; }

    public double CenterY { get; private set; }

    public double? PlayerX { get; private set; }

    public double? PlayerY { get; private set; }

    public string? SelectedId { get; private set; }

    public string? CurrentKey => SelectedId;

    public IReadOnlyList<string> Types => _markers.Select(m => m.Type).Distinct().ToArray();

    public bool ShowUndiscovered { get; set; }

    public IReadOnlyList<MapMarker> VisibleMarkers =>
        _markers.Where(m => (m.Discovered || ShowUndiscovered) && !_hiddenTypes.Contains(m.Type) && ListSearch.Matches(m.Name, _tokens)).ToArray();

    public void SetViewport(double width, double height)
    {
        if (!(width > 0) || !(height > 0)) return;
        ViewWidth = width;
        ViewHeight = height;
        Clamp();
    }

    public (double X, double Y) WorldToScreen(double x, double y) =>
        ((x - CenterX) * Zoom + ViewWidth / 2, (y - CenterY) * Zoom + ViewHeight / 2);

    public (double X, double Y) ScreenToWorld(double x, double y) =>
        ((x - ViewWidth / 2) / Zoom + CenterX, (y - ViewHeight / 2) / Zoom + CenterY);

    public void PanByWorld(double dx, double dy)
    {
        CenterX += dx;
        CenterY += dy;
        Clamp();
    }

    public void DragByScreen(double dx, double dy) => PanByWorld(-dx / Zoom, -dy / Zoom);

    public void DragByScreen(double dx, double dy, double multiplier)
    {
        if (double.IsFinite(dx) && double.IsFinite(dy) && double.IsFinite(multiplier)) PanByWorld(-dx * multiplier / Zoom, -dy * multiplier / Zoom);
    }

    public void CenterOn(double x, double y)
    {
        CenterX = x;
        CenterY = y;
        Clamp();
    }

    public void SetZoom(double zoom, double? anchorX = null, double? anchorY = null)
    {
        if (!double.IsFinite(zoom)) return;
        var ax = anchorX ?? ViewWidth / 2;
        var ay = anchorY ?? ViewHeight / 2;
        var (wx, wy) = ScreenToWorld(ax, ay);
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        CenterX = wx - (ax - ViewWidth / 2) / Zoom;
        CenterY = wy - (ay - ViewHeight / 2) / Zoom;
        Clamp();
    }

    public void ZoomBy(double factor, double? anchorX = null, double? anchorY = null)
    {
        if (factor > 0 && double.IsFinite(factor)) SetZoom(Zoom * factor, anchorX, anchorY);
    }

    public void SetZoomT(double t, double? anchorX = null, double? anchorY = null)
    {
        if (double.IsFinite(t)) SetZoom(MinZoom + Math.Clamp(t, 0, 1) * (MaxZoom - MinZoom), anchorX, anchorY);
    }

    public void StepZoomT(double delta, double? anchorX = null, double? anchorY = null)
    {
        if (double.IsFinite(delta)) SetZoomT(ZoomT + delta, anchorX, anchorY);
    }

    public void SetPlayer(double x, double y)
    {
        PlayerX = x;
        PlayerY = y;
    }

    public bool CenterOnPlayer()
    {
        if (PlayerX is not { } x || PlayerY is not { } y) return false;
        CenterOn(x, y);
        return true;
    }

    public void SetSearch(string? query) => _tokens = ListSearch.Tokenize(query);

    public void SetTypeVisible(string type, bool visible)
    {
        if (visible) _hiddenTypes.Remove(type);
        else _hiddenTypes.Add(type);
    }

    public bool IsTypeVisible(string type) => !_hiddenTypes.Contains(type);

    public void Upsert(MapMarker marker)
    {
        var index = _markers.FindIndex(m => m.Id == marker.Id);
        if (index >= 0) _markers[index] = marker;
        else _markers.Add(marker);
    }

    public bool Remove(string id)
    {
        if (SelectedId == id) SelectedId = null;
        return _markers.RemoveAll(m => m.Id == id) > 0;
    }

    public MapMarker? Nearest(double x, double y, double maxDistance = double.PositiveInfinity)
    {
        MapMarker? best = null;
        var limit = maxDistance * maxDistance;
        var bestSquared = double.PositiveInfinity;
        foreach (var marker in VisibleMarkers)
        {
            var squared = Squared(marker.X - x, marker.Y - y);
            if (squared > limit) continue;
            if (best is null || squared < bestSquared || (squared == bestSquared && string.CompareOrdinal(marker.Id, best.Id) < 0))
            {
                best = marker;
                bestSquared = squared;
            }
        }
        return best;
    }

    public (double X, double Y) DisplayPosition(MapMarker marker)
    {
        var (x, y) = WorldToScreen(marker.X, marker.Y);
        return MapGlyphs.IsEdgeClamped(marker.Type) ? MapCamera.ClampToEdge(x, y, ViewWidth, ViewHeight) : (x, y);
    }

    public MapMarker? NearestToScreen(double screenX, double screenY, double maxPixels)
    {
        MapMarker? best = null;
        var limit = maxPixels * maxPixels;
        var bestSquared = double.PositiveInfinity;
        foreach (var marker in VisibleMarkers)
        {
            var (x, y) = DisplayPosition(marker);
            var squared = Squared(x - screenX, y - screenY);
            if (squared > limit) continue;
            if (best is null || squared < bestSquared || (squared == bestSquared && string.CompareOrdinal(marker.Id, best.Id) < 0))
            {
                best = marker;
                bestSquared = squared;
            }
        }
        return best;
    }

    public bool Select(string key) => SelectMarker(key, true);

    public bool SelectMarker(string key, bool centre)
    {
        var marker = _markers.Find(m => m.Id == key && (m.Discovered || ShowUndiscovered));
        if (marker is null) return false;
        _hiddenTypes.Remove(marker.Type);
        _tokens = Array.Empty<string>();
        SelectedId = marker.Id;
        if (centre) CenterOn(marker.X, marker.Y);
        return true;
    }

    public void ClearSelection() => SelectedId = null;

    public MapMarker? Marker(string id) => _markers.Find(m => m.Id == id);

    public bool Cycle(int delta)
    {
        var ordered = VisibleMarkers
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0) return false;
        var index = ordered.FindIndex(m => m.Id == SelectedId);
        var next = index < 0
            ? (delta >= 0 ? 0 : ordered.Count - 1)
            : ((index + delta) % ordered.Count + ordered.Count) % ordered.Count;
        return Select(ordered[next].Id);
    }

    public FastTravelRequest? RequestFastTravel(string? markerId = null)
    {
        var id = markerId ?? SelectedId;
        var marker = id is null ? null : _markers.Find(m => m.Id == id);
        return marker is { Discovered: true, CanFastTravel: true }
            ? new FastTravelRequest(marker.Id, marker.X, marker.Y)
            : null;
    }

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens) =>
        _markers.Where(m => m.Discovered && ListSearch.Matches(m.Name + " " + m.Type, tokens))
                .Select(m => new PipBoySearchHit(PipBoyTab.Map, m.Id, m.Name, m.Type))
                .ToArray();

    private static double Squared(double dx, double dy) => dx * dx + dy * dy;

    private void Clamp()
    {
        CenterX = ClampAxis(CenterX, Bounds.MinX, Bounds.MaxX, ViewWidth / (2 * Zoom), GutterFraction * ViewWidth / Zoom);
        CenterY = ClampAxis(CenterY, Bounds.MinY, Bounds.MaxY, ViewHeight / (2 * Zoom), GutterFraction * ViewHeight / Zoom);
    }

    private static double ClampAxis(double center, double min, double max, double half, double margin)
    {
        if (!double.IsFinite(center)) return (min + max) / 2;
        var lo = min + half - margin;
        var hi = max - half + margin;
        return lo > hi ? (min + max) / 2 : Math.Clamp(center, lo, hi);
    }
}
