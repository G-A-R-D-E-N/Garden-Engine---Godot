using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Combat;

public enum DamageKind { Physical, Unmitigated }

public static class ArmorMitigation
{
    // Invariant: the path is the Radicle ARMO FNAM Armor Rating row; the cap on the share of physical damage armor can remove is an UNVERIFIED placeholder, not an engine value.
    public const string RatingPath = "ARMO\\ArmorRating";
    public const double MaxReduction = 0.85;

    public static double RatingOf(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0 ? value : 0;

    public static double Reduced(double damage, double rating) =>
        double.IsFinite(damage) && damage > 0 ? damage * (1 - Math.Clamp(double.IsFinite(rating) ? rating / 100 : 0, 0, MaxReduction)) : 0;
}

// Requires: main thread; the one entry the live paths use to hurt the player, so armor is applied once and only to physical damage.
public static class PlayerDamage
{
    public static Func<double>? ArmorRating { get; set; }

    public static double Take(double amount, DamageKind kind)
    {
        var dealt = kind == DamageKind.Physical && ArmorRating?.Invoke() is { } rating ? ArmorMitigation.Reduced(amount, rating) : amount;
        if (dealt > 0) PlayerHealth.Current?.AddHealth(-dealt);
        return dealt;
    }
}

public static class FallAssumptions
{
    // Invariant: UNVERIFIED placeholders, not engine values.
    public const double SafeHeightMeters = 4;
    public const double HealthPerMeter = 10;
}

public static class FallDamage
{
    public static double Amount(double landingSpeed, double gravity, bool inPowerArmor, bool swimming)
    {
        if (inPowerArmor || swimming || !double.IsFinite(landingSpeed) || !double.IsFinite(gravity) || landingSpeed <= 0 || gravity <= 0) return 0;
        var height = landingSpeed * landingSpeed / (2 * gravity);
        return Math.Max(0, height - FallAssumptions.SafeHeightMeters) * FallAssumptions.HealthPerMeter;
    }
}
