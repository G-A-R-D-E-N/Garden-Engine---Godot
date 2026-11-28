using System;
using System.Collections.Generic;
using System.Globalization;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class AliasNativeAdapters(IScriptHost host, EvidenceGate gate, QuestAliasResolver aliases, Func<string, EngineObjectReference?> reference)
{
    public const string SystemKey = "system.quest_alias";

    public AdapterReport Bind()
    {
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        var outcomes = new List<AdapterOutcome>
        {
            Register("GetReference", Get),
            Register("ForceRefTo", ForceRefTo),
            Register("Clear", Clear),
        };
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private AdapterOutcome Register(string function, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> handler)
    {
        var native = new NativeId("ReferenceAlias", function);
        var family = new GameplayNativeFamily("quest-alias", "Garden.Quest", SystemKey, native);
        return host.TryRegisterNative(native, handler, NativeTargetKind.EngineObject)
            ? new AdapterOutcome(family, AdapterState.Bound, "bound to the quest alias resolver")
            : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved");
    }

    private static (string Quest, int Alias) AliasOf(PapyrusValue receiver)
    {
        if (receiver.Reference is EngineObjectReference { Owner: { Kind: PapyrusOwnerKind.QuestAlias } owner} && int.TryParse(owner.Discriminator, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return (owner.BaseFormKey, id);
        throw new InvalidOperationException("the receiver is not a quest alias");
    }

    private PapyrusValue Get(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        return aliases.Resolve(quest, alias) is { } refKey && reference(refKey) is { } value ? PapyrusValue.FromObject(value) : PapyrusValue.None;
    }

    private PapyrusValue ForceRefTo(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        if (arguments.Count != 1) throw new InvalidOperationException("ForceRefTo takes one reference");
        if (arguments[0].Kind == PapyrusValueKind.Object) aliases.ForceRefTo(quest, alias, EnableNativeAdapters.RefKey(arguments[0]));
        else aliases.Clear(quest, alias);
        return PapyrusValue.None;
    }

    private PapyrusValue Clear(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        aliases.Clear(quest, alias);
        return PapyrusValue.None;
    }
}
