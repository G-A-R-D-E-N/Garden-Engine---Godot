using System;

namespace OpenCommonwealth.Services.Combat;

public static class RadiationAssumptions
{
    // Invariant: UNVERIFIED placeholder, not an engine value; the Rads at which the maximum health has shrunk to zero.
    public const double RadsForFullLoss = 1000;

    public static double Fraction(double rads) => double.IsFinite(rads) ? Math.Clamp(rads / RadsForFullLoss, 0, 1) : 0;
}
