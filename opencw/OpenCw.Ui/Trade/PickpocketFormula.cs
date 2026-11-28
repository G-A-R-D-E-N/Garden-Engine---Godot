using System;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record PickpocketSettings(
    float ActorSkillBase = 50,
    float ActorSkillMult = 5,
    float TargetSkillBase = 0,
    float TargetSkillMult = -5,
    float AmountBase = 0,
    float AmountMult = -0.25f,
    float WeightBase = 0,
    float WeightMult = -0.5f,
    float MinChance = 5,
    float MaxChance = 95,
    float PlantChanceMult = 1)
{
    public const string FloatPath = "GMST\\FloatValue";

    public static PickpocketSettings Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var d = new PickpocketSettings();
        return new PickpocketSettings(
            Value(reader, "fPickPocketActorSkillBase", d.ActorSkillBase),
            Value(reader, "fPickPocketActorSkillMult", d.ActorSkillMult),
            Value(reader, "fPickPocketTargetSkillBase", d.TargetSkillBase),
            Value(reader, "fPickPocketTargetSkillMult", d.TargetSkillMult),
            Value(reader, "fPickPocketAmountBase", d.AmountBase),
            Value(reader, "fPickPocketAmountMult", d.AmountMult),
            Value(reader, "fPickPocketWeightBase", d.WeightBase),
            Value(reader, "fPickPocketWeightMult", d.WeightMult),
            Value(reader, "fPickPocketMinChance", d.MinChance),
            Value(reader, "fPickPocketMaxChance", d.MaxChance),
            Value(reader, "fPickPocketPlantChanceMult", d.PlantChanceMult));
    }

    internal static float Value(IRecordReader reader, string editorId, float fallback)
    {
        try
        {
            return reader.FindEditorId(editorId, "GMST") is { } id
                && float.TryParse(reader.Scalar(id, FloatPath), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && float.IsFinite(value) ? value : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

public static class PickpocketFormula
{
    public const int RollCount = 100;

    // Invariant: single-precision, term order as the exe; the perk entry point (modify pickpocket chance) runs after the linear sum and before the plant multiplier.
    public static int Chance(
        PickpocketSettings s, float thiefAgility, float targetPerception, int value, float weight, bool placing, Func<float, float>? perk = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        var chance = s.TargetSkillMult * targetPerception + s.TargetSkillBase + s.ActorSkillMult * thiefAgility + s.ActorSkillBase
            + (float)value * s.AmountMult + s.AmountBase + s.WeightMult * weight + s.WeightBase;
        if (perk is not null) chance = perk(chance);
        if (placing) chance *= s.PlantChanceMult;
        if (!(chance <= s.MaxChance)) chance = s.MaxChance;
        else if (chance < s.MinChance) chance = s.MinChance;
        var whole = (int)chance;
        return whole + (chance - whole >= 0.5f ? 1 : 0);
    }

    public static int StolenValue(int unitValue, int count) => (int)Math.Clamp((long)unitValue * Math.Max(count, 0), 1L, int.MaxValue);

    public static float StolenWeight(double unitWeight, int count) => (float)(unitWeight * Math.Max(count, 0));

    // Requires: roll is uniform in 0..RollCount-1.
    public static bool Succeeds(int chance, int roll) => roll < chance;
}
