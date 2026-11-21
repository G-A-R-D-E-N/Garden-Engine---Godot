using System;

namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; the maxima are inputs, and EnsureFrom derives them from the stored SPECIAL values (secondary-source, UNVERIFIED) with the stored Health and ActionPoints as the fallback.
public static class PlayerHealth
{
    private static double? _endurance;
    private static double _damageScale = 1;

    public static double DamageScale
    {
        get => _damageScale;
        set
        {
            _damageScale = double.IsFinite(value) && value >= 0 ? value : 1;
            if (Current is { } vitals) vitals.DamageScale = _damageScale;
        }
    }

    public static PlayerVitals? Current { get; private set; }

    public static event Action? Died;

    public static IEffectTarget? Target => Current is { } vitals ? new PlayerVitalsTarget(vitals) : null;

    public static PlayerVitals? Attach(double maxHealth, double health, double maxActionPoints, double actionPoints)
    {
        if (!double.IsFinite(maxHealth) || maxHealth <= 0 || !double.IsFinite(health) || health <= 0)
        {
            Current = null;
            _endurance = null;
            return null;
        }
        _endurance = null;
        var vitals = new PlayerVitals(maxHealth, health, maxActionPoints, actionPoints) { DamageScale = _damageScale };
        vitals.Died += () => { if (ReferenceEquals(Current, vitals)) Died?.Invoke(); };
        Current = vitals;
        return vitals;
    }

    // Constraint: secondary-source formulas, UNVERIFIED against the engine (docs/code-notes/opencw-vitals.md); the 80 base and the 0.5 Endurance factor have no GMST evidence.
    public static double MaxHealthFor(double endurance, int level) =>
        Math.Floor(80 + 5 * endurance + (2.5 + endurance / 2) * (Math.Max(1, level) - 1));

    public static double MaxActionPointsFor(double agility) => 60 + 10 * agility;

    // Invariant: an attached store is kept, so reopening the Pip-Boy never refills health; no stored Health attaches nothing; a stored Endurance or Agility above 0 derives the maximum, otherwise the stored Health or ActionPoints is the fallback.
    public static PlayerVitals? EnsureFrom(Func<string, double?> stored, int level = 1)
    {
        if (Current is { } held) return held;
        if (stored(VitalsEffects.HealthValue) is not { } health) return null;
        var actionPoints = stored(VitalsEffects.ActionPointsValue) ?? 0;
        var knownEndurance = stored("Endurance") is { } endurance && double.IsFinite(endurance) && endurance > 0 ? endurance : (double?)null;
        var maxHealth = knownEndurance is { } known ? MaxHealthFor(known, level) : health;
        var maxActionPoints = stored("Agility") is { } agility && double.IsFinite(agility) && agility > 0 ? MaxActionPointsFor(agility) : actionPoints;
        var attached = Attach(maxHealth, maxHealth, maxActionPoints, maxActionPoints);
        _endurance = knownEndurance;
        return attached;
    }

    // Invariant: only a maximum derived from a stored Endurance follows the level; the current health is clamped, never raised, because the level-up heal rule is UNVERIFIED.
    public static void Relevel(int level)
    {
        if (Current is { } held && _endurance is { } endurance) held.SetBaseMaxHealth(MaxHealthFor(endurance, level));
    }

    public static void Detach()
    {
        Current = null;
        _endurance = null;
    }
}
