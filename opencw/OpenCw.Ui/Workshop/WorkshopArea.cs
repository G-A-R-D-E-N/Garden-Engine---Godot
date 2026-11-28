using System;

namespace OpenCommonwealth.Services.Ui.Workshop;

public enum AreaShape
{
    Box,
    Sphere,
    Ellipsoid,
}

public static class WorkshopArea
{
    public const double FallbackRadiusFo4Units = 5000.0;

    // Requires: the point is in the volume's own space; Sphere takes its radius from halfX.
    public static bool InVolume(AreaShape shape, double halfX, double halfY, double halfZ, double x, double y, double z)
    {
        switch (shape)
        {
            case AreaShape.Sphere:
                return x * x + y * y + z * z <= halfX * halfX;
            case AreaShape.Ellipsoid:
                if (!(halfX > 0) || !(halfY > 0) || !(halfZ > 0)) return false;
                return x * x / (halfX * halfX) + y * y / (halfY * halfY) + z * z / (halfZ * halfZ) <= 1.0;
            default:
                return Math.Abs(x) <= halfX && Math.Abs(y) <= halfY && Math.Abs(z) <= halfZ;
        }
    }
}
