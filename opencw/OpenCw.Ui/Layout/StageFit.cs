using System;

namespace OpenCommonwealth.Services.Ui.Layout;

public readonly record struct StageTransform(double Scale, double OffsetX, double OffsetY)
{
    public LayoutRect Apply(LayoutRect rect) =>
        new(OffsetX + rect.X * Scale, OffsetY + rect.Y * Scale, rect.Width * Scale, rect.Height * Scale);
}

public static class StageFit
{
    public static StageTransform Fit(LayoutRect area, LayoutRect safe, double stageWidth, double stageHeight)
    {
        var box = safe.Width > 0 && safe.Height > 0 ? safe : area;
        double scale = Math.Min(box.Width / stageWidth, box.Height / stageHeight);
        return new StageTransform(
            scale,
            box.X + (box.Width - stageWidth * scale) / 2,
            box.Y + (box.Height - stageHeight * scale) / 2);
    }
}
