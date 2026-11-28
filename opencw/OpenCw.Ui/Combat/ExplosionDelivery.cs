using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Combat;

public sealed record ExplosionTarget(string Id, double Distance, double Resistance = 0);

public sealed record ExplosionHit(string TargetId, double Falloff, double MitigatedDamage, double Force, EffectSpec? Effect);

public sealed record ProximityMineConfig(double ArmDelaySeconds = 0.75, double TriggerRadius = 2.0);

public static class ExplosionDelivery
{
    public static double FalloffAt(ExplosionInfo explosion, double distance)
    {
        if (explosion is null) return 0;
        if (!double.IsFinite(distance) || distance < 0) return 0;
        var outer = (double)explosion.OuterRadius;
        if (!double.IsFinite(outer) || outer <= 0) return 0;
        if (distance >= outer) return 0;
        var inner = double.IsFinite(explosion.InnerRadius) ? Math.Max(0.0, explosion.InnerRadius) : 0.0;
        if (distance <= inner) return 1.0;
        return 1.0 - (distance - inner) / (outer - inner);
    }

    public static IReadOnlyList<ExplosionHit> ResolveHits(ExplosionInfo explosion, IReadOnlyList<ExplosionTarget> targets, ArmorMitigationCurve? curve = null)
    {
        var hits = new List<ExplosionHit>();
        if (explosion is null || targets is null) return hits;
        foreach (var target in targets)
        {
            if (target is null) continue;
            var falloff = FalloffAt(explosion, target.Distance);
            if (falloff <= 0) continue;
            var mitigated = ArmorMitigation.Mitigate(explosion.Damage * falloff, target.Resistance, curve);
            var force = float.IsFinite(explosion.Force) && explosion.Force > 0 ? explosion.Force * falloff : 0;
            var effect = mitigated > 0 ? new EffectSpec(VitalsEffects.HealthValue, EffectKind.Damage, mitigated) : null;
            hits.Add(new ExplosionHit(target.Id, falloff, mitigated, force, effect));
        }
        return hits;
    }
}

public sealed class ProximityMineState(ProximityMineConfig config)
{
    private readonly ProximityMineConfig _config = config ?? new ProximityMineConfig();

    public bool Armed { get; private set; }

    public bool Detonated { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public bool Advance(double deltaSeconds, double nearestTargetDistance)
    {
        if (Detonated) return false;
        if (double.IsFinite(deltaSeconds) && deltaSeconds > 0) ElapsedSeconds += deltaSeconds;
        if (!Armed && ElapsedSeconds >= ArmDelay) Armed = true;
        if (!Armed) return false;
        if (!double.IsFinite(nearestTargetDistance) || nearestTargetDistance < 0) return false;
        if (nearestTargetDistance > TriggerRadius) return false;
        Detonated = true;
        return true;
    }

    private double ArmDelay =>
        double.IsFinite(_config.ArmDelaySeconds) && _config.ArmDelaySeconds > 0 ? _config.ArmDelaySeconds : 0;

    private double TriggerRadius =>
        double.IsFinite(_config.TriggerRadius) && _config.TriggerRadius > 0 ? _config.TriggerRadius : 0;
}
