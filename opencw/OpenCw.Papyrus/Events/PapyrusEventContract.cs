using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

/// <summary>Whether one event name can reach Papyrus today.</summary>
public enum EventSupportState
{
    /// <summary>Garden constructs the name and the evidence allows its delivery.</summary>
    Supported,

    /// <summary>Garden constructs the name and delivers it as a counted approximation.</summary>
    Approximated,

    /// <summary>Garden constructs the name, but an evidence key it needs does not allow delivery.</summary>
    Blocked,

    /// <summary>Garden constructs no fixed emitter for the name, so no Garden system can emit it.</summary>
    NotEmitted,
}

/// <summary>One event name Garden can construct, with the family key its route needs.</summary>
public sealed record EventEmitter(string Name, string Family, string FamilyKey);

/// <summary>What one event name resolves to, and why.</summary>
public sealed record EventSupport(string Name, string? FamilyKey, EventSupportState State, string Reason)
{
    public bool Deliverable => State == EventSupportState.Supported || State == EventSupportState.Approximated;
}

/// <summary>
/// The explicit set of events Garden can emit into Papyrus, and the rule that decides whether one is
/// deliverable.
/// </summary>
/// <remarks>
/// The set is what the code constructs, not what the engine is assumed to publish. Every entry names
/// the family key its route needs; delivery itself is decided by <see cref="PapyrusEventBridge"/>,
/// which requires an evidence row for the event's own name (<c>event.&lt;name&gt;</c>). A name is
/// deliverable only when both keys allow it, so a family that is allowed by policy still delivers
/// nothing while the event's own behaviour is unproven.
/// </remarks>
public static class PapyrusEventContract
{
    public const string KeyPrefix = "event.";

    /// <summary>The event names the library's runtimes construct, with the key their route needs.</summary>
    public static IReadOnlyList<EventEmitter> Emitters { get; } = new[]
    {
        new EventEmitter("OnActivate", "reference activation", "vm.session.activation_routing"),
        new EventEmitter("OnInit", "script lifecycle", EvidenceKeys.OnInitBarrier),
        new EventEmitter("OnLoad", "reference lifecycle", "event.reference.lifecycle"),
        new EventEmitter("OnCellAttach", "reference lifecycle", "event.reference.lifecycle"),
        new EventEmitter("OnCellDetach", "reference lifecycle", "event.reference.lifecycle"),
        new EventEmitter("OnUnload", "reference unload", "event.reference.unloaded"),
        new EventEmitter("Quest.OnStageSet", "quest alias stage event", "quest.alias.runtime"),
    };

    /// <summary>Every entry, classified against the registry's statuses.</summary>
    public static IReadOnlyList<EventSupport> Classify(EvidenceRegistry registry)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }
        return Emitters
            .OrderBy(emitter => emitter.Name, StringComparer.Ordinal)
            .Select(emitter => Evaluate(emitter, registry))
            .ToList();
    }

    /// <summary>One event name, classified; a name Garden does not construct is never deliverable.</summary>
    public static EventSupport Classify(string name, EvidenceRegistry registry)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }
        var emitter = Emitters.FirstOrDefault(entry => Handles(entry.Name, name));
        return emitter is null
            ? new EventSupport(name, null, EventSupportState.NotEmitted,
                "Garden constructs no fixed emitter for this name, so no Garden system can emit it")
            : Evaluate(emitter, registry);
    }

    /// <summary>The function a compiled script declares to receive an event: <c>::remote_Type_Event</c> for a remote event.</summary>
    public static string HandlerName(string eventName)
    {
        var dot = eventName.IndexOf('.');
        return dot < 0 ? eventName : $"::remote_{eventName[..dot]}_{eventName[(dot + 1)..]}";
    }

    /// <summary>Whether a declared handler receives an event; names compare case-insensitively, like the VM's function lookup.</summary>
    public static bool Handles(string eventName, string handler) =>
        string.Equals(HandlerName(eventName), handler, StringComparison.OrdinalIgnoreCase);

    /// <summary>The names a delivery may actually reach today.</summary>
    public static IReadOnlySet<string> Deliverable(EvidenceRegistry registry) =>
        Classify(registry)
            .Where(support => support.Deliverable)
            .Select(support => support.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static EventSupport Evaluate(EventEmitter emitter, EvidenceRegistry registry)
    {
        var family = Allowance(registry, emitter.FamilyKey);
        var own = Allowance(registry, KeyPrefix + emitter.Name);
        var state = family == EventSupportState.Blocked || own == EventSupportState.Blocked
            ? EventSupportState.Blocked
            : family == EventSupportState.Approximated || own == EventSupportState.Approximated
                ? EventSupportState.Approximated
                : EventSupportState.Supported;
        return new EventSupport(emitter.Name, emitter.FamilyKey, state,
            $"{emitter.Name} needs {emitter.FamilyKey} and {KeyPrefix}{emitter.Name}; the family key is {Describe(registry, emitter.FamilyKey)} and the event key is {Describe(registry, KeyPrefix + emitter.Name)}");
    }

    private static EventSupportState Allowance(EvidenceRegistry registry, string key)
    {
        if (!registry.TryStatus(key, out var status))
        {
            return EventSupportState.Blocked;
        }
        return status switch
        {
            EvidenceStatus.Proven => EventSupportState.Supported,
            EvidenceStatus.EnginePolicy => EventSupportState.Supported,
            EvidenceStatus.Approximated => EventSupportState.Approximated,
            _ => EventSupportState.Blocked,
        };
    }

    private static string Describe(EvidenceRegistry registry, string key) =>
        registry.TryStatus(key, out var status) ? status.ToString() : "absent, which fails closed";
}
