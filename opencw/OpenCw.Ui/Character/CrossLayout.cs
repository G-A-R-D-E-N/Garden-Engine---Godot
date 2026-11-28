using System;

namespace OpenCommonwealth.Services.Ui.Character;

public readonly record struct CrossPoint(double X, double Y);

public static class CrossLayout
{
    public static CrossPoint Offset(int slot, double step)
    {
        if (slot < 1 || slot > FavouritesCrossModel.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        var distance = FavouritesCrossModel.RingOf(slot) * step;
        return FavouritesCrossModel.ArmOf(slot) switch
        {
            0 => new CrossPoint(-distance, 0),
            1 => new CrossPoint(distance, 0),
            2 => new CrossPoint(0, -distance),
            _ => new CrossPoint(0, distance),
        };
    }

    public static int? Hit(FavouritesCrossModel model, double dx, double dy, double step, double radius)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || !(radius > 0)) return null;
        int? best = null;
        var bestDistance = double.MaxValue;
        for (var slot = 1; slot <= FavouritesCrossModel.SlotCount; slot++)
        {
            if (!model.IsVisible(slot)) continue;
            var at = Offset(slot, step);
            var distance = Math.Sqrt((dx - at.X) * (dx - at.X) + (dy - at.Y) * (dy - at.Y));
            if (distance > radius || distance >= bestDistance) continue;
            best = slot;
            bestDistance = distance;
        }
        return best;
    }
}
