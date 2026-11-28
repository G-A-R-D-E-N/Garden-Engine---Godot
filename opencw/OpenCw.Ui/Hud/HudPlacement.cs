using System;
using OpenCommonwealth.Services.Ui.Layout;

namespace OpenCommonwealth.Services.Ui.Hud;

public readonly record struct HudPlaced(LayoutRect Rect, double Scale);

public static class HudPlacement
{
    public static HudPlaced Place(
        LayoutRect slot,
        double stageWidth,
        double stageHeight,
        LayoutRect area,
        double safeMargin,
        double userScale,
        HudElementPlacement user)
    {
        double fit = Math.Min(area.Width / stageWidth, area.Height / stageHeight);
        double scale = fit * userScale;
        double margin = Math.Min(safeMargin * fit, Math.Min(area.Width, area.Height) / 2);
        var safe = new LayoutRect(area.X + margin, area.Y + margin, area.Width - 2 * margin, area.Height - 2 * margin);
        double width = slot.Width * scale;
        double height = slot.Height * scale;

        HudAlign horizontal = user.Anchor is { } a ? HudAnchors.Horizontal(a) : Classify(slot.X + slot.Width / 2, stageWidth);
        HudAlign vertical = user.Anchor is { } b ? HudAnchors.Vertical(b) : Classify(slot.Y + slot.Height / 2, stageHeight);
        bool anchored = user.Anchor.HasValue;

        double x = Axis(horizontal, safe.X, safe.Width, width, anchored ? 0 : Distance(horizontal, slot.X, slot.Width, stageWidth) * fit)
            + user.OffsetX * fit;
        double y = Axis(vertical, safe.Y, safe.Height, height, anchored ? 0 : Distance(vertical, slot.Y, slot.Height, stageHeight) * fit)
            + user.OffsetY * fit;

        x = Math.Clamp(x, safe.X, Math.Max(safe.X, safe.X + safe.Width - width));
        y = Math.Clamp(y, safe.Y, Math.Max(safe.Y, safe.Y + safe.Height - height));
        return new HudPlaced(new LayoutRect(x, y, width, height), scale);
    }

    private static HudAlign Classify(double centre, double stageSize) =>
        centre < stageSize / 3 ? HudAlign.Start : centre > stageSize * 2 / 3 ? HudAlign.End : HudAlign.Middle;

    private static double Distance(HudAlign align, double start, double size, double stageSize) => align switch
    {
        HudAlign.Start => start,
        HudAlign.End => stageSize - (start + size),
        _ => start + size / 2 - stageSize / 2,
    };

    private static double Axis(HudAlign align, double safeStart, double safeSize, double size, double distance) => align switch
    {
        HudAlign.Start => safeStart + distance,
        HudAlign.End => safeStart + safeSize - distance - size,
        _ => safeStart + safeSize / 2 + distance - size / 2,
    };
}
