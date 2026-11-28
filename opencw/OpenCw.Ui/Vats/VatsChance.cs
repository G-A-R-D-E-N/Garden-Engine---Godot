using System;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Vats;

public sealed record VatsChanceSettings(
    float PartVisibilityMax = 0.8f,
    float NonActorBodyTerm = 100f,
    float ArmConditionBase = 0.2f,
    float ArmConditionMult = 0.8f,
    float WeaponDistanceMultiplier = 1f,
    float DistanceTermMin = 0.01f,
    float ConeDistanceMaxDegrees = 20f,
    float ConeDistanceMult = 400f,
    float ConeDistanceOffset = 300f,
    float ConeDistanceBonus = 0.8f,
    float WeaponBaseMultiplier = 0.007f,
    float PerceptionMultiplier = 0.035f,
    float DistanceMultiplier = 1f,
    float BodyPartMultiplier = 0.031f,
    float WeaponPerceptionOffset = 0f,
    float DistanceOffset = 0.001f,
    float BodyPartOffset = 0f,
    float Offset = 0f)
{
    public const string FloatPath = GmstSettings.FloatPath;

    // Requires: GMST values override the exe defaults above, so the formula must be fed from the plugin set; an unreadable GMST keeps the default.
    public static VatsChanceSettings Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var d = new VatsChanceSettings();
        return new VatsChanceSettings(
            GmstSettings.Float(reader, "fVATSChancePartVisibilityMax", d.PartVisibilityMax),
            GmstSettings.Float(reader, "fVATSChanceNonActorBodyTerm", d.NonActorBodyTerm),
            GmstSettings.Float(reader, "fVATSChanceArmConditionBase", d.ArmConditionBase),
            GmstSettings.Float(reader, "fVATSChanceArmConditionMult", d.ArmConditionMult),
            GmstSettings.Float(reader, "fVATSChanceWeaponDistanceMultiplier", d.WeaponDistanceMultiplier),
            GmstSettings.Float(reader, "fVATSChanceDistanceTermMin", d.DistanceTermMin),
            GmstSettings.Float(reader, "fVATSChanceConeDistanceMaxDegrees", d.ConeDistanceMaxDegrees),
            GmstSettings.Float(reader, "fVATSChanceConeDistanceMult", d.ConeDistanceMult),
            GmstSettings.Float(reader, "fVATSChanceConeDistanceOffset", d.ConeDistanceOffset),
            GmstSettings.Float(reader, "fVATSChanceConeDistanceBonus", d.ConeDistanceBonus),
            GmstSettings.Float(reader, "fVATSChanceWeaponBaseMultiplier", d.WeaponBaseMultiplier),
            GmstSettings.Float(reader, "fVATSChancePerceptionMultiplier", d.PerceptionMultiplier),
            GmstSettings.Float(reader, "fVATSChanceDistanceMultiplier", d.DistanceMultiplier),
            GmstSettings.Float(reader, "fVATSChanceBodyPartMultiplier", d.BodyPartMultiplier),
            GmstSettings.Float(reader, "fVATSChanceWeaponPerceptionOffset", d.WeaponPerceptionOffset),
            GmstSettings.Float(reader, "fVATSChanceDistanceOffset", d.DistanceOffset),
            GmstSettings.Float(reader, "fVATSChanceBodyPartOffset", d.BodyPartOffset),
            GmstSettings.Float(reader, "fVATSChanceOffset", d.Offset));
    }
}

// Requires: Distance is player to target in world units; WeaponMaxRange is the instance range when an instance exists, else the form range; WeaponAccuracy is the engine's weapon accuracy score (not derived here).
public readonly record struct VatsChanceInput(
    float Distance,
    int WeaponType,
    float WeaponMaxRange,
    float? AimModelMaxConeDegrees,
    float WeaponAccuracy,
    float Perception,
    int CrippledArmCount,
    float PartVisibility,
    byte? PartToHitChance);

public static class VatsChance
{
    public const int FirstRangedWeaponType = 7;

    // Invariant: exe literals; 0.5 is the arm penalty per crippled arm and 0.01 the chance floor.
    public const float ArmPenaltyPerCrippledArm = 0.5f;
    public const float MinimumChance = 0.01f;

    // Invariant: single-precision, term order as the exe; null for melee weapon types, whose distance-gated path is not ported; perk entry points are not applied.
    public static float? Fraction(VatsChanceSettings s, in VatsChanceInput i)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (i.WeaponType < FirstRangedWeaponType)
            return null;
        var visibility = s.PartVisibilityMax > 0f ? i.PartVisibility / s.PartVisibilityMax : 1f;
        visibility = Math.Min(Math.Max(visibility, 0f), 1f);
        var distance = float.IsPositiveInfinity(i.Distance) ? 0f : i.Distance;
        var body = i.PartToHitChance is { } toHit ? toHit : s.NonActorBodyTerm;
        var arm = Math.Max(1f - i.CrippledArmCount * ArmPenaltyPerCrippledArm, 0f) * s.ArmConditionMult + s.ArmConditionBase;
        var term = DistanceTerm(s, distance, i.WeaponMaxRange);
        if (i.AimModelMaxConeDegrees is { } cone)
            term += ConeBonus(s, distance, cone);
        var weapon = s.PerceptionMultiplier * i.Perception + s.WeaponBaseMultiplier * i.WeaponAccuracy + s.WeaponPerceptionOffset;
        var chance = weapon * (arm * visibility) * (s.BodyPartMultiplier * body + s.BodyPartOffset) * (s.DistanceMultiplier * term + s.DistanceOffset) + s.Offset;
        if (!(chance >= MinimumChance))
            return 0f;
        return chance > 1f ? 1f : chance;
    }

    // Requires: rounding unverified; the exe shows no rounding rule, so this rounds half away from zero.
    public static int Percent(float fraction) => (int)MathF.Round(fraction * 100f, MidpointRounding.AwayFromZero);

    private static float DistanceTerm(VatsChanceSettings s, float distance, float maxRange)
    {
        if (!(maxRange > 0f) || !(s.WeaponDistanceMultiplier > 0f))
            return 1f;
        var t = 1f - distance / (s.WeaponDistanceMultiplier * maxRange);
        return t > 1f ? 1f : Math.Max(t, s.DistanceTermMin);
    }

    private static float ConeBonus(VatsChanceSettings s, float distance, float maxConeDegrees)
    {
        var cone = Math.Min(Math.Max(maxConeDegrees / s.ConeDistanceMaxDegrees, 0f), 1f);
        var range = (1f - cone) * s.ConeDistanceMult + s.ConeDistanceOffset;
        if (!(range > 0f))
            return 0f;
        return Math.Min(Math.Max(1f - distance / range, 0f), 1f) * s.ConeDistanceBonus;
    }
}
