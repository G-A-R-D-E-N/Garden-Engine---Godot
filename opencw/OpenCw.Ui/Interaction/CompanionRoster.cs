using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

public enum CompanionState { Follow, Wait }

public enum CompanionDistance { Near, Medium, Far }

public enum CompanionStance { Defensive, Aggressive }

public sealed record CompanionEntry(string RefKey, string BaseKey, CompanionState State, CompanionDistance Distance, CompanionStance Stance);

// Requires: main thread only; keyed by the bare reference form key as PlayerTeammates is; one human companion slot.
public sealed class CompanionRoster
{
    private readonly HashSet<string> _hasBeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _disallowed = new(StringComparer.Ordinal);
    private CompanionEntry? _current;

    public event Action? Changed;

    public CompanionEntry? Current => _current;

    public bool IsCompanion(string refKey) => refKey.Length > 0 && _current?.RefKey == refKey;

    // Invariant: a roster member is a teammate whether or not the SetPlayerTeammate native set the flag; a dismissed actor is a teammate only when the flag is set.
    public bool IsTeammate(string refKey, bool setByScript) => setByScript || IsCompanion(refKey);

    public bool HasBeenCompanion(string refKey) => _hasBeen.Contains(refKey);

    public bool IsDisallowed(string refKey) => _disallowed.Contains(refKey);

    public void SetDisallowed(string refKey, bool disallowed)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        if (disallowed) _disallowed.Add(refKey);
        else _disallowed.Remove(refKey);
    }

    // Invariant: a disallowed actor is refused; recruiting the current companion again keeps its state; recruiting another actor replaces the current companion.
    public bool Recruit(string refKey, string baseKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        ArgumentException.ThrowIfNullOrEmpty(baseKey);
        if (_disallowed.Contains(refKey) || IsCompanion(refKey)) return false;
        _current = new CompanionEntry(refKey, baseKey, CompanionState.Follow, CompanionDistance.Medium, CompanionStance.Defensive);
        _hasBeen.Add(refKey);
        Changed?.Invoke();
        return true;
    }

    public bool Dismiss(string refKey)
    {
        if (!IsCompanion(refKey)) return false;
        _current = null;
        Changed?.Invoke();
        return true;
    }

    public bool Configure(string refKey, CompanionState state, CompanionDistance distance, CompanionStance stance)
    {
        if (_current is not { } entry || !IsCompanion(refKey)) return false;
        _current = entry with { State = state, Distance = distance, Stance = stance };
        Changed?.Invoke();
        return true;
    }

    // Requires: called after ContainerLedger.Reload has cleared the roster; sets the saved entry as written, without the recruit rules.
    public void Restore(CompanionEntry? entry)
    {
        _current = entry;
        if (entry is not null) _hasBeen.Add(entry.RefKey);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _current = null;
        _hasBeen.Clear();
        _disallowed.Clear();
        Changed?.Invoke();
    }
}
