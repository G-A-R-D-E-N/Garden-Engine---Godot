using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Vats;

public readonly record struct VatsAim(
    float Distance,
    int WeaponType,
    float WeaponMaxRange,
    float? AimModelMaxConeDegrees,
    float WeaponAccuracy,
    float Perception,
    int CrippledArmCount);

public readonly record struct VatsPartState(float Visibility, int ApCost, double Health, bool Crippled, VatsPoint? Screen);

public static class VatsPartChances
{
    // Invariant: a part the host gives no state for is not offered; null when the weapon is melee (Fraction is not ported there).
    public static IReadOnlyList<VatsPart>? Build(
        VatsChanceSettings settings, IReadOnlyList<VatsBodyPart> parts, in VatsAim aim, Func<VatsBodyPart, VatsPartState?> state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(state);
        if (aim.WeaponType < VatsChance.FirstRangedWeaponType)
            return null;
        var rows = new List<VatsPart>(parts.Count);
        foreach (var part in parts)
        {
            if (state(part) is not { } s)
                continue;
            var input = new VatsChanceInput(
                aim.Distance, aim.WeaponType, aim.WeaponMaxRange, aim.AimModelMaxConeDegrees, aim.WeaponAccuracy,
                aim.Perception, aim.CrippledArmCount, s.Visibility, (byte)Math.Clamp(part.BaseHitChance, 0, byte.MaxValue));
            if (VatsChance.Fraction(settings, input) is not { } fraction)
                return null;
            rows.Add(new VatsPart(
                part.PartType.ToString(CultureInfo.InvariantCulture), part.Name, VatsChance.Percent(fraction), s.Health, s.Crippled, s.ApCost, s.Screen));
        }
        return rows;
    }
}
