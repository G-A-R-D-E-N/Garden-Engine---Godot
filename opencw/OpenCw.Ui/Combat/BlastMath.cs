namespace OpenCommonwealth.Services.Combat;

// Invariant: every number here is an UNVERIFIED placeholder of ours, not an engine or record value.
public static class BlastMath
{
    public const double DefaultDamage = 50;
    public const double RadiusMeters = 5;
    public const double FuseSeconds = 3;
    public const double MineArmSeconds = 1;
    public const double MineTriggerMeters = 1.5;
    public const double ThrowSpeedMetersPerSecond = 12;

    public static double BaseDamage(double weaponDamage) =>
        double.IsFinite(weaponDamage) && weaponDamage > 0 ? weaponDamage : DefaultDamage;

    // Invariant: full damage at the centre, falling linearly to 0 at the radius; 0 beyond it or for a bad input.
    public static double Damage(double baseDamage, double distance, double radius)
    {
        if (!double.IsFinite(baseDamage) || !double.IsFinite(distance) || !double.IsFinite(radius) || baseDamage <= 0 || radius <= 0 || distance >= radius) return 0;
        return baseDamage * (1 - Math.Max(0, distance) / radius);
    }

    public static bool MineTriggered(double armedSeconds, double distance) =>
        armedSeconds >= MineArmSeconds && distance <= MineTriggerMeters;
}
