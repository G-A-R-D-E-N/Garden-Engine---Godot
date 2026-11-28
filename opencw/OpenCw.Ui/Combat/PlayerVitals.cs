using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Combat;

public enum Vital
{
    Health,
    ActionPoints,
    Rads,
}

public readonly record struct ActiveBuff(string ActorValue, double Amount, double Left);

public readonly record struct PendingHeal(double Rate, double Left, Vital Target);

public sealed record VitalsSnapshot(double BaseMaxHealth, double BaseMaxActionPoints, double Health, double ActionPoints, double Rads, IReadOnlyList<PendingHeal> Heals, IReadOnlyList<ActiveBuff> Buffs);

// Requires: main thread only; the base maxima are inputs because the derivation is secondary-source (PlayerHealth.EnsureFrom), and Tick must be called once per player update for queued changes and buffs to advance.
public sealed class PlayerVitals
{
    public PlayerVitals(double maxHealth, double health, double maxActionPoints, double actionPoints)
    {
        _maxHealth = Clean(maxHealth);
        _maxActionPoints = Clean(maxActionPoints);
        Health = Math.Min(Clean(health), MaxHealth);
        ActionPoints = Math.Min(Clean(actionPoints), MaxActionPoints);
    }

    private double _maxHealth;
    private double _maxActionPoints;
    private readonly List<Heal> _heals = [];
    private readonly List<ActiveBuff> _buffs = [];

    private readonly record struct Heal(double Rate, double Left, Vital Target);

    public event Action? Changed;

    public double Health { get; private set; }

    public double MaxHealth => Math.Max(0, _maxHealth + Buff(VitalsEffects.HealthValue));

    public double ActionPoints { get; private set; }

    public double MaxActionPoints => Math.Max(0, _maxActionPoints + Buff(VitalsEffects.ActionPointsValue));

    public double Rads { get; private set; }

    public IReadOnlyList<ActiveBuff> Buffs => _buffs;

    public VitalsSnapshot Capture() =>
        new(_maxHealth, _maxActionPoints, Health, ActionPoints, Rads, _heals.Select(heal => new PendingHeal(heal.Rate, heal.Left, heal.Target)).ToList(), [.. _buffs]);

    public void Restore(VitalsSnapshot snapshot)
    {
        _maxHealth = Clean(snapshot.BaseMaxHealth);
        _maxActionPoints = Clean(snapshot.BaseMaxActionPoints);
        _buffs.Clear();
        _heals.Clear();
        foreach (var buff in snapshot.Buffs)
            if (!string.IsNullOrWhiteSpace(buff.ActorValue) && double.IsFinite(buff.Amount) && buff.Amount != 0 && double.IsFinite(buff.Left) && buff.Left > 0) _buffs.Add(buff);
        foreach (var heal in snapshot.Heals)
            if (double.IsFinite(heal.Rate) && heal.Rate != 0 && (heal.Rate > 0 || heal.Target == Vital.Rads) && double.IsFinite(heal.Left) && heal.Left > 0 && Enum.IsDefined(heal.Target)) _heals.Add(new Heal(heal.Rate, heal.Left, heal.Target));
        Rads = Clean(snapshot.Rads);
        Health = Math.Min(Clean(snapshot.Health), MaxHealth);
        ActionPoints = Math.Min(Clean(snapshot.ActionPoints), MaxActionPoints);
        Changed?.Invoke();
    }

    public void ResetToFull(double maxHealth, double maxActionPoints)
    {
        _maxHealth = Clean(maxHealth);
        _maxActionPoints = Clean(maxActionPoints);
        ResetToFull();
    }

    public void ResetToFull()
    {
        _buffs.Clear();
        _heals.Clear();
        Rads = 0;
        Health = MaxHealth;
        ActionPoints = MaxActionPoints;
        Changed?.Invoke();
    }

    public double Buff(string actorValue) => _buffs.Where(buff => string.Equals(buff.ActorValue, actorValue, StringComparison.OrdinalIgnoreCase)).Sum(buff => buff.Amount);

