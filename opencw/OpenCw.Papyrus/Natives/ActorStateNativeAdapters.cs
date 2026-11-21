using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public enum ActorStateFlag
{
    CanSpeakWhenDown,
    NoBleedoutRecovery,
}

public enum ActorStateQuery
{
    BleedingOut,
    BeingRidden,
    OnMount,
    NoBleedoutRecovery,
}

// Requires: keyed by the bare reference form key; false means the actor has no state, which the natives read as not set and ignore on write.
public interface IActorStateAccess
{
    bool TrySet(string refKey, ActorStateFlag flag, bool value);
    bool TryGet(string refKey, ActorStateQuery query, out bool value);
}

public sealed class ActorStateNativeAdapters(IScriptHost host, EvidenceGate gate, IActorStateAccess access)
{
    public const string SystemKey = "system.actorstate";

    private static GameplayNativeFamily Family(string name) =>
        new("actorstate", "Garden.Interaction", SystemKey, new NativeId("Actor", name));

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        var natives = new (string Name, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Handler)[]
        {
            ("AllowBleedoutDialogue", Setter("AllowBleedoutDialogue", ActorStateFlag.CanSpeakWhenDown)),
            ("SetNoBleedoutRecovery", Setter("SetNoBleedoutRecovery", ActorStateFlag.NoBleedoutRecovery)),
            ("GetNoBleedoutRecovery", Getter(ActorStateQuery.NoBleedoutRecovery)),
            ("IsBleedingOut", Getter(ActorStateQuery.BleedingOut)),
            ("IsBeingRidden", Getter(ActorStateQuery.BeingRidden)),
            ("IsOnMount", Getter(ActorStateQuery.OnMount)),
        };
        foreach (var (name, handler) in natives)
        {
            var family = Family(name);
            outcomes.Add(host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
                ? new AdapterOutcome(family, AdapterState.Bound, "bound to the receiver's Garden actor state")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Setter(string name, ActorStateFlag flag) => (receiver, arguments) =>
    {
        if (arguments.Count != 1 || arguments[0].Kind != PapyrusValueKind.Bool)
            throw new InvalidOperationException($"{name} takes one bool argument");
        if (TryRefKey(receiver, out var refKey)) access.TrySet(refKey, flag, arguments[0].Flag);
        return PapyrusValue.None;
    };

    private Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Getter(ActorStateQuery query) => (receiver, arguments) =>
    {
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        return PapyrusValue.FromBool(TryRefKey(receiver, out var refKey) && access.TryGet(refKey, query, out var value) && value);
    };

    private static bool TryRefKey(PapyrusValue receiver, out string refKey)
    {
        try
        {
            refKey = EnableNativeAdapters.RefKey(receiver);
            return true;
        }
        catch (InvalidOperationException)
        {
            refKey = "";
            return false;
        }
    }
}
