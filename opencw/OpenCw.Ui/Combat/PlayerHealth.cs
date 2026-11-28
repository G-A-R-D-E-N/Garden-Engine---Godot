using System;

namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; the maxima are inputs, and EnsureFrom derives them from the stored SPECIAL values (secondary-source, UNVERIFIED) with the stored Health and ActionPoints as the fallback.
public static class PlayerHealth
{
    public static PlayerVitals? Current { get; private set; }

    private static VitalsSnapshot? _pending;
    private static double _defaultMaxHealth;
    private static double _defaultMaxActionPoints;
    private static double? _defaultEndurance;

    public static VitalsSnapshot? Snapshot => Current?.Capture() ?? _pending;

    public static void ApplySnapshot(VitalsSnapshot? snapshot, int level = 1)
    {
        _pending = Current is null ? snapshot : null;
        if (Current is { } current)
        {
            if (snapshot is null) current.ResetToFull(_defaultEndurance is { } endurance ? MaxHealthFor(endurance, level) : _defaultMaxHealth, _defaultMaxActionPoints);
            else current.Restore(snapshot);
        }
    }

    public static IEffectTarget? Target => Current is { } vitals ? new PlayerVitalsTarget(vitals) : null;

    public static PlayerVitals? Attach(double maxHealth, double health, double maxActionPoints, double actionPoints)
    {
        if (!double.IsFinite(maxHealth) || maxHealth <= 0 || !double.IsFinite(health) || health <= 0)
        {
            Current = null;
            return null;
        }
        _defaultMaxHealth = maxHealth;
        _defaultMaxActionPoints = maxActionPoints;
        _defaultEndurance = null;
        Current = new PlayerVitals(maxHealth, health, maxActionPoints, actionPoints);
        if (_pending is { } saved)
        {
            Current.Restore(saved);
            _pending = null;
        }
        return Current;
    }

    // Constraint: secondary-source formulas, UNVERIFIED against the engine (CodeNotes:Garden Engine/history/docs/code-notes/opencw-vitals.md); the 80 base and the 0.5 Endurance factor have no GMST evidence.
    public static double MaxHealthFor(double endurance, int level) =>
        Math.Floor(80 + 5 * endurance + (2.5 + endurance / 2) * (Math.Max(1, level) - 1));

    public static double MaxActionPointsFor(double agility) => 60 + 10 * agility;

    // Invariant: an attached store is kept, so reopening the Pip-Boy never refills health; no stored Health attaches nothing; a stored Endurance or Agility above 0 derives the maximum, otherwise the stored Health or ActionPoints is the fallback.
    public static PlayerVitals? EnsureFrom(Func<string, double?> stored, int level = 1)
    {
        if (Current is { } held) return held;
        if (stored(VitalsEffects.HealthValue) is not { } health || !double.IsFinite(health) || health <= 0)
        {
            if (_pending is { BaseMaxHealth: > 0 } saved)
                return Attach(saved.BaseMaxHealth, saved.BaseMaxHealth, saved.BaseMaxActionPoints, saved.BaseMaxActionPoints);
            return null;
        }
        var actionPoints = stored(VitalsEffects.ActionPointsValue) ?? 0;
        var endurance = stored("Endurance");
        var validEndurance = endurance is { } value && double.IsFinite(value) && value > 0 ? value : (double?)null;
        var maxHealth = validEndurance is { } baseEndurance ? MaxHealthFor(baseEndurance, level) : health;
        var maxActionPoints = stored("Agility") is { } agility && double.IsFinite(agility) && agility > 0 ? MaxActionPointsFor(agility) : actionPoints;
        var attached = Attach(maxHealth, maxHealth, maxActionPoints, maxActionPoints);
        if (attached is not null) _defaultEndurance = validEndurance;
        return attached;
    }

    public static void Detach()
    {
        Current = null;
        _pending = null;
    }
}
