using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Combat;

public enum EffectKind
{
    Damage,
    Restore,
}

// Requires: the caller supplies verified numbers; DurationSeconds 0 applies Amount once, above 0 spreads Amount as a total across the duration.
public sealed record EffectSpec(string ActorValue, EffectKind Kind, double Amount, double DurationSeconds = 0);

public sealed record EffectOutcome(bool Accepted, string? Reason);

// Requires: main thread only; the caller ticks it, see CodeNotes:Garden Engine/history/docs/code-notes/opencw-effects.md.
public sealed class EffectApplier
{
    private readonly List<Running> _running = [];

    public int Active => _running.Count;

    public EffectOutcome Apply(IEffectTarget target, EffectSpec spec)
    {
        if (Refusal(target, spec) is { } reason) return new EffectOutcome(false, reason);
        var total = spec.Kind == EffectKind.Damage ? -spec.Amount : spec.Amount;
        if (spec.DurationSeconds == 0)
            return target.Change(spec.ActorValue, total) ? new EffectOutcome(true, null) : new EffectOutcome(false, "target unchanged");
        _running.Add(new Running(target, spec.ActorValue, total, spec.DurationSeconds));
        return new EffectOutcome(true, null);
    }

    // Invariant: the delivered total comes from elapsed time, so uneven ticks deliver exactly the total and never more.
    public void Tick(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        for (var i = _running.Count - 1; i >= 0; i--)
        {
            var effect = _running[i];
            if (effect.Target.IsDead || effect.Advance(seconds)) _running.RemoveAt(i);
        }
    }

    private static string? Refusal(IEffectTarget target, EffectSpec spec)
    {
        if (string.IsNullOrEmpty(spec.ActorValue)) return "no actor value";
        if (!double.IsFinite(spec.Amount) || spec.Amount <= 0) return "amount is not a positive number";
        if (!double.IsFinite(spec.DurationSeconds) || spec.DurationSeconds < 0) return "duration is not a non-negative number";
        if (target.IsDead) return "target is dead";
        return target.Tracks(spec.ActorValue) ? null : string.Create(CultureInfo.InvariantCulture, $"actor value {spec.ActorValue} not tracked");
    }

    private sealed class Running(IEffectTarget target, string actorValue, double total, double duration)
    {
        private double _elapsed;
        private double _delivered;

        public IEffectTarget Target => target;

        public bool Advance(double seconds)
        {
            _elapsed = Math.Min(duration, _elapsed + seconds);
            var due = _elapsed >= duration ? total : total * (_elapsed / duration);
            var slice = due - _delivered;
            _delivered = due;
            if (slice != 0) target.Change(actorValue, slice);
            return _elapsed >= duration;
        }
    }
}
