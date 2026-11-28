using System;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public readonly record struct MapLabelBox(double X, double Y);

public static class MapCamera
{
    public const double WheelStepT = 0.15;
    public const double StickZoomPerStep = 0.05;
    public const double StickStepSeconds = 1.0 / 30.0;
    public const double ArrowPixels = 30;
    public const double Deadzone = 0.2;
    public const double LocalDragMultiplier = 2.75;
    public const double WorldDragMultiplier = 1.0;
    public const double TopInset = 30;
    public const double SideInset = 10;
    public const double LabelGap = 10;

    public static double DragMultiplier(MapView view) => view == MapView.Local ? LocalDragMultiplier : WorldDragMultiplier;

    public static double Axis(double value) => double.IsFinite(value) && Math.Abs(value) >= Deadzone ? Math.Clamp(value, -1, 1) : 0;

    public static void ZoomNotch(MapModel map, bool zoomIn, double? anchorX = null, double? anchorY = null) =>
        map.StepZoomT(zoomIn ? WheelStepT : -WheelStepT, anchorX, anchorY);

    public static void PanArrow(MapModel map, double directionX, double directionY)
    {
        if (!double.IsFinite(directionX) || !double.IsFinite(directionY)) return;
        map.PanByWorld(Math.Sign(directionX) * ArrowPixels / map.Zoom, Math.Sign(directionY) * ArrowPixels / map.Zoom);
    }

    public static bool ZoomStick(MapModel map, double amount, double seconds)
    {
        amount = Axis(amount);
        if (amount == 0 || !(seconds > 0) || !double.IsFinite(seconds)) return false;
        var before = map.ZoomT;
        map.StepZoomT(amount * StickZoomPerStep * seconds / StickStepSeconds);
        return map.ZoomT != before;
    }

    public static void Drag(MapModel map, MapView view, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
        map.DragByScreen(dx, dy, DragMultiplier(view));
    }

    public static (double X, double Y) ClampToEdge(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !(width > 0) || !(height > 0)) return (width / 2, height / 2);
        if (x >= 0 && x <= width && y >= 0 && y <= height) return (x, y);
        double cx = width / 2, cy = height / 2;
        double left = SideInset, right = width - SideInset, top = TopInset, bottom = height - SideInset;
        if (left > right) left = right = cx;
        if (top > bottom) top = bottom = cy;
        var dx = x - cx;
        var dy = y - cy;
        var tx = dx > 0 ? (right - cx) / dx : dx < 0 ? (left - cx) / dx : double.PositiveInfinity;
        var ty = dy > 0 ? (bottom - cy) / dy : dy < 0 ? (top - cy) / dy : double.PositiveInfinity;
        var t = Math.Min(Math.Min(tx, ty), 1);
        return (cx + dx * t, cy + dy * t);
    }

    public static MapLabelBox PlaceLabel(double anchorX, double anchorY, double width, double height, double viewWidth, double viewHeight)
    {
        var x = Math.Clamp(anchorX - width / 2, 0, Math.Max(0, viewWidth - width));
        var y = anchorY + LabelGap;
        if (y + height > viewHeight) y = anchorY - LabelGap - height;
        y = Math.Clamp(y, 0, Math.Max(0, viewHeight - height));
        return new MapLabelBox(x, y);
    }
}
