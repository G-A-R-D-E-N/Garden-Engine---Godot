using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

/// <summary>An event a Garden system produces for Papyrus owners.</summary>
public sealed record EngineEvent(string Name, IReadOnlyList<PapyrusValue> Arguments, PapyrusOwnerId? Target = null);

public enum EventDelivery
{
    Delivered,

    /// <summary>The event's evidence key does not allow delivery, so nothing was sent.</summary>
    Refused,

    /// <summary>The owner is parked, so its cell is not attached.</summary>
    SkippedParked,

    /// <summary>The owner's script does not declare the handler.</summary>
    NoHandler,

    /// <summary>No instance is registered for the owner.</summary>
    UnknownOwner,
    Pending,
}

public sealed record EventOutcome(PapyrusOwnerId Owner, EventDelivery Result, string? Reason);

public sealed record EventBatch(
    string Name,
    IReadOnlyList<EventOutcome> Outcomes,
    int Delivered,
    int Refused,
    int SkippedParked,
    int NoHandler,
    int UnknownOwner,
    int Pending = 0);

/// <summary>
/// The only route from Garden systems to owner-local Papyrus events.
/// </summary>
/// <remarks>
/// A Garden system hands the bridge an <see cref="EngineEvent"/>; it never calls the host itself, so
/// engine-produced delivery has one place to gate and one place to count. Delivery is gated on the
/// event's own evidence key, so an event whose behavior is unproven is refused and counted instead of
/// sent. A targeted event reaches only its owner; a broadcast reaches every registered owner that
/// declares the handler, skips the parked ones, and passes the arguments through verbatim.
/// </remarks>
public sealed class PapyrusEventBridge
{
    public const string KeyPrefix = "event.";

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<PapyrusOwnerId, List<ScriptInstance>> _instances = new();
    private readonly HashSet<PapyrusOwnerId> _parked = new();

    public PapyrusEventBridge(IScriptHost host, EvidenceGate gate)
    {
        _host = host;
        _gate = gate;
    }

    public int Delivered { get; private set; }

    public int Refused { get; private set; }

    public int SkippedParked { get; private set; }

    public int NoHandler { get; private set; }

    public int UnknownOwner { get; private set; }

    public int Pending { get; private set; }

    public int RegisteredOwners => _instances.Count;

    public int ParkedOwners => _parked.Count;

    public void Register(PapyrusOwnerId owner, ScriptInstance instance)
    {
        if (!_instances.TryGetValue(owner, out var instances))
            _instances[owner] = instances = new List<ScriptInstance>();
        if (!instances.Contains(instance)) instances.Add(instance);
        _parked.Remove(owner);
    }

    public void Park(PapyrusOwnerId owner) => _parked.Add(owner);

    public void Unpark(PapyrusOwnerId owner) => _parked.Remove(owner);

    public EventBatch Send(EngineEvent engineEvent)
    {
        var decision = _gate.Gate(KeyPrefix + engineEvent.Name);
        var owners = engineEvent.Target is { } target
            ? new[] { target }
            : _instances.Keys.OrderBy(owner => owner.Key, StringComparer.Ordinal).ToArray();

        var outcomes = new List<EventOutcome>(owners.Length);
        foreach (var owner in owners)
        {
            IEnumerable<ScriptInstance?> recipients = _instances.TryGetValue(owner, out var instances)
                ? instances : new ScriptInstance?[] { null };
            foreach (var instance in recipients)
            {
                if (!decision.Allowed)
                {
                    Refused++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.Refused, decision.Reason));
                    continue;
                }
                if (_parked.Contains(owner) || instance?.Detached == true)
                {
                    SkippedParked++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.SkippedParked,
                        "the owner is parked, so its cell is not attached"));
                    continue;
                }
                if (instance is null)
                {
                    UnknownOwner++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.UnknownOwner, "no instance is registered for this owner"));
                    continue;
                }
                var handler = PapyrusEventContract.HandlerName(engineEvent.Name);
                if (!instance.DefinesFunction(handler))
                {
                    NoHandler++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.NoHandler,
                        $"the script {instance.ScriptName} does not declare {handler}"));
                    continue;
                }

                var result = _host.RunEvent(instance, handler, engineEvent.Arguments.ToArray());
                if (result.Reason == VmStopReason.Waiting)
                {
                    Pending++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.Pending,
                        $"{handler} latent wait: {result.Message}"));
                    continue;
                }
                if (result.Completed)
                {
                    Delivered++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.Delivered, null));
                }
                else
                {
                    Refused++;
                    outcomes.Add(new EventOutcome(owner, EventDelivery.Refused, result.Message ?? result.Reason.ToString()));
                }
            }
        }

        return new EventBatch(engineEvent.Name, outcomes, outcomes.Count(outcome => outcome.Result == EventDelivery.Delivered),
            outcomes.Count(outcome => outcome.Result == EventDelivery.Refused),
            outcomes.Count(outcome => outcome.Result == EventDelivery.SkippedParked),
            outcomes.Count(outcome => outcome.Result == EventDelivery.NoHandler),
            outcomes.Count(outcome => outcome.Result == EventDelivery.UnknownOwner),
            outcomes.Count(outcome => outcome.Result == EventDelivery.Pending));
    }
}
