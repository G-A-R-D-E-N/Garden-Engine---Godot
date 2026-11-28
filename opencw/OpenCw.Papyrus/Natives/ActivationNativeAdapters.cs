using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ActivationNativeAdapters(
    IScriptHost host, EvidenceGate gate, Action<string, bool, bool> setBlock, Func<string, bool> isBlocked)
{
    public const string SystemKey = "system.activation";
    public static readonly GameplayNativeFamily BlockFamily =
        new("activation", "Garden.Interaction", SystemKey, new NativeId("ObjectReference", "BlockActivation"));
    public static readonly GameplayNativeFamily IsBlockedFamily =
        new("activation", "Garden.Interaction", SystemKey, new NativeId("ObjectReference", "IsActivationBlocked"));

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        foreach (var (family, handler) in new (GameplayNativeFamily, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue>)[] { (BlockFamily, Block), (IsBlockedFamily, IsBlocked) })
        {
            outcomes.Add(host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
                ? new AdapterOutcome(family, AdapterState.Bound, "bound to the receiver's Garden reference")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private static string RefKey(PapyrusValue receiver)
    {
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference { Owner: { } owner } reference
            || reference.Identity != owner.Key)
            throw new InvalidOperationException("the receiver has no valid Garden owner");
        if (owner.Kind is not (PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference))
            throw new InvalidOperationException($"the receiver owner kind {owner.Kind} is unsupported");
        return owner.Discriminator;
    }

    private PapyrusValue Block(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = RefKey(receiver);
        if (arguments.Count > 2 || arguments.Any(argument => argument.Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("BlockActivation takes up to two bool arguments");
        setBlock(refKey, arguments.Count < 1 || arguments[0].Flag, arguments.Count > 1 && arguments[1].Flag);
        return PapyrusValue.None;
    }

    private PapyrusValue IsBlocked(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = RefKey(receiver);
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        return PapyrusValue.FromBool(isBlocked(refKey));
    }
}
