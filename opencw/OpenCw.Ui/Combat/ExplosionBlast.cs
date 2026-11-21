using System;

namespace OpenCommonwealth.Services.Combat;

public static class ExplosionBlast
{
    // Constraint: full damage inside InnerRadius, linear to zero at OuterRadius, radii in FO4 units; the falloff shape is a tuning default, UNVERIFIED against the engine.
    public static float DamageAt(ExplosionInfo info, float distanceUnits)
    {
        if (!float.IsFinite(distanceUnits) || distanceUnits < 0 || !float.IsFinite(info.Damage) || info.Damage <= 0) return 0f;
        var inner = Math.Max(0f, info.InnerRadius);
        var outer = Math.Max(inner, info.OuterRadius);
        if (distanceUnits <= inner) return info.Damage;
        if (distanceUnits >= outer) return 0f;
        return info.Damage * (outer - distanceUnits) / (outer - inner);
    }
}
