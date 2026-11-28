using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Layout;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class HudStagePlacement
{
    public const double StageWidth = 1280;
    public const double StageHeight = 720;

    public static readonly IReadOnlyDictionary<HudElement, (string Path, LayoutRect Slot)> Elements = new Dictionary<HudElement, (string, LayoutRect)>
    {
        [HudElement.Health] = ("LeftMeters_mc/HPMeter_mc", new LayoutRect(62, 640, 290, 24)),
        [HudElement.ActionPoints] = ("RightMeters_mc/ActionPointMeter_mc", new LayoutRect(984, 640, 234, 24)),
        [HudElement.Ammo] = ("RightMeters_mc/AmmoCount_mc", new LayoutRect(1120, 556, 98, 86)),
        [HudElement.Compass] = ("BottomCenterGroup_mc/CompassWidget_mc", new LayoutRect(488, 650, 304, 44)),
        [HudElement.Crosshair] = ("CenterGroup_mc/HUDCrosshair_mc", new LayoutRect(621, 341, 38, 38)),
        [HudElement.Interaction] = ("CenterGroup_mc/RolloverWidget_mc", new LayoutRect(733, 286, 320, 120)),
        [HudElement.Toasts] = ("HUDNotificationsGroup_mc", new LayoutRect(62, 36, 340, 316)),
    };

    public const string TopPath = "TopCenterGroup_mc";
    public static readonly LayoutRect TopSlot = new(540, 32, 200, 140);

    public static SwfPlace Compute(HudElement element, HudSettings settings, double areaWidth, double areaHeight) =>
        Elements.TryGetValue(element, out var entry) ? Fit(entry.Slot, settings.PlacementOf(element), settings, areaWidth, areaHeight) : SwfPlace.Identity;

    public static SwfPlace ComputeTop(HudSettings settings, double areaWidth, double areaHeight) =>
        Fit(TopSlot, default, settings, areaWidth, areaHeight);

    private static SwfPlace Fit(LayoutRect slot, HudElementPlacement user, HudSettings settings, double areaWidth, double areaHeight)
    {
        if (!(areaWidth > 0) || !(areaHeight > 0))
            return SwfPlace.Identity;
        var area = new LayoutRect(0, 0, areaWidth, areaHeight);
        double fit = Math.Min(areaWidth / StageWidth, areaHeight / StageHeight);
        var placed = HudPlacement.Place(slot, StageWidth, StageHeight, area, settings.SafeMargin, settings.Scale, user);
        double scale = placed.Scale / fit;
        return new SwfPlace(scale, placed.Rect.X / fit - slot.X * scale, placed.Rect.Y / fit - slot.Y * scale);
    }
}
