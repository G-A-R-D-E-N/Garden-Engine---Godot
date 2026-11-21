namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; Change returns true only when a tracked value actually moved.
public interface IEffectTarget
{
    bool IsDead { get; }

    bool Tracks(string actorValue);

    bool Change(string actorValue, double amount);
}

// Requires: main thread only; a player at 0 health is dead for effects and accepts no further change.
public sealed class PlayerVitalsTarget(PlayerVitals vitals) : IEffectTarget
{
    public bool IsDead => vitals.Health <= 0;

    public bool Tracks(string actorValue) => actorValue is VitalsEffects.HealthValue or VitalsEffects.ActionPointsValue;

    public bool Change(string actorValue, double amount)
    {
        if (IsDead || !double.IsFinite(amount) || !Tracks(actorValue)) return false;
        var health = actorValue == VitalsEffects.HealthValue;
        var before = health ? vitals.Health : vitals.ActionPoints;
        if (health) vitals.AddHealth(amount);
        else vitals.AddActionPoints(amount);
        return (health ? vitals.Health : vitals.ActionPoints) != before;
    }
}
