using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public interface IActorLifeSource
{
    bool TryIsDead(PapyrusOwnerId owner, out bool dead);
}

public sealed class CombatNativeAdapters
{
    public const string SystemKey = "system.combat";
    public static readonly GameplayNativeFamily IsDeadFamily =
        new("combat", "Garden.Combat", SystemKey, new NativeId("Actor", "IsDead"));

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly IActorLifeSource _life;

    public CombatNativeAdapters(IScriptHost host, EvidenceGate gate, IActorLifeSource life)
    {
        _host = host;
        _gate = gate;
        _life = life;
    }

    public AdapterReport Bind()
    {
        // Requires: this gate call stays; it counts the reliance on the system key.
        _gate.Gate(SystemKey);
        if (!_host.TryRegisterNative(IsDeadFamily.Native, IsDead, NativeTargetKind.EngineObject))
            return Report(AdapterState.Conflict, "registration conflict: the existing handler was preserved");
        var outcome = NativeBinding.Published(_host, IsDeadFamily, "bound to the receiver's Garden owner");
        return Report(outcome.State, outcome.Reason);
    }

    private static AdapterReport Report(AdapterState state, string reason)
    {
        var bound = state == AdapterState.Bound;
        return new AdapterReport(new[] { new AdapterOutcome(IsDeadFamily, state, reason) }, bound ? 1 : 0, bound ? 0 : 1);
    }

    private PapyrusValue IsDead(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference { Owner: { } owner } reference
            || reference.Identity != owner.Key)
            throw new InvalidOperationException("the receiver has no valid Garden owner");
        if (owner.Kind is not (PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference))
            throw new InvalidOperationException($"the receiver owner kind {owner.Kind} is unsupported");
        return _life.TryIsDead(owner, out var dead)
            ? PapyrusValue.FromBool(dead)
            : throw new InvalidOperationException("Garden has no actor state for the receiver");
    }
}