    public void AddHealth(double amount) => Move(Health, amount, MaxHealth, v => Health = v);

    public void AddActionPoints(double amount) => Move(ActionPoints, amount, MaxActionPoints, v => ActionPoints = v);

    // Invariant: Rads never goes below 0 and has no upper cap.
    public void AddRads(double amount)
    {
        if (!double.IsFinite(amount)) return;
        var next = Math.Max(0, Rads + amount);
        if (next == Rads) return;
        Rads = next;
        Changed?.Invoke();
    }

    public double PendingHealth => Pending(Vital.Health);

    public double PendingRads => Pending(Vital.Rads);

    private double Pending(Vital target) => _heals.Where(heal => heal.Target == target).Sum(heal => heal.Rate * heal.Left);

    // Invariant: a heal delivers total over seconds at a constant rate; non-finite or non-positive input queues nothing.
    public void QueueHeal(double total, double seconds) => Queue(total, seconds, Vital.Health);

    public void QueueActionPoints(double total, double seconds) => Queue(total, seconds, Vital.ActionPoints);

    // Invariant: total is signed (negative lowers Rads); zero or non-finite input queues nothing.
    public void QueueRads(double total, double seconds) => Queue(total, seconds, Vital.Rads);

    private void Queue(double total, double seconds, Vital target)
    {
        if (!double.IsFinite(total) || total == 0 || (total < 0 && target != Vital.Rads) || !double.IsFinite(seconds) || seconds <= 0) return;
        _heals.Add(new Heal(total / seconds, seconds, target));
        Changed?.Invoke();
    }

    // Invariant: amount is signed and held flat for seconds (not scaled by time); a zero amount or non-positive duration adds nothing.
    public void AddBuff(string actorValue, double amount, double seconds)
    {
        if (!double.IsFinite(amount) || amount == 0 || !double.IsFinite(seconds) || seconds <= 0) return;
        _buffs.Add(new ActiveBuff(actorValue, amount, seconds));
        Changed?.Invoke();
    }

    // Invariant: buffs expire and the current values are clamped to the lowered maxima before this tick's gains land.
    public void Tick(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0 || (_heals.Count == 0 && _buffs.Count == 0)) return;
        ExpireBuffs(dt);
        if (Health <= 0) { _heals.Clear(); return; }
        var gain = 0d;
        var points = 0d;
        var rads = 0d;
        for (var i = _heals.Count - 1; i >= 0; i--)
        {
            var step = Math.Min(dt, _heals[i].Left);
            var amount = _heals[i].Rate * step;
            if (_heals[i].Target == Vital.ActionPoints) points += amount;
            else if (_heals[i].Target == Vital.Rads) rads += amount;
            else gain += amount;
            if (_heals[i].Left - step <= 0) _heals.RemoveAt(i);
            else _heals[i] = _heals[i] with { Left = _heals[i].Left - step };
        }
        AddHealth(gain);
        AddActionPoints(points);
        AddRads(rads);
    }

    private void ExpireBuffs(double dt)
    {
        if (_buffs.Count == 0) return;
        for (var i = _buffs.Count - 1; i >= 0; i--)
        {
            if (_buffs[i].Left - dt <= 0) _buffs.RemoveAt(i);
            else _buffs[i] = _buffs[i] with { Left = _buffs[i].Left - dt };
        }
        Health = Math.Min(Health, MaxHealth);
        ActionPoints = Math.Min(ActionPoints, MaxActionPoints);
        Changed?.Invoke();
    }

    public void SpendActionPoints(double cost)
    {
        if (double.IsFinite(cost) && cost > 0) Move(ActionPoints, -cost, MaxActionPoints, v => ActionPoints = v);
    }

    private void Move(double current, double amount, double max, Action<double> set)
    {
        if (!double.IsFinite(amount)) return;
        var next = Math.Clamp(current + amount, 0, max);
        if (next == current) return;
        set(next);
        Changed?.Invoke();
    }

    private static double Clean(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
