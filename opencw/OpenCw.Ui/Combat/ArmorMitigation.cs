using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Combat;

public sealed record ArmorMitigationCurve(double BaseFactor = 0.5, double Exponent = 0.366, double MinMultiplier = 0.01, double MaxMultiplier = 0.99)
{
    private const double DefaultBaseFactor = 0.5;
    private const double DefaultExponent = 0.366;
    private const double Floor = 0.01;
    private const double Ceiling = 0.99;

    public double EffectiveBaseFactor =>
        double.IsFinite(this.BaseFactor) && this.BaseFactor > 0 ? this.BaseFactor : DefaultBaseFactor;

    public double EffectiveExponent =>
        double.IsFinite(this.Exponent) && this.Exponent > 0 ? this.Exponent : DefaultExponent;

    public double Min =>
        double.IsFinite(this.MinMultiplier) && this.MinMultiplier > 0 && this.MinMultiplier <= 1 ? this.MinMultiplier : Floor;

    public double Max
    {
        get
        {
            var ceiling = double.IsFinite(this.MaxMultiplier) && this.MaxMultiplier > 0 && this.MaxMultiplier <= 1 ? this.MaxMultiplier : Ceiling;
            return ceiling < this.Min ? this.Min : ceiling;
        }
    }
}

public sealed record DamageChannel(double RawAmount, double Resistance);

public static class ArmorMitigation
{
    private static readonly ArmorMitigationCurve DefaultCurve = new();

    public static double DamageMultiplier(double rawDamage, double resistance, ArmorMitigationCurve? curve = null)
    {
        if (!double.IsFinite(rawDamage) || rawDamage <= 0) return 0;
        var shape = curve ?? DefaultCurve;
        if (double.IsPositiveInfinity(resistance)) return shape.Min;
        if (!double.IsFinite(resistance) || resistance <= 0) return 1.0;
        var multiplier = shape.EffectiveBaseFactor * Math.Pow(rawDamage / resistance, shape.EffectiveExponent);
        return double.IsFinite(multiplier) ? Math.Clamp(multiplier, shape.Min, shape.Max) : shape.Max;
    }

    public static double Mitigate(double rawDamage, double resistance, ArmorMitigationCurve? curve = null)
    {
        if (!double.IsFinite(rawDamage) || rawDamage <= 0) return 0;
        return rawDamage * DamageMultiplier(rawDamage, resistance, curve);
    }

    public static double MitigateAll(IReadOnlyList<DamageChannel> channels, ArmorMitigationCurve? curve = null)
    {
        if (channels is null) return 0;
        var total = 0.0;
        foreach (var channel in channels)
        {
            if (channel is null) continue;
            var mitigated = Mitigate(channel.RawAmount, channel.Resistance, curve);
            if (double.IsFinite(mitigated)) total += mitigated;
        }
        return total;
    }
}
