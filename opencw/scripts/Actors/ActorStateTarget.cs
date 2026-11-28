using System;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth;

// Requires: main thread only; no Godot types here so the unit tests can link this file. isValid false reads as dead.
public sealed class ActorStateTarget(ActorState state, Func<float, bool>? damage = null, Func<bool>? isValid = null) : IEffectTarget
{
    public bool IsDead => state.IsDead || isValid?.Invoke() == false;

    public bool Tracks(string actorValue) => actorValue == VitalsEffects.HealthValue;

    public bool Change(string actorValue, double amount)
    {
        if (IsDead || !Tracks(actorValue) || !double.IsFinite(amount) || amount == 0 || state.Health is not { } before) return false;
        var step = (float)Math.Abs(amount);
        if (amount < 0) _ = damage?.Invoke(step) ?? state.Damage(step, null);
        else state.Heal(step);
        return state.Health != before;
    }
}
