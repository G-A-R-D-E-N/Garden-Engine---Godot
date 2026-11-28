using System;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth;

public enum CombatPhase
{
    Idle,
    Alert,
    Search,
    Pursue,
    Attack,
    Flee,
}

// Constraint: every value is our own placeholder with no FO4 record behind it (UNVERIFIED); metres and seconds.
public readonly record struct CombatTunables(float AttackRange, float AttackDamage, float AttackCooldown, float AlertSeconds, float SearchSeconds, float LoseSeconds, float FleeBelowHealth, float FleeSafeDistance, float FleeMaxSeconds)
{
    public static readonly CombatTunables Default = new(2f, 5f, 1.5f, 3f, 10f, 2f, 0.15f, 20f, 20f);
}

public readonly record struct CombatSense(string? Target, bool Hostile, bool Visible, bool Aware, float Distance, float? HealthFraction, bool CanFlee = true);

public readonly record struct CombatStep(CombatPhase Phase, bool Hit);

// Requires: main thread only; no Godot types here so the unit tests can link this file.
public sealed class NpcCombatBrain(CombatTunables tunables)
{
    private float _timer;
    private float _unseen;
    private float _cooldown;
    private float _noFlee;

    public CombatTunables Tunables { get; } = tunables;
    public CombatPhase Phase { get; private set; }
    public string? Target { get; private set; }

    public event Action<CombatPhase, CombatPhase>? PhaseChanged;

    public bool Engaged => IsEngaged(Phase);

    public static bool IsEngaged(CombatPhase phase) => phase is CombatPhase.Search or CombatPhase.Pursue or CombatPhase.Attack or CombatPhase.Flee;

    public void Reset()
    {
        _timer = _unseen = _cooldown = _noFlee = 0f;
        Enter(CombatPhase.Idle);
    }

    public CombatStep Step(float dt, in CombatSense sense, IEffectTarget? victim)
    {
        if (!float.IsFinite(dt) || dt < 0f) dt = 0f;
        _cooldown = Math.Max(0f, _cooldown - dt);
        _noFlee = Math.Max(0f, _noFlee - dt);
        if (Phase == CombatPhase.Flee)
        {
            _timer += dt;
            if (_timer >= Tunables.FleeMaxSeconds)
            {
                _noFlee = Tunables.FleeMaxSeconds;
                return new(Enter(CombatPhase.Idle), false);
            }
        }
        bool wounded = sense.CanFlee && _noFlee <= 0f && sense.HealthFraction is { } health && health <= Tunables.FleeBelowHealth;
        bool fight = sense.Target is not null && sense.Hostile && !(wounded && sense.Distance >= Tunables.FleeSafeDistance);
        if (fight && sense.Visible)
        {
            _unseen = 0f;
            Target = sense.Target;
            if (wounded) return new(Enter(CombatPhase.Flee), false);
            if (sense.Distance > Tunables.AttackRange) return new(Enter(CombatPhase.Pursue), false);
            Enter(CombatPhase.Attack);
            return new(Phase, TryHit(victim));
        }
        if (fight && Phase == CombatPhase.Flee) return new(Phase, false);
        if (fight && Phase is CombatPhase.Pursue or CombatPhase.Attack)
        {
            _unseen += dt;
            return new(_unseen >= Tunables.LoseSeconds ? Enter(CombatPhase.Search) : Enter(CombatPhase.Pursue), false);
        }
        return new(Calm(dt, sense), false);
    }

    private CombatPhase Calm(float dt, in CombatSense sense)
    {
        switch (Phase)
        {
            case CombatPhase.Pursue or CombatPhase.Attack:
                return Enter(CombatPhase.Search);
            case CombatPhase.Flee:
                return Enter(CombatPhase.Idle);
            case CombatPhase.Search:
                _timer -= dt;
                return _timer <= 0f ? Enter(CombatPhase.Idle) : Phase;
            case CombatPhase.Alert:
                _timer = sense.Aware ? Tunables.AlertSeconds : _timer - dt;
                return _timer <= 0f ? Enter(CombatPhase.Idle) : Phase;
            default:
                return sense.Aware ? Enter(CombatPhase.Alert) : Phase;
        }
    }

    private bool TryHit(IEffectTarget? victim)
    {
        if (_cooldown > 0f || victim is null || victim.IsDead) return false;
        _cooldown = Tunables.AttackCooldown;
        return victim.Change(VitalsEffects.HealthValue, -Tunables.AttackDamage);
    }

    private CombatPhase Enter(CombatPhase next)
    {
        if (next == Phase) return Phase;
        var previous = Phase;
        Phase = next;
        _timer = next == CombatPhase.Search ? Tunables.SearchSeconds : next == CombatPhase.Alert ? Tunables.AlertSeconds : 0f;
        if (next == CombatPhase.Idle) Target = null;
        PhaseChanged?.Invoke(previous, next);
        return next;
    }
}
