namespace OpenCommonwealth.Services.Combat;

// Constraint: only a gunshot is heard by perception on its own; a landed melee swing is heard through ActorCombat.Damage, and a throw or mine is quiet until it detonates.
public static class AttackNoise
{
    public static bool Makes(AttackKind kind) => kind == AttackKind.Shot;
}
