namespace OpenCommonwealth.Services.Combat;

public static class PelletSpread
{
    // Invariant: UNVERIFIED placeholders, not engine values; the half angle used when the aim model gives none, and the cap on a record value whose unit is not confirmed.
    public const double DefaultHalfAngleDegrees = 3;
    public const double MaxHalfAngleDegrees = 15;

    public static double HalfAngleDegrees(double recordDegrees) =>
        double.IsFinite(recordDegrees) && recordDegrees > 0 ? Math.Min(recordDegrees, MaxHalfAngleDegrees) : DefaultHalfAngleDegrees;

    // Requires: roll returns values in [0, 1). One (x, y) pair of tangent angles in radians per pellet, uniform over the disc; a single pellet stays on the centre ray and draws nothing.
    public static IReadOnlyList<(double X, double Y)> Offsets(int pellets, double halfAngleDegrees, Func<double> roll)
    {
        if (pellets <= 1) return [(0, 0)];
        var half = HalfAngleDegrees(halfAngleDegrees) * Math.PI / 180;
        var result = new (double X, double Y)[pellets];
        for (var i = 0; i < pellets; i++)
        {
            var radius = half * Math.Sqrt(Math.Clamp(roll(), 0, 1));
            var theta = 2 * Math.PI * Math.Clamp(roll(), 0, 1);
            result[i] = (radius * Math.Cos(theta), radius * Math.Sin(theta));
        }
        return result;
    }
}
