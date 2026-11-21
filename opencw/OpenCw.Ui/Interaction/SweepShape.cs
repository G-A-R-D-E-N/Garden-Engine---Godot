using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

// Invariant: local design values, not engine data; a carried or thrown object is swept as one sphere just inside its bounds (mean half extent), UNVERIFIED against the native collision shape.
public static class SweepShape
{
    public const double MinRadius = 0.03;
    public const double MaxRadius = 0.4;
    public const double InscribedFactor = 0.9;
    public const double BackoffStepMeters = 0.1;

    public static double Radius(double sizeX, double sizeY, double sizeZ)
    {
        var half = (Math.Abs(sizeX) + Math.Abs(sizeY) + Math.Abs(sizeZ)) / 6 * InscribedFactor;
        return double.IsFinite(half) ? Math.Clamp(half, MinRadius, MaxRadius) : MinRadius;
    }

    // Requires: result is the safe and unsafe fractions a motion cast returns; an empty or non-finite answer means the whole motion is clear.
    public static double SafeFraction(IReadOnlyList<float>? result) =>
        result is { Count: > 0 } && float.IsFinite(result[0]) ? Math.Clamp(result[0], 0, 1) : 1;

    public static bool Blocked(IReadOnlyList<float>? result) => SafeFraction(result) < 1;

    // Invariant: distances back from the object toward the player in fixed steps, ending exactly at the player; the first one that is not overlapping is the placement.
    public static IEnumerable<double> Backoff(double length)
    {
        if (!(length > 0) || !double.IsFinite(length)) yield break;
        for (var distance = 0.0; distance < length; distance += BackoffStepMeters) yield return distance;
        yield return length;
    }
}
