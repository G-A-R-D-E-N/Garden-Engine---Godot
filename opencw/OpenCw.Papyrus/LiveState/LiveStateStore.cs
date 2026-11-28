using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenCommonwealth.Services.Dialogue;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Papyrus.LiveState;

// Constraint: Sex (the CTDA sex ordinal), Ref and Items are put in before the condition call and Items must be an in-memory lookup that never reaches Radicle or the record reader.
public sealed record ActorFacts(FormKey? Base, FormKey? Race, IReadOnlyDictionary<FormKey, int>? Factions, IReadOnlySet<FormKey>? Keywords,
    double? Level, bool? Dead, FormKey? Voice = null, IReadOnlyDictionary<FormKey, double?>? Values = null,
    int? Sex = null, FormKey? Ref = null, Func<FormKey, int?>? Items = null);

// Requires: whatever a Radicle condition call may ask is put in before the call; every read below is a lookup and never calls the record reader or Radicle.
public sealed class LiveStateStore
{
    public const string PlayerKey = "player";

    private readonly ConcurrentDictionary<string, ActorFacts> _actors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<FormKey, double?> _baseline = new();

    public IQuestState? Quests { get; set; }

    public IAliasState? Aliases { get; set; }

    // Invariant: the binding is dropped only while it is still the one the caller made, so a newer session's binding is never cleared by an older owner.
    public void ClearQuestsIf(IQuestState? owned)
    {
        if (owned is not null && ReferenceEquals(Quests, owned)) Quests = null;
    }

    // Constraint: a value here is a global the game set at run time and wins over the record value; it must not read the record source.
    public Func<FormKey, double?>? GlobalOverlay { get; set; }

    public double? HourOfDay { get; set; }

    public RuntimeTrace Trace { get; } = new();

    public void PutActor(string key, ActorFacts facts) => _actors[key] = facts;

    public ActorFacts? Actor(string? key) => key is not null && _actors.TryGetValue(key, out var facts) ? facts : null;

    // Requires: read is the record-side value of a global (null when it has none) and is called here, before the condition call, never from a query.
    public void PrimeGlobals(IEnumerable<FormKey> globals, Func<FormKey, double?> read)
    {
        foreach (var global in globals) _baseline[global] = read(global);
    }

    public double? GlobalValue(FormKey global) => GlobalOverlay?.Invoke(global) ?? (_baseline.TryGetValue(global, out var value) ? value : null);

    // Invariant: a new game, a loaded save or a replaced load order drops everything primed from records or actors; the sources it reads (quests, overlay) are not owned here.
    public void Reset()
    {
        _actors.Clear();
        _baseline.Clear();
        HourOfDay = null;
    }
}
