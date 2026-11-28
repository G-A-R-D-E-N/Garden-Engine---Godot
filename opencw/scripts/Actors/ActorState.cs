using System;

namespace OpenCommonwealth;

public enum ActorLife
{
    Alive,
    Dead,
    EssentialDown,
}

// Invariant: the numbers are the engine's KNOCK_STATE_ENUM values (commonlibf4-nomad), which NpcStates.KnockedOut and PickpocketTarget.KnockState compare as plain ints.
public enum ActorKnock
{
    Normal = 0,
    Explode = 1,
    ExplodeLeadIn = 2,
    Out = 3,
    OutLeadIn = 4,
    Queued = 5,
    GetUp = 6,
    Down = 7,
    WaitForTaskQueue = 8,
}

public enum ActorProtection
{
    None,
    Essential,
    Protected,
    Invulnerable,
}

public sealed class ActorState
{
    public ActorState(ushort level, string? levelUnresolved)
    {
        Level = level;
        LevelUnresolved = levelUnresolved;
    }

    public ushort Level { get; }
    public string? LevelUnresolved { get; }
    public float? Health { get; private set; }
    public float? MaxHealth { get; private set; }
    public ActorProtection Protection { get; set; }
    public ActorLife Life { get; private set; }
    public string? Killer { get; private set; }
    public bool IsDead => Life == ActorLife.Dead;
    public bool IsEssentialDown => Life == ActorLife.EssentialDown;
    public bool CanSpeakWhenDown { get; set; }
    public bool NoBleedoutRecovery { get; set; }
    public bool IgnoreFriendlyHits { get; set; }
    public bool NotShowOnStealthMeter { get; set; }
    public bool IsMount { get; set; }
    public string? Rider { get; set; }
    public ActorKnock Knock => _knockLeft > 0f ? _knock : ActorKnock.Normal;
    public bool Staggered => _staggerLeft > 0f;
    public bool Timed => IsEssentialDown || _knockLeft > 0f || _staggerLeft > 0f;

    private ActorKnock _knock;
    private float _knockLeft;
    private float _staggerLeft;
    private float _downFor;

    public event Action<ActorState>? Died;

    public void SetHealth(float max)
    {
        if (!float.IsFinite(max) || max <= 0f) throw new ArgumentOutOfRangeException(nameof(max));
        MaxHealth = max;
        Health = max;
    }

    public bool Damage(float amount, string? source)
    {
        if (IsDead || Protection == ActorProtection.Invulnerable || Health is not { } health || !float.IsFinite(amount) || amount <= 0f) return false;
        bool floored = Protection is ActorProtection.Essential or ActorProtection.Protected;
        Health = Math.Max(floored ? Math.Min(health, 1f) : 0f, health - amount);
        if (Health <= 0f) Die(source);
        else if (Protection == ActorProtection.Essential && Health <= 1f && Life == ActorLife.Alive) GoDown();
        return true;
    }

    public bool Heal(float amount)
    {
        if (IsDead || !float.IsFinite(amount) || amount <= 0f || Health is not { } health || MaxHealth is not { } max || health >= max) return false;
        Health = Math.Min(max, health + amount);
        return true;
    }

    public bool SetKnock(ActorKnock knock, float seconds)
    {
        if (IsDead || !float.IsFinite(seconds) || seconds <= 0f) return false;
        _knock = knock;
        _knockLeft = knock == ActorKnock.Normal ? 0f : seconds;
        return true;
    }

    public bool Stagger(float seconds)
    {
        if (IsDead || !float.IsFinite(seconds) || seconds <= 0f) return false;
        _staggerLeft = seconds;
        return true;
    }

    public void Tick(float seconds, float recoverSeconds, float regainFraction)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f) return;
        _knockLeft = Math.Max(0f, _knockLeft - seconds);
        _staggerLeft = Math.Max(0f, _staggerLeft - seconds);
        if (Life != ActorLife.EssentialDown || NoBleedoutRecovery) return;
        _downFor += seconds;
        if (_downFor < recoverSeconds || Health is not { } health || MaxHealth is not { } max) return;
        Health = Math.Max(health, max * Math.Clamp(regainFraction, 0f, 1f));
        Life = ActorLife.Alive;
        _downFor = 0f;
    }

    public bool Kill(string? source)
    {
        if (IsDead) return false;
        Health = 0f;
        Die(source);
        return true;
    }

    // Must not raise Died.
    public void RestoreDead()
    {
        Health = 0f;
        Life = ActorLife.Dead;
    }

    private void GoDown()
    {
        Life = ActorLife.EssentialDown;
        _downFor = 0f;
    }

    private void Die(string? source)
    {
        Life = ActorLife.Dead;
        Killer = source;
        Died?.Invoke(this);
    }
}

public static class ActorSpawnRules
{
    // Invariant: bits are NPC_ ACBS ConfigurationFlags 1 Essential, 11 Protected, 31 Invulnerable (Radicle docs/NPCS.md); Essential wins over Protected.
    internal static ActorProtection ProtectionOf(uint configurationFlags) =>
        (configurationFlags & 1u << 31) != 0 ? ActorProtection.Invulnerable
        : (configurationFlags & 1u << 1) != 0 ? ActorProtection.Essential
        : (configurationFlags & 1u << 11) != 0 ? ActorProtection.Protected
        : ActorProtection.None;

    internal static float? ProvenHealth(float? health) => health is { } value && float.IsFinite(value) && value > 0f ? value : null;

    internal static ActorState StateFor(ushort level, string? levelUnresolved, float? health, ActorProtection? protection)
    {
        var state = new ActorState(level, levelUnresolved);
        if (levelUnresolved == null && ProvenHealth(health) is { } proven && protection is { } known)
        {
            state.Protection = known;
            state.SetHealth(proven);
        }
        return state;
    }
}
