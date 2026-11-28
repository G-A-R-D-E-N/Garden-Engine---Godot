using System;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth;

// Requires: main thread only; no Godot types so the unit tests can link this file. Confidence is the NPC_ AIDT value (xEdit wbConfidenceEnum 0 Cowardly to 4 Foolhardy); null leaves the baseline.
public static class NpcCombatProfile
{
    public const int Cowardly = 0;
    public const int Foolhardy = 4;
    // Invariant: equals NavService.ReachTolerance, the closest a NavWalker is guaranteed to stop to its goal, so a smaller range could never be closed.
    public const float MinAttackRange = 0.5f;
    // Constraint: PLACEHOLDER, no record gives this factor; see CodeNotes:Garden Engine/notes/i5-combat-chain.md.
    public const float CowardlyFleeFactor = 2f;

    public static CombatTunables From(CombatTunables baseline, WeaponInfo? weapon, double combatDistance, double unitsPerMeter, int? confidence)
    {
        var tunables = baseline;
        if (weapon is { Kind: WeaponKind.Melee or WeaponKind.Ranged } w)
        {
            bool ranged = w.Kind == WeaponKind.Ranged;
            double? range = ranged ? w.MaxRange / unitsPerMeter : WeaponReach.Meters(w.Reach, combatDistance, unitsPerMeter);
            double damage = (w.BaseDamage + (ranged ? w.AmmoDamage : 0)) * (ranged ? w.Pellets : 1);
            double cooldown = w.Scaled(ranged ? w.FireSeconds : w.AttackSeconds);
            tunables = tunables with
            {
                AttackRange = Math.Max(MinAttackRange, Pick(range, tunables.AttackRange)),
                AttackDamage = Pick(damage, tunables.AttackDamage),
                AttackCooldown = Pick(cooldown, tunables.AttackCooldown),
            };
        }
        if (confidence == Foolhardy) tunables = tunables with { FleeBelowHealth = 0f };
        else if (confidence == Cowardly) tunables = tunables with { FleeBelowHealth = Math.Min(1f, tunables.FleeBelowHealth * CowardlyFleeFactor) };
        return tunables;
    }

    private static float Pick(double? value, float fallback) =>
        value is { } d && double.IsFinite(d) && d > 0 && d <= float.MaxValue ? (float)d : fallback;
}
