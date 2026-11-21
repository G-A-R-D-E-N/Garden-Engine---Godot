namespace OpenCommonwealth.Services.Ui.Vats;

public sealed class VatsLimbs
{
    private readonly Dictionary<string, Dictionary<byte, float>> _taken = new(StringComparer.OrdinalIgnoreCase);

    // Invariant: a missing, zero, negative or non-finite multiplier counts as 1 (UNVERIFIED default).
    public static float PartDamage(float damage, float damageMult) =>
        damage * (float.IsFinite(damageMult) && damageMult > 0 ? damageMult : 1f);

    // Invariant: 0 means the part has no health share to wear down (no max health or no percent); UNVERIFIED model, a share of the actor's max health.
    public static float Capacity(float? maxHealth, int healthPercent) =>
        maxHealth is > 0 && healthPercent > 0 ? maxHealth.Value * healthPercent / 100f : 0f;

    public static double HealthFraction(float? health, float? maxHealth) =>
        health is { } h && maxHealth is > 0 ? Math.Clamp(h / maxHealth.Value, 0.0, 1.0) : 0.0;

    public float Taken(string actorId, byte partType) =>
        _taken.TryGetValue(actorId, out var parts) && parts.TryGetValue(partType, out var taken) ? taken : 0f;

    public void Add(string actorId, byte partType, float damage)
    {
        if (!float.IsFinite(damage) || damage <= 0f) return;
        if (!_taken.TryGetValue(actorId, out var parts)) _taken[actorId] = parts = new Dictionary<byte, float>();
        parts[partType] = (parts.TryGetValue(partType, out var before) ? before : 0f) + damage;
    }

    public double Fraction(string actorId, byte partType, float capacity) =>
        capacity > 0f ? Math.Clamp(1.0 - Taken(actorId, partType) / capacity, 0.0, 1.0) : 1.0;

    public bool Crippled(string actorId, byte partType, float capacity) =>
        capacity > 0f && Taken(actorId, partType) >= capacity;

    public void Forget(string actorId) => _taken.Remove(actorId);
}
