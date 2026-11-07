using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

/// <summary>A reference lifecycle transition a Garden system reports.</summary>
public enum LifecycleTransition
{
    Loaded,
    Unloaded,
    CellAttached,
    CellDetached,
}

public sealed record LifecycleOutcome(
    PapyrusOwnerId Owner,
    LifecycleTransition Transition,
    string? EventName,
    bool Delivered,
    bool Parked,
    string? Reason);

public sealed record LifecycleReport(
    IReadOnlyList<LifecycleOutcome> Outcomes,
    int Delivered,
    int Parked,
    int UnloadedUncertainty)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"transitions={Outcomes.Count} delivered={Delivered} parked={Parked} unloaded-uncertainty={UnloadedUncertainty}\n");
        foreach (var outcome in Outcomes)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Owner.Key} {outcome.Transition.ToString().ToLowerInvariant()} event={outcome.EventName} delivered={outcome.Delivered} parked={outcome.Parked} {outcome.Reason}\n");
        }
        return text.ToString();
    }
}

/// <summary>
/// Delivers reference lifecycle events through the C2 bridge.
/// </summary>
/// <remarks>
/// The driver never touches the host: every event goes through <see cref="PapyrusEventBridge"/>, so
/// lifecycle delivery has the same gate, the same targets and the same counters as every other
/// engine-produced event. Loading, attaching and detaching deliver their event when the gate allows it.
/// An unloaded reference is the one uncertain case: while its behavior is unproven the owner is parked
/// and the uncertainty is counted, and the count disappears only when the evidence key actually allows
/// the event — so the temporary parking policy is replaced by proof, not by assumption.
/// </remarks>
public sealed class ReferenceLifecycle
{
    public const string UnloadedKey = "event.reference.unloaded";
    public const string LifecycleKey = "event.reference.lifecycle";

    private readonly PapyrusEventBridge _bridge;
    private readonly EvidenceGate _gate;

    public ReferenceLifecycle(PapyrusEventBridge bridge, EvidenceGate gate)
    {
        _bridge = bridge;
        _gate = gate;
    }

    public int Delivered { get; private set; }

    public int Parked { get; private set; }

    /// <summary>The unloaded-reference uncertainty, which only proof can remove.</summary>
    public int UnloadedUncertainty { get; private set; }

    public LifecycleReport Report(IEnumerable<LifecycleOutcome> outcomes)
    {
        var list = outcomes.ToList();
        return new LifecycleReport(list, list.Count(outcome => outcome.Delivered),
            list.Count(outcome => outcome.Parked), UnloadedUncertainty);
    }

    public LifecycleOutcome Transition(PapyrusOwnerId owner, LifecycleTransition transition)
    {
        if (transition == LifecycleTransition.Unloaded)
        {
            return Unload(owner);
        }

        var eventName = transition switch
        {
            LifecycleTransition.Loaded => "OnLoad",
            LifecycleTransition.CellAttached => "OnCellAttach",
            _ => "OnCellDetach",
        };

        if (!_gate.Gate(LifecycleKey).Allowed)
        {
            return new LifecycleOutcome(owner, transition, eventName, false, false,
                "the lifecycle family is not proven, so nothing was delivered");
        }

        var batch = _bridge.Send(new EngineEvent(eventName, Array.Empty<PapyrusValue>(), owner));
        var delivered = batch.Delivered > 0;
        if (delivered)
        {
            Delivered++;
        }
        return new LifecycleOutcome(owner, transition, eventName, delivered, false,
            delivered ? null : batch.Outcomes.FirstOrDefault()?.Result.ToString());
    }

    private LifecycleOutcome Unload(PapyrusOwnerId owner)
    {
        if (_gate.Gate(UnloadedKey).Allowed)
        {
            var batch = _bridge.Send(new EngineEvent("OnUnload", Array.Empty<PapyrusValue>(), owner));
            var delivered = batch.Delivered > 0;
            if (delivered)
            {
                Delivered++;
            }
            return new LifecycleOutcome(owner, LifecycleTransition.Unloaded, "OnUnload", delivered, false,
                delivered ? null : batch.Outcomes.FirstOrDefault()?.Result.ToString());
        }

        _bridge.Park(owner);
        Parked++;
        UnloadedUncertainty++;
        return new LifecycleOutcome(owner, LifecycleTransition.Unloaded, "OnUnload", false, true,
            "the unloaded-reference behavior is unproven, so the owner is parked and the uncertainty is counted");
    }
}
