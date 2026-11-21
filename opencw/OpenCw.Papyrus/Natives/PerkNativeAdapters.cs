using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class PerkNativeAdapters(
    IScriptHost host, EvidenceGate gate, Func<string, bool> isPlayer, Func<string, bool> has, Func<string, bool> add, Func<string, bool> remove,
    Func<string, string, bool?>? actorHas = null, Action<string, string, bool>? actorSet = null)
{
    public const string SystemKey = "system.perks";
    private static readonly GameplayNativeFamily AddFamily = Family("AddPerk");
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
                ? NativeBinding.Published(host, family, "bound to the player's perk store")
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

    // Constraint: a receiver that is not the player is accepted only when the actor delegates were supplied; otherwise the write is refused.
    private (string RefKey, bool Player) Target(PapyrusValue receiver)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        var player = isPlayer(refKey);
        if (!player && actorSet is null) throw new InvalidOperationException("Garden keeps perks for the player only");
        return (refKey, player);
    }

    private static string Check(IReadOnlyList<PapyrusValue> arguments, int most, bool notify)
    {
        if (arguments.Count is < 1 || arguments.Count > most || (notify && arguments.Count > 1 && arguments[1].Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("the native has the wrong arguments");
        return PerkKey(arguments[0]);
    }

    private PapyrusValue Add(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (refKey, player) = Target(receiver);
        var perk = Check(arguments, 2, true);
        if (player) add(perk);
        else actorSet!(refKey, perk, true);
        return PapyrusValue.None;
    }

    private PapyrusValue Remove(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (refKey, player) = Target(receiver);
        var perk = Check(arguments, 1, false);
        if (player) remove(perk);
        else actorSet!(refKey, perk, false);
        return PapyrusValue.None;
    }

    // Constraint: a receiver that is no placed or dynamic reference (an alias or runtime owner) is not an actor, so HasPerk answers false for it; with actor delegates an actor whose perks cannot be read throws instead of answering false.
    private PapyrusValue Has(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var perk = Check(arguments, 1, false);
        if (receiver.Kind != PapyrusValueKind.Object) throw new InvalidOperationException("the receiver is not an object reference");
        string refKey;
        try { refKey = EnableNativeAdapters.RefKey(receiver); }
        catch (InvalidOperationException) { return PapyrusValue.FromBool(false); }
        if (isPlayer(refKey)) return PapyrusValue.FromBool(has(perk));
        if (actorHas is null) return PapyrusValue.FromBool(false);
        return PapyrusValue.FromBool(actorHas(refKey, perk) ?? throw new InvalidOperationException("Garden cannot read the actor's perks"));
    }
}
