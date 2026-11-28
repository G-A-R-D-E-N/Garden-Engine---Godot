using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; keyed by the bare reference form key; not saved.
public sealed class PlayerTeammates
{
    private readonly HashSet<string> _teammates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _xpGivers = new(StringComparer.Ordinal);

    public int Count => _teammates.Count;

    public void Set(string refKey, bool teammate)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        if (teammate) _teammates.Add(refKey);
        else
        {
            _teammates.Remove(refKey);
            _xpGivers.Remove(refKey);
        }
    }

    // Invariant: only a current teammate can give XP, so clearing the teammate flag also drops the share.
    public void SetGivesXp(string refKey, bool gives)
    {
        ArgumentException.ThrowIfNullOrEmpty(refKey);
        if (gives && _teammates.Contains(refKey)) _xpGivers.Add(refKey);
        else _xpGivers.Remove(refKey);
    }

    public bool GivesXp(string refKey) => refKey.Length > 0 && _xpGivers.Contains(refKey);

    public bool IsTeammate(string refKey) => refKey.Length > 0 && _teammates.Contains(refKey);

    public void Clear()
    {
        _teammates.Clear();
        _xpGivers.Clear();
    }
}
