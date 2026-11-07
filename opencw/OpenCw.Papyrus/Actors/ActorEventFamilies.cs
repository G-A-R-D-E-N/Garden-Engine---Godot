using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

/// <summary>Which route an actor event must take.</summary>
public enum ActorEventPath
{
    /// <summary>Delivered straight to the owner.</summary>
    DirectOwner,

    /// <summary>Delivered only to an owner that registered for it.</summary>
    RegistrationRequired,
}

/// <summary>One actor event a Garden system reports.</summary>
public sealed record ActorEvent(string Name, ActorEventPath Path, string? Filter, IReadOnlyList<PapyrusValue> Arguments);

public enum ActorEventState
{
    Delivered,

    /// <summary>No matching registration exists, so the event was refused.</summary>
    Refused,

    /// <summary>The registration exists but its filter does not match.</summary>
    FilteredOut,
}

public sealed record ActorEventOutcome(
    PapyrusOwnerId Owner,
    string Name,
    ActorEventPath Path,
    ActorEventState State,
    string Reason);

public sealed record ActorEventReport(IReadOnlyList<ActorEventOutcome> Outcomes, int Delivered, int Refused, int Filtered)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"events={Outcomes.Count} delivered={Delivered} refused={Refused} filtered={Filtered}\n");
        foreach (var outcome in Outcomes)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Owner.Key} {outcome.Name} {outcome.Path.ToString().ToLowerInvariant()} {outcome.State.ToString().ToLowerInvariant()} {outcome.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Delivers the actor event families on the route each family requires.
/// </summary>
/// <remarks>
/// A direct owner event goes straight to its owner through the C2 bridge. A registration-required event
/// goes only to an owner that actually registered for it through the C2b registry, and a registration
/// whose filter does not match is filtered out rather than delivered — the register and filter behaviour
/// is never bypassed by sending directly. Both routes use the same bridge, so the gate and the counters
/// are the same.
/// </remarks>
public sealed class ActorEventFamilies
{
    public const string DirectKey = "event.actor.direct_owner";
    public const string RegisteredKey = "event.actor.registration_required";

    private readonly PapyrusEventBridge _bridge;
    private readonly ScriptRegistrationRegistry _registrations;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<(PapyrusOwnerId Owner, string Name), string> _filters = new();

    public ActorEventFamilies(
        PapyrusEventBridge bridge,
        ScriptRegistrationRegistry registrations,
        EvidenceGate gate)
    {
        _bridge = bridge;
        _registrations = registrations;
        _gate = gate;
    }

    public int Delivered { get; private set; }

    public int Refused { get; private set; }

    public int Filtered { get; private set; }

    /// <summary>Sets the filter a registration carries for one owner and event.</summary>
    public void SetFilter(PapyrusOwnerId owner, string eventName, string filter) =>
        _filters[(owner, eventName)] = filter;

    public ActorEventReport Deliver(PapyrusOwnerId owner, ActorEvent actorEvent)
    {
        var key = actorEvent.Path == ActorEventPath.DirectOwner ? DirectKey : RegisteredKey;
        if (!_gate.Gate(key).Allowed)
        {
            Refused++;
            return new ActorEventReport(new[]
            {
                new ActorEventOutcome(owner, actorEvent.Name, actorEvent.Path, ActorEventState.Refused,
                    $"the {actorEvent.Path} route is not proven, so nothing was delivered"),
            }, 0, 1, 0);
        }

        if (actorEvent.Path == ActorEventPath.DirectOwner)
        {
            return Send(owner, actorEvent, "the owner event was delivered directly");
        }

        var registration = _registrations.MatchCustomEvent(actorEvent.Name)
            .FirstOrDefault(entry => entry.Owner == owner);
        if (registration is null)
        {
            Refused++;
            return new ActorEventReport(new[]
            {
                new ActorEventOutcome(owner, actorEvent.Name, actorEvent.Path, ActorEventState.Refused,
                    "the owner did not register for this event, so it was not delivered"),
            }, 0, 1, 0);
        }

        if (_filters.TryGetValue((owner, actorEvent.Name), out var registeredFilter)
            && !string.Equals(registeredFilter, actorEvent.Filter ?? "", StringComparison.Ordinal))
        {
            Filtered++;
            return new ActorEventReport(new[]
            {
                new ActorEventOutcome(owner, actorEvent.Name, actorEvent.Path, ActorEventState.FilteredOut,
                    $"the registration's filter {registeredFilter} does not match {actorEvent.Filter}"),
            }, 0, 0, 1);
        }

        return Send(owner, actorEvent, "the registered owner received the event");
    }

    private ActorEventReport Send(PapyrusOwnerId owner, ActorEvent actorEvent, string reason)
    {
        var batch = _bridge.Send(new EngineEvent(actorEvent.Name, actorEvent.Arguments, owner));
        if (batch.Delivered > 0)
        {
            Delivered++;
            return new ActorEventReport(new[]
            {
                new ActorEventOutcome(owner, actorEvent.Name, actorEvent.Path, ActorEventState.Delivered, reason),
            }, 1, 0, 0);
        }

        Refused++;
        return new ActorEventReport(new[]
        {
            new ActorEventOutcome(owner, actorEvent.Name, actorEvent.Path, ActorEventState.Refused,
                batch.Outcomes.FirstOrDefault()?.Reason ?? batch.Outcomes.FirstOrDefault()?.Result.ToString() ?? "the event was not delivered"),
        }, 0, 1, 0);
    }
}
