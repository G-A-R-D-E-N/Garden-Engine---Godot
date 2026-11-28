using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Events;

public sealed record ScriptRegistration(string EventName)
{
    public static ScriptRegistration ForCustomEvent(string eventName) =>
        new(string.IsNullOrWhiteSpace(eventName)
            ? throw new ArgumentException("an event registration needs a name", nameof(eventName))
            : eventName);
}

public sealed record ScriptRegistrationEntry(long Handle, PapyrusOwnerId Owner, ScriptRegistration Registration);

public sealed class ScriptRegistrationRegistry
{
    private readonly List<ScriptRegistrationEntry> _entries = new();
    private readonly Dictionary<long, ScriptRegistrationEntry> _byHandle = new();
    private long _nextHandle = 1;

    public int Count => _entries.Count;

    public IReadOnlyList<ScriptRegistrationEntry> Entries => _entries;

    public ScriptRegistrationEntry Register(PapyrusOwnerId owner, ScriptRegistration registration)
    {
        var entry = new ScriptRegistrationEntry(_nextHandle++, owner, registration);
        _entries.Add(entry);
        _byHandle[entry.Handle] = entry;
        return entry;
    }

    public bool Unregister(long handle)
    {
        if (!_byHandle.Remove(handle, out var entry))
        {
            return false;
        }
        _entries.Remove(entry);
        return true;
    }

    public IReadOnlyList<ScriptRegistrationEntry> MatchCustomEvent(string eventName) =>
        _entries.Where(entry => string.Equals(entry.Registration.EventName, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Handle)
            .ToList();
}
