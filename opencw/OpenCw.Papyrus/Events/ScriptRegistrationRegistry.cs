using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Events;

public enum RegistrationKind
{
    /// <summary>The owner wants a named custom event.</summary>
    CustomEvent,

    /// <summary>The owner wants a timer.</summary>
    Timer,
    RemoteEvent,
}

/// <summary>Which clock a timer runs on.</summary>
public enum ClockSource
{
    /// <summary>A real-time clock, which Garden owns.</summary>
    RealTime,

    /// <summary>The game's own clock, which is not available yet.</summary>
    GameTime,
}

/// <summary>
/// One registration, with the full payload of its kind.
/// </summary>
/// <remarks>
/// The payload is kept whole rather than flattened into a name, so a registration can be read back
/// exactly as it was made: a custom event keeps the name it wants, and a timer keeps its interval, its
/// repeat flag and its clock source.
/// </remarks>
public sealed record ScriptRegistration(
    RegistrationKind Kind,
    string EventName,
    double IntervalSeconds,
    bool Repeats,
    ClockSource Clock)
{
    public PapyrusOwnerId? Source { get; init; }

    public static ScriptRegistration ForRemoteEvent(PapyrusOwnerId source, string eventName)
    {
        if (string.IsNullOrWhiteSpace(source.BaseFormKey))
            throw new ArgumentException("a remote event needs a source owner", nameof(source));
        return new(RegistrationKind.RemoteEvent, Require(eventName), 0, false, ClockSource.RealTime) { Source = source };
    }

    public static ScriptRegistration ForCustomEvent(string eventName) =>
        new(RegistrationKind.CustomEvent, Require(eventName), 0, false, ClockSource.RealTime);

    public static ScriptRegistration ForTimer(double intervalSeconds, bool repeats,
        ClockSource clock = ClockSource.RealTime)
    {
        if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0)
        {
            throw new ArgumentException("a timer needs a positive interval", nameof(intervalSeconds));
        }
        return new ScriptRegistration(RegistrationKind.Timer, "", intervalSeconds, repeats, clock);
    }

    private static string Require(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("an event registration needs a name", nameof(value))
            : value;
}

public sealed record ScriptRegistrationEntry(long Handle, PapyrusOwnerId Owner, ScriptRegistration Registration)
{
    public double ElapsedSeconds { get; internal set; }

    public long Fires { get; internal set; }
}

/// <summary>
/// The typed registration registry.
/// </summary>
/// <remarks>
/// Every registration keeps its full payload, and dispatch matches on that payload rather than on a
/// kind: a custom-event dispatch returns exactly the registrations for that name, and a non-matching
/// name returns nothing. Timers are advanced by one owner only — the session — and an advance from
/// anywhere else is refused and counted. A real-time timer fires on accumulated time; a game-time
/// timer stays unbound and is counted until a real game-time clock and evidence exist, so it is never
/// fired against the wrong clock.
/// </remarks>
public sealed class ScriptRegistrationRegistry
{
    public const string SessionOwner = "GameSession";

    private readonly List<ScriptRegistrationEntry> _entries = new();
    private readonly Dictionary<long, ScriptRegistrationEntry> _byHandle = new();
    private long _nextHandle = 1;

    public ScriptRegistrationRegistry(bool gameTimeClockAvailable = false) =>
        GameTimeClockAvailable = gameTimeClockAvailable;

    public bool GameTimeClockAvailable { get; }

    public int Count => _entries.Count;

    public int Fired { get; private set; }

    public int RefusedForeignAdvance { get; private set; }

    public int UnboundGameTime { get; private set; }

    public IReadOnlyList<ScriptRegistrationEntry> Entries => _entries;

    public ScriptRegistrationEntry Register(PapyrusOwnerId owner, ScriptRegistration registration)
    {
        if (registration.Kind == RegistrationKind.RemoteEvent
            && (registration.Source is not { } source || string.IsNullOrWhiteSpace(source.BaseFormKey)
                || string.IsNullOrWhiteSpace(registration.EventName)))
            throw new ArgumentException("a remote event needs a source owner and event name", nameof(registration));
        if (registration.Kind == RegistrationKind.Timer
            && (!double.IsFinite(registration.IntervalSeconds) || registration.IntervalSeconds <= 0))
        {
            throw new ArgumentException("a timer needs a finite positive interval", nameof(registration));
        }
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

    public int UnregisterAll(PapyrusOwnerId owner)
    {
        var removed = _entries.Where(entry => entry.Owner == owner).ToList();
        foreach (var entry in removed)
        {
            _entries.Remove(entry);
            _byHandle.Remove(entry.Handle);
        }
        return removed.Count;
    }

    /// <summary>Every registration whose payload wants this custom event name.</summary>
    public IReadOnlyList<ScriptRegistrationEntry> MatchCustomEvent(string eventName) =>
        _entries.Where(entry => entry.Registration.Kind == RegistrationKind.CustomEvent
                                && string.Equals(entry.Registration.EventName, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Handle)
            .ToList();

    public IReadOnlyList<ScriptRegistrationEntry> MatchRemoteEvent(PapyrusOwnerId source, string eventName) =>
        _entries.Where(entry => entry.Registration.Kind == RegistrationKind.RemoteEvent
                                && entry.Registration.Source == source
                                && string.Equals(entry.Registration.EventName, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Handle)
            .ToList();

    /// <summary>Advances the timers. Only the session may do this.</summary>
    public int Advance(string owner, double deltaSeconds, ClockSource clock)
    {
        if (!string.Equals(owner, SessionOwner, StringComparison.Ordinal))
        {
            RefusedForeignAdvance++;
            throw new InvalidOperationException(
                $"only {SessionOwner} advances the script runtime, not {owner}");
        }
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "a clock cannot run backwards");
        }

        var fired = 0;
        foreach (var entry in _entries.Where(entry => entry.Registration.Kind == RegistrationKind.Timer).ToList())
        {
            if (entry.Registration.Clock != clock)
            {
                continue;
            }
            if (clock == ClockSource.GameTime && !GameTimeClockAvailable)
            {
                UnboundGameTime++;
                continue;
            }

            var elapsed = entry.ElapsedSeconds + deltaSeconds;
            if (!double.IsFinite(elapsed))
            {
                throw new OverflowException("timer elapsed time is not representable");
            }
            var interval = entry.Registration.IntervalSeconds;
            var periods = elapsed / interval;
            periods = entry.Registration.Repeats ? periods : Math.Min(1, periods);
            var fires = checked((int)Math.Floor(periods + 4 * (Math.BitIncrement(periods) - periods)));
            var batchFired = checked(fired + fires);
            var totalFired = checked(Fired + fires);
            var entryFires = checked(entry.Fires + fires);
            entry.ElapsedSeconds = Math.Max(0, elapsed - fires * interval);
            entry.Fires = entryFires;
            fired = batchFired;
            Fired = totalFired;
            if (!entry.Registration.Repeats && fires > 0)
            {
                Unregister(entry.Handle);
            }
        }
        return fired;
    }
}
