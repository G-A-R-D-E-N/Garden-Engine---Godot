namespace OpenCommonwealth.Services.Ui.Vats;

public sealed record VatsShotModifiers(
    double PartMultiplier = 1.0,
    bool IsCritical = false,
    double CriticalMultiplier = 2.0,
    double BonusCriticalDamage = 0.0,
    bool IsSneakAttack = false,
    double SneakMultiplier = 2.0,
    double BonusMultiplier = 1.0);

public readonly record struct VatsDamageBreakdown(
    double BaseDamage,
    double PartDamage,
    double CriticalBonusDamage,
    double FinalDamage);

public static class VatsDamageCalculator
{
    private static readonly VatsShotModifiers DefaultModifiers = new();

    public static double EffectivePartMultiplier(float damageMult) =>
        float.IsFinite(damageMult) && damageMult > 0f ? damageMult : 1.0;

    public static VatsDamageBreakdown Compute(double baseDamage, VatsBodyPart? part, VatsShotModifiers? modifiers = null)
    {
        var sanitizedBase = double.IsFinite(baseDamage) && baseDamage > 0.0 ? baseDamage : 0.0;
        var mods = modifiers ?? DefaultModifiers;
        var partMultiplier = part is { } bodyPart ? EffectivePartMultiplier(bodyPart.DamageMult) : 1.0;
        partMultiplier *= PositiveOrOne(mods.PartMultiplier);
        var damage = sanitizedBase * partMultiplier;
        if (mods.IsSneakAttack && double.IsFinite(mods.SneakMultiplier) && mods.SneakMultiplier > 0.0)
            damage *= mods.SneakMultiplier;
        if (double.IsFinite(mods.BonusMultiplier) && mods.BonusMultiplier >= 0.0)
            damage *= mods.BonusMultiplier;
        var partDamage = Saturate(damage);
        var criticalBonus = 0.0;
        if (mods.IsCritical && partDamage > 0.0)
        {
            var criticalMultiplier = double.IsFinite(mods.CriticalMultiplier) && mods.CriticalMultiplier >= 1.0
                ? mods.CriticalMultiplier
                : 2.0;
            var bonusCritical = double.IsFinite(mods.BonusCriticalDamage) && mods.BonusCriticalDamage > 0.0
                ? mods.BonusCriticalDamage
                : 0.0;
            criticalBonus = Saturate(partDamage * (criticalMultiplier - 1.0) + bonusCritical);
        }
        var finalDamage = Saturate(partDamage + criticalBonus);
        return new VatsDamageBreakdown(sanitizedBase, partDamage, criticalBonus, finalDamage);
    }

    private static double PositiveOrOne(double value) => double.IsFinite(value) && value > 0.0 ? value : 1.0;

    private static double Saturate(double value)
    {
        if (double.IsNaN(value) || value <= 0.0)
            return 0.0;
        return double.IsPositiveInfinity(value) ? double.MaxValue : value;
    }
}
