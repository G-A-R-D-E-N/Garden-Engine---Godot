using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class FactionNativeAdapters(
    IScriptHost host, EvidenceGate gate, Func<string, bool> isPlayer, Func<string, int> rankOf, Func<string, int, bool> setRank, Func<string, bool> leave, Func<string, string, int?>? actorRankOf = null)
{
    public const string SystemKey = "system.factions";
    private static readonly GameplayNativeFamily RankFamily = Family("GetFactionRank");
    private static readonly GameplayNativeFamily InFamily = Family("IsInFaction");
    private static readonly GameplayNativeFamily SetFamily = Family("SetFactionRank");
    private static readonly GameplayNativeFamily RemoveFamily = Family("RemoveFromFaction");

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        foreach (var (family, handler) in new (GameplayNativeFamily, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue>)[]
                 { (RankFamily, Rank), (InFamily, IsIn), (SetFamily, Set), (RemoveFamily, Remove) })
        {
            outcomes.Add(host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
                ? NativeBinding.Published(host, family, "bound: reads answer for the player and, where the base list is readable, other actors; writes are supported for the player only and are refused for other actors")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private static GameplayNativeFamily Family(string name) =>
        new("faction", "Garden.Character", SystemKey, new NativeId("Actor", name));

    // Requires: a faction is a form, so it arrives as a bare form key (script property) or as a Form owner; None and any other shape are refused.
    internal static string FactionKey(PapyrusValue value)
    {
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the faction is none or not a form");
        var key = reference.Identity;
        if (reference.Owner is { } owner)
        {
            if (owner.Kind != PapyrusOwnerKind.Form) throw new InvalidOperationException($"the faction owner kind {owner.Kind} is unsupported");
            key = owner.BaseFormKey;
        }
        if (string.IsNullOrWhiteSpace(key) || !key.Contains(':') || key.Contains('#'))
            throw new InvalidOperationException("the faction identity is not a form key");
        return key;
    }

    // Requires: only the player has a Garden faction overlay, so a write on any other receiver is refused as unsupported.
    private string Faction(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, int count)
    {
        if (!isPlayer(EnableNativeAdapters.RefKey(receiver))) throw new InvalidOperationException("changing factions is unsupported for actors other than the player");
        if (arguments.Count != count) throw new InvalidOperationException("the native has the wrong arguments");
        return FactionKey(arguments[0]);
    }

    // Requires: an unreadable base list for a non-player throws, so the script fails instead of reading a false negative.
    private int RankFor(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (isPlayer(refKey)) return rankOf(Faction(receiver, arguments, 1));
        if (arguments.Count != 1) throw new InvalidOperationException("the native has the wrong arguments");
        return actorRankOf?.Invoke(refKey, FactionKey(arguments[0])) ?? throw new InvalidOperationException("the faction list of this actor is unavailable");
    }

    private PapyrusValue Rank(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) =>
        PapyrusValue.FromInt(RankFor(receiver, arguments));

    private PapyrusValue IsIn(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) =>
        PapyrusValue.FromBool(RankFor(receiver, arguments) >= 0);

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var faction = Faction(receiver, arguments, 2);
        if (arguments[1].Kind != PapyrusValueKind.Int || arguments[1].Integer < 0)
            throw new InvalidOperationException("the faction rank is not a non-negative integer");
        setRank(faction, (int)arguments[1].Integer);
        return PapyrusValue.None;
    }

    private PapyrusValue Remove(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        leave(Faction(receiver, arguments, 1));
        return PapyrusValue.None;
    }
}
