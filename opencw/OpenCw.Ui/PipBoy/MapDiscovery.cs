using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class MapDiscovery
{
    public const double Radius = WorldMapFeed.CellSize;

    public static IReadOnlyList<string> Near(IEnumerable<MapMarker> markers, double x, double y, double radius = Radius) =>
        markers.Where(m => (m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y) <= radius * radius).Select(m => m.Id).ToArray();

    public static string? Nearest(IEnumerable<MapMarker> markers, double x, double y, double radius = Radius)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        MapMarker? best = null;
        var bestSquared = double.PositiveInfinity;
        foreach (var marker in markers)
        {
            var squared = (marker.X - x) * (marker.X - x) + (marker.Y - y) * (marker.Y - y);
            if (squared > radius * radius) continue;
            if (best is null || squared < bestSquared || (squared == bestSquared && string.CompareOrdinal(marker.Id, best.Id) < 0))
            {
                best = marker;
                bestSquared = squared;
            }
        }
        return best?.Id;
    }

    public static IReadOnlyList<MapMarker> Apply(IEnumerable<MapMarker> markers, IReadOnlyCollection<string> discovered) =>
        markers.Select(m => !m.Discovered && discovered.Contains(m.Id) ? m with { Discovered = true } : m).ToArray();
}
