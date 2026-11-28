using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class EnableNativeAdapters(IScriptHost host, EvidenceGate gate, Action<string, bool> setEnabled, Action<string>? deleteReference = null)
{
    public const string SystemKey = "system.enable";
    private static readonly GameplayNativeFamily EnableFamily = Family("Enable");
    private static readonly GameplayNativeFamily DisableFamily = Family("Disable");
    private static readonly GameplayNativeFamily EnableNoWaitFamily = Family("EnableNoWait");
    private static readonly GameplayNativeFamily DisableNoWaitFamily = Family("DisableNoWait");
    private static readonly GameplayNativeFamily DeleteFamily = Family("Delete");

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        foreach (var (family, enable) in new[] { (EnableFamily, true), (DisableFamily, false), (EnableNoWaitFamily, true), (DisableNoWaitFamily, false) })
        {
            outcomes.Add(host.TryRegisterNative(family.Native, (receiver, arguments) => Set(receiver, arguments, enable), NativeTargetKind.EngineObject)
                ? new AdapterOutcome(family, AdapterState.Bound, "bound to the receiver's Garden reference")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        // Constraint: without a delete seam Delete stays unregistered, so the host refuses it by name instead of pretending the reference was removed.
        if (deleteReference is not null)
            outcomes.Add(host.TryRegisterNative(DeleteFamily.Native, (receiver, arguments) => Delete(receiver, arguments), NativeTargetKind.EngineObject)
                ? new AdapterOutcome(DeleteFamily, AdapterState.Bound, "bound to the receiver's Garden reference removal")
                : new AdapterOutcome(DeleteFamily, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private static GameplayNativeFamily Family(string name) =>
        new("enable", "Garden.Placement", SystemKey, new NativeId("ObjectReference", name));

    // Constraint: a script property holds the bare reference form key (no owner), which equals the compiled fo4_form_key; an owner, when present, must be a placed or dynamic reference.
    internal static string RefKey(PapyrusValue receiver)
    {
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the receiver is not an object reference");
        if (reference.Owner is not { } owner)
        {
            if (!reference.Identity.Contains(':') || reference.Identity.Contains('#'))
                throw new InvalidOperationException("the receiver identity is not a reference form key");
            return reference.Identity;
        }
        if (reference.Identity != owner.Key)
            throw new InvalidOperationException("the receiver has no valid Garden owner");
        if (owner.Kind is not (PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference))
            throw new InvalidOperationException($"the receiver owner kind {owner.Kind} is unsupported");
        return owner.Discriminator;
    }

    // Constraint: an owned receiver gives its reference key (placed, dynamic) or its base form key (form); other owners have no reference key.
    internal static string ObjectKey(PapyrusValue receiver)
    {
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the receiver is not an object reference");
        return reference.Owner switch
        {
            null => reference.Identity,
            { Kind: PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference } owner => owner.Discriminator,
            { Kind: PapyrusOwnerKind.Form } owner => owner.BaseFormKey,
            { } owner => throw new InvalidOperationException($"the receiver owner kind {owner.Kind} has no reference key"),
        };
    }

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, bool enable)
    {
        var refKey = RefKey(receiver);
        if (arguments.Count > 1 || arguments.Any(argument => argument.Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("Enable and Disable take at most one bool argument");
        setEnabled(refKey, enable);
        return PapyrusValue.None;
    }

    private PapyrusValue Delete(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = RefKey(receiver);
        if (arguments.Count != 0) throw new InvalidOperationException("Delete takes no arguments");
        deleteReference!(refKey);
        return PapyrusValue.None;
    }
}
