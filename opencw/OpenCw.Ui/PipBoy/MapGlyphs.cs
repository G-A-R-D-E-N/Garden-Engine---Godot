using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum MapGlyph
{
    Location,
    LocationUndiscovered,
    SettlementOwned,
    SettlementRaider,
    Quest,
    Door,
    PowerArmor,
    Waypoint,
    Player,
}

public sealed record MapLegendEntry(MapGlyph Glyph, string Word);

public static class MapGlyphs
{
    private static readonly Dictionary<string, MapGlyph> Table = new()
    {
        ["location"] = MapGlyph.Location,
        ["settlement"] = MapGlyph.SettlementOwned,
        ["settlementowned"] = MapGlyph.SettlementOwned,
        ["raider"] = MapGlyph.SettlementRaider,
        ["raiders"] = MapGlyph.SettlementRaider,
        ["settlementraider"] = MapGlyph.SettlementRaider,
        ["quest"] = MapGlyph.Quest,
        ["questdoor"] = MapGlyph.Quest,
        ["door"] = MapGlyph.Door,
        ["powerarmor"] = MapGlyph.PowerArmor,
        ["waypoint"] = MapGlyph.Waypoint,
        ["player"] = MapGlyph.Player,
    };

    private static readonly Dictionary<MapGlyph, string> Words = new()
    {
        [MapGlyph.Location] = "LOCATION",
        [MapGlyph.LocationUndiscovered] = "UNDISCOVERED",
        [MapGlyph.SettlementOwned] = "SETTLEMENT",
        [MapGlyph.SettlementRaider] = "RAIDERS",
        [MapGlyph.Quest] = "QUEST",
        [MapGlyph.Door] = "DOOR",
        [MapGlyph.PowerArmor] = "POWER ARMOR",
        [MapGlyph.Waypoint] = "MARKER",
        [MapGlyph.Player] = "YOU",
    };

    private static readonly HashSet<string> EdgeKinds = new() { "quest", "questdoor", "waypoint" };

    public static MapGlyph Of(MapMarker marker)
    {
        var glyph = Table.GetValueOrDefault(MapMarkerKinds.Normalize(marker.Type), MapGlyph.Location);
        return glyph == MapGlyph.Location && !marker.Discovered ? MapGlyph.LocationUndiscovered : glyph;
    }

    public static string Word(MapGlyph glyph) => Words[glyph];

    public static bool IsEdgeClamped(string? type) => EdgeKinds.Contains(MapMarkerKinds.Normalize(type));

    public static double Scale(MapView view, double t, bool selected)
    {
        var (min, max) = (view, selected) switch
        {
            (MapView.Local, false) => (2.0, 1.5),
            (MapView.Local, true) => (2.5, 1.875),
            (_, false) => (2.0, 0.5),
            _ => (2.5, 0.625),
        };
        return min + Math.Clamp(t, 0, 1) * (max - min);
    }

    public static IReadOnlyList<MapLegendEntry> Legend(IEnumerable<MapMarker> drawn, bool waypoint, bool player)
    {
        var glyphs = drawn.Select(Of).ToHashSet();
        if (waypoint) glyphs.Add(MapGlyph.Waypoint);
        if (player) glyphs.Add(MapGlyph.Player);
        return glyphs.OrderBy(glyph => glyph).Select(glyph => new MapLegendEntry(glyph, Word(glyph))).ToArray();
    }
}
