using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class PerkNativeAdapters(
    IScriptHost host, EvidenceGate gate, Func<string, bool> isPlayer, Func<string, bool> has, Func<string, bool> add, Func<string, bool> remove)
{
    public const string SystemKey = "system.perks";
    public static readonly GameplayNativeFamily AddFamily = Family("AddPerk");
    private static readonly GameplayNativeFamily RemoveFamily = Family("RemovePerk");
    private static readonly GameplayNativeFamily HasFamily = Family("HasPerk");

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        foreach (var (family, handler) in new (GameplayNativeFamily, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue>)[]
                 { (AddFamily, Add), (RemoveFamily, Remove), (HasFamily, Has) })
        {
            outcomes.Add(host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
                ? new AdapterOutcome(family, AdapterState.Bound, "bound to the player's perk store")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private static GameplayNativeFamily Family(string name) =>
        new("perk", "Garden.Character", SystemKey, new NativeId("Actor", name));

    // Constraint: a perk is a form, so it arrives as a bare form key (script property) or as a Form owner; None and any other shape are refused.
    private static string PerkKey(PapyrusValue value)
    {
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the perk is none or not a form");
        var key = reference.Identity;
        if (reference.Owner is { } owner)
        {
            if (owner.Kind != PapyrusOwnerKind.Form) throw new InvalidOperationException($"the perk owner kind {owner.Kind} is unsupported");
            key = owner.BaseFormKey;
        }
        if (string.IsNullOrWhiteSpace(key) || !key.Contains(':') || key.Contains('#'))
            throw new InvalidOperationException("the perk identity is not a form key");
        return key;
    }

    private string Prepare(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, int most, bool notify)
    {
        if (!isPlayer(EnableNativeAdapters.RefKey(receiver))) throw new InvalidOperationException("Garden keeps perks for the player only");
        return Check(arguments, most, notify);
    }

    private static string Check(IReadOnlyList<PapyrusValue> arguments, int most, bool notify)
    {
        if (arguments.Count is < 1 || arguments.Count > most || (notify && arguments.Count > 1 && arguments[1].Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("the native has the wrong arguments");
        return PerkKey(arguments[0]);
    }

    private PapyrusValue Add(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        add(Prepare(receiver, arguments, 2, true));
        return PapyrusValue.None;
    }

    private PapyrusValue Remove(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        remove(Prepare(receiver, arguments, 1, false));
        return PapyrusValue.None;
    }

    private PapyrusValue Has(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) =>
        PapyrusValue.FromBool(Check(arguments, 1, false) is var perk && IsPlayerReceiver(receiver) && has(perk));

    // Constraint: a receiver that is no placed or dynamic reference (an alias or runtime owner) is not the player, so HasPerk answers false for it; only a receiver that is not an object at all still throws.
    private bool IsPlayerReceiver(PapyrusValue receiver)
    {
        if (receiver.Kind != PapyrusValueKind.Object) throw new InvalidOperationException("the receiver is not an object reference");
        try { return isPlayer(EnableNativeAdapters.RefKey(receiver)); }
        catch (InvalidOperationException) { return false; }
    }
}
