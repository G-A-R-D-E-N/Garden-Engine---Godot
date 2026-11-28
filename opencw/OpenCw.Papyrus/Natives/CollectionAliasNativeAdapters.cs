using System;
using System.Collections.Generic;
using System.Globalization;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: a RefCollectionAlias receiver is a quest alias owner, like a ReferenceAlias; the members live in the QuestAliasResolver and are saved with the scene state.
public sealed class CollectionAliasNativeAdapters(IScriptHost host, EvidenceGate gate, QuestAliasResolver aliases, Func<string, EngineObjectReference?> reference)
{
    private const string Script = "RefCollectionAlias";

    public AdapterReport Bind()
    {
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(AliasNativeAdapters.SystemKey);
        var outcomes = new List<AdapterOutcome>
        {
            Register("Find", Find),
            Register("GetCount", GetCount),
            Register("GetAt", GetAt),
            Register("AddRef", AddRef),
            Register("RemoveRef", RemoveRef),
            Register("RemoveAll", RemoveAll),
        };
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private AdapterOutcome Register(string function, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> handler)
    {
        var native = new NativeId(Script, function);
        var family = new GameplayNativeFamily("quest-alias", "Garden.Quest", AliasNativeAdapters.SystemKey, native);
        return host.TryRegisterNative(native, handler, NativeTargetKind.EngineObject)
            ? new AdapterOutcome(family, AdapterState.Bound, "bound to the references added to the collection; a collection filled by conditions starts empty")
            : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved");
    }

    private static (string Quest, int Alias) AliasOf(PapyrusValue receiver)
    {
        if (receiver.Reference is EngineObjectReference { Owner: { Kind: PapyrusOwnerKind.QuestAlias } owner } && int.TryParse(owner.Discriminator, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return (owner.BaseFormKey, id);
        throw new InvalidOperationException("the receiver is not a quest alias");
    }

    private static void Arity(IReadOnlyList<PapyrusValue> arguments, int count)
    {
        if (arguments.Count != count) throw new InvalidOperationException("the native has the wrong arguments");
    }

    // Requires: None is not a reference to add, find or remove; a non-object argument is refused.
    private static string? RefOf(PapyrusValue value)
    {
        if (value.Kind == PapyrusValueKind.None || value.Kind == PapyrusValueKind.Object && value.Reference is null) return null;
        if (value.Kind != PapyrusValueKind.Object) throw new InvalidOperationException("the argument is not a reference");
        return EnableNativeAdapters.RefKey(value);
    }

    private PapyrusValue Find(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 1);
        if (RefOf(arguments[0]) is not { } key) return PapyrusValue.FromInt(-1);
        var members = aliases.Members(quest, alias);
        for (var index = 0; index < members.Count; index++)
            if (FormKeyComparer.Instance.Equals(members[index], key)) return PapyrusValue.FromInt(index);
        return PapyrusValue.FromInt(-1);
    }

    private PapyrusValue GetCount(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 0);
        return PapyrusValue.FromInt(aliases.Members(quest, alias).Count);
    }

    private PapyrusValue GetAt(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 1);
        if (arguments[0].Kind != PapyrusValueKind.Int) throw new InvalidOperationException("the index is not an integer");
        var members = aliases.Members(quest, alias);
        var index = arguments[0].Integer;
        return index >= 0 && index < members.Count && reference(members[(int)index]) is { } value ? PapyrusValue.FromObject(value) : PapyrusValue.None;
    }

    private PapyrusValue AddRef(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 1);
        if (RefOf(arguments[0]) is { } key) aliases.AddMember(quest, alias, key);
        return PapyrusValue.None;
    }

    private PapyrusValue RemoveRef(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 1);
        if (RefOf(arguments[0]) is { } key) aliases.RemoveMember(quest, alias, key);
        return PapyrusValue.None;
    }

    private PapyrusValue RemoveAll(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (quest, alias) = AliasOf(receiver);
        Arity(arguments, 0);
        aliases.ClearMembers(quest, alias);
        return PapyrusValue.None;
    }
}
