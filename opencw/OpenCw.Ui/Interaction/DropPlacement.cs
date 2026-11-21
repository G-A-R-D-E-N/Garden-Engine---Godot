using System;

namespace OpenCommonwealth.Services.Interaction;

// Invariant: these are local design values, not engine data; the native put-down physics is UNVERIFIED and not reproduced.
public static class DropPlacement
{
    public const double ForwardMeters = 0.6;
    public const double ProbeUpMeters = 0.5;
    public const double ProbeDownMeters = 3;
    public const double LiftMeters = 0.02;
    public const double MaxRestShiftMeters = 1;

    public static (double X, double Z) Ahead(double x, double z, double forwardX, double forwardZ)
    {
        var lengthSquared = forwardX * forwardX + forwardZ * forwardZ;
        if (!(lengthSquared > 1e-4)) return (x, z);
        var length = Math.Sqrt(lengthSquared);
        return (x + forwardX / length * ForwardMeters, z + forwardZ / length * ForwardMeters);
    }

    // Requires: modelMinY is the lowest model point in the item's own space; the item rests on the ground with that point lifted by LiftMeters.
    public static double RestY(double groundY, double modelMinY) =>
        groundY + LiftMeters - Math.Clamp(double.IsFinite(modelMinY) ? modelMinY : 0, -MaxRestShiftMeters, MaxRestShiftMeters);
}
