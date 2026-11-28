namespace OpenCommonwealth.Services.Physics;

public readonly record struct SettleFloor(float FeetY, long BodyId);

public readonly record struct SettleRayHit(long BodyId, float HitY);

public static class SpawnSettle
{
    public const float NearbyAbove = 0.05f;
    public const float MaxBelow = 2f;
    public const float MaxRise = 2.05f;
    public const float FloorProbeDepth = 80f;
    public const float AuthoredDiscardProbeDepth = MaxBelow;
    public const float SolidBetweenFloorSlack = 0.1f;

    public static bool RiseWithinReach(float spawnY, float feetY) => feetY - spawnY <= MaxRise && spawnY - feetY <= MaxRise;

    public static bool SolidBetweenBlocksRaised(float fromY, float feetY, bool rayHit, bool sameColliderAsFloor, float hitY)
    {
        if (feetY <= fromY) return false;
        if (!rayHit) return false;
        return !sameColliderAsFloor || hitY < feetY - SolidBetweenFloorSlack;
    }

    public static float? RaisedFeetIfAccepted(float spawnY, float? nearbyFeetY, float candidateRaisedFeetY, bool solidBetween)
    {
        if (nearbyFeetY is not null) return null;
        if (solidBetween || !RiseWithinReach(spawnY, candidateRaisedFeetY)) return null;
        return candidateRaisedFeetY;
    }

    public static float? Resolve(float spawnY, float? nearbyFeetY, float? raisedFeetY, bool collisionUnderPoint)
    {
        if (nearbyFeetY is float nearby) return nearby;
        if (raisedFeetY is float raised && RiseWithinReach(spawnY, raised)) return raised;
        return collisionUnderPoint ? null : spawnY;
    }

    public static float? ResolveSpawn(
        float spawnY,
        Func<float, float, SettleFloor?> settle,
        Func<float, float, SettleRayHit?> rayUp,
        Func<float, float, bool> collisionWithin)
    {
        float? nearbyY = settle(NearbyAbove, MaxBelow)?.FeetY;
        float? raisedY = null;
        if (nearbyY is null && settle(MaxRise, MaxBelow) is SettleFloor up)
        {
            var hit = rayUp(spawnY, up.FeetY);
            bool solid = SolidBetweenBlocksRaised(spawnY, up.FeetY, hit is not null, hit?.BodyId == up.BodyId, hit?.HitY ?? 0f);
            raisedY = RaisedFeetIfAccepted(spawnY, nearbyY, up.FeetY, solid);
        }
        bool collisionUnderPoint = nearbyY is not null || raisedY is not null || collisionWithin(MaxRise, AuthoredDiscardProbeDepth);
        return Resolve(spawnY, nearbyY, raisedY, collisionUnderPoint);
    }
}
