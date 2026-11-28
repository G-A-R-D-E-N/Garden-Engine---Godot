using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class HudCompassArt
{
    public const string Parent = "BottomCenterGroup_mc/CompassWidget_mc";
    public const double HalfWidth = 152;
    public const string DirectionSymbol = "DirectionMarkerWidget";
    public const string MarkerSymbol = "CompassMarkerWidget";
    public const string DirectionTemplate = "compass_dir";
    public const double MarkerY = 0;

    public static readonly SwfSpriteText EmptyLabel = new("", -30, 5.75, 64, 24, 19, true);

    public static string MarkerTemplate(CompassMarkerKind kind) => "compass_mark" + (int)kind;

    public const string LocationSymbol = "LandmarkMarker";

    // Constraint: frame order is the CompassMarkerWidget frame labels Quest, QuestDoor, PlayerSet, Recon, EnemyTargeted, Enemy, Location; Friendly has no frame and Location is an empty holder (CodeNotes:Garden Engine/history/docs/code-notes/ui-vanilla.md).
    public static int MarkerFrame(CompassMarkerKind kind) => kind switch
    {
        CompassMarkerKind.Objective => 0,
        CompassMarkerKind.Custom => 2,
        CompassMarkerKind.Friendly => 3,
        CompassMarkerKind.Hostile => 5,
        _ => 0,
    };

    public static IEnumerable<SwfTemplate> Templates()
    {
        yield return new SwfTemplate(DirectionTemplate, DirectionSymbol, null, 0);
        foreach (var kind in Enum.GetValues<CompassMarkerKind>())
            yield return new SwfTemplate(MarkerTemplate(kind), kind == CompassMarkerKind.Location ? LocationSymbol : MarkerSymbol, null, MarkerFrame(kind));
    }

    public static double PixelOffset(double position) => Math.Clamp(position, -1, 1) * HalfWidth;

    public static void Add(SwfBindings bindings, CompassModel model)
    {
        foreach (var tick in model.Ticks)
        {
            var label = tick.Label is { } text ? EmptyLabel with { Text = text } : null;
            bindings.Sprites.Add(new SwfSprite(DirectionTemplate, Parent, PixelOffset(tick.Position), 0, tick.Alpha, label));
        }
        foreach (var marker in model.Markers)
            bindings.Sprites.Add(new SwfSprite(MarkerTemplate(marker.Kind), Parent, PixelOffset(marker.Position), MarkerY, marker.Alpha));
    }
}
