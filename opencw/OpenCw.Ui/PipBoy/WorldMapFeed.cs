using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record WorldMapData(MapBounds Bounds, IReadOnlyList<MapMarker> Markers)
{
    public const double ViewWidth = 1280;
    public const double ViewHeight = 720;

    public const double MaxZoom = 0.25;

    public const double LocalHalfExtent = 2 * WorldMapFeed.CellSize;

    // Invariant: the zoom floor is half the whole-world fit, because the default zoom range (0.05 to 16) cannot fit a worldspace hundreds of thousands of units wide.
    public MapModel NewModel() => Fit(Bounds, Markers);

    // Invariant: the local view is a square of LocalHalfExtent either side of the player holding only the world markers inside it; non-finite input gives no local view.
    public MapModel? NewLocalModel(double playerX, double playerY)
    {
        if (!double.IsFinite(playerX) || !double.IsFinite(playerY)) return null;
        var local = new MapBounds(playerX - LocalHalfExtent, playerY - LocalHalfExtent, playerX + LocalHalfExtent, playerY + LocalHalfExtent);
        var model = Fit(local, Markers.Where(m => m.X >= local.MinX && m.X <= local.MaxX && m.Y >= local.MinY && m.Y <= local.MaxY));
        model.SetPlayer(playerX, playerY);
        return model;
    }

    private static MapModel Fit(MapBounds bounds, IEnumerable<MapMarker> markers)
    {
        var fit = Math.Min(ViewWidth / bounds.Width, ViewHeight / bounds.Height);
        return new MapModel(bounds, ViewWidth, ViewHeight, markers, fit / 2, Math.Max(MaxZoom, fit));
    }
}

// Invariant: map X is the FO4 X and map Y is the negated FO4 Y; a cell index N spans N * CellSize to (N + 1) * CellSize.
public static class WorldMapFeed
{
    public const double CellSize = 4096;
    public const string MarkerType = "location";
    public const string UnnamedMarker = "Unnamed location";
    public const string NwCellXPath = "WRLD\\MapNwCellX";
    public const string NwCellYPath = "WRLD\\MapNwCellY";
    public const string SeCellXPath = "WRLD\\MapSeCellX";
    public const string SeCellYPath = "WRLD\\MapSeCellY";
    public const string WorldFlagsPath = "WRLD\\Flags";
    public const string MarkerFlagsPath = "REFR\\MapFlags";
    public const string PositionXPath = "REFR\\PositionX";
    public const string PositionYPath = "REFR\\PositionY";
    public const int CantFastTravelWorld = 0x02;
    public const int MarkerVisible = 0x01;
    public const int MarkerCanTravelTo = 0x02;

    // Requires: null means the worldspace has no readable map cell rectangle, so the Pip-Boy omits the Map tab.
    public static WorldMapData? Read(IRecordReader records, string worldspaceId)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(worldspaceId);
        if (Number(records.Scalar(worldspaceId, NwCellXPath)) is not { } nwX || Number(records.Scalar(worldspaceId, NwCellYPath)) is not { } nwY
            || Number(records.Scalar(worldspaceId, SeCellXPath)) is not { } seX || Number(records.Scalar(worldspaceId, SeCellYPath)) is not { } seY)
            return null;
        var bounds = new MapBounds(nwX * CellSize, -(nwY + 1) * CellSize, (seX + 1) * CellSize, -seY * CellSize);
        if (!(bounds.Width > 0) || !(bounds.Height > 0))
            return null;
        var worldFlags = Flags(records.Scalar(worldspaceId, WorldFlagsPath));
        var canTravelWorld = (worldFlags & CantFastTravelWorld) == 0;
        var markers = new List<MapMarker>();
        foreach (var id in records.WorldspaceMapMarkers(worldspaceId))
        {
            if (Number(records.Scalar(id, PositionXPath)) is not { } x || Number(records.Scalar(id, PositionYPath)) is not { } y)
                continue;
            var name = records.Name(id) is { Length: > 0 } full ? full : records.EditorId(id) is { Length: > 0 } editorId ? editorId : UnnamedMarker;
            var flags = Flags(records.Scalar(id, MarkerFlagsPath));
            markers.Add(new MapMarker(id, name, MarkerType, x, -y, (flags & MarkerVisible) != 0, canTravelWorld && (flags & MarkerCanTravelTo) != 0));
        }
        return new WorldMapData(bounds, markers);
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static long Flags(string? text) => Number(text) is { } value ? (long)value : 0;
}

public sealed class MapTravelRules : IMapTravelRules
{
    public const string NoExecutorReason = "Fast travel is not available yet";

    private readonly bool _executorAvailable;

    public MapTravelRules(bool executorAvailable)
    {
        _executorAvailable = executorAvailable;
    }

    public TravelVerdict CanFastTravel(MapMarker marker)
    {
        if (!marker.Discovered || !marker.CanFastTravel)
            return TravelVerdict.No();
        return _executorAvailable ? TravelVerdict.Yes : TravelVerdict.No(NoExecutorReason);
    }
}
