using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: Rank answers null when the actor's base memberships cannot be read, so the native is refused rather than answering not a member.
public interface IActorFactions
{
    int? Rank(string actor, string faction);

    void SetRank(string actor, string faction, int rank);

    void Leave(string actor, string faction);
}

public sealed class FactionNativeAdapters(
    IScriptHost host, EvidenceGate gate, Func<string, bool> isPlayer, Func<string, int> rankOf, Func<string, int, bool> setRank, Func<string, bool> leave, IActorFactions? actors = null)
{
    public const string SystemKey = "system.factions";
    private static readonly GameplayNativeFamily RankFamily = Family("GetFactionRank");
    public static readonly GameplayNativeFamily InFamily = Family("IsInFaction");
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
                ? new AdapterOutcome(family, AdapterState.Bound, actors is not null
                    ? "bound to the player's faction overlay; other actors read their base record and a saved overlay"
                    : "bound to the player's faction overlay; other actors are refused")
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

    // Requires: the player answers from the player overlay; any other receiver needs the actor store, and without one it is refused because answering false would contradict its base record.
    private (string Actor, string Faction, bool Player) Faction(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, int count)
    {
        var actor = EnableNativeAdapters.RefKey(receiver);
        var player = isPlayer(actor);
        if (!player && actors is null) throw new InvalidOperationException("Garden keeps factions for the player only");
        if (arguments.Count != count) throw new InvalidOperationException("the native has the wrong arguments");
        return (actor, FactionKey(arguments[0]), player);
    }

    private int RankOf(string actor, string faction, bool player) =>
        player ? rankOf(faction) : actors!.Rank(actor, faction) ?? throw new InvalidOperationException($"the base factions of {actor} are unreadable");

    private PapyrusValue Rank(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (actor, faction, player) = Faction(receiver, arguments, 1);
        return PapyrusValue.FromInt(RankOf(actor, faction, player));
    }

    private PapyrusValue IsIn(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (actor, faction, player) = Faction(receiver, arguments, 1);
        return PapyrusValue.FromBool(RankOf(actor, faction, player) >= 0);
    }

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (actor, faction, player) = Faction(receiver, arguments, 2);
        if (arguments[1].Kind != PapyrusValueKind.Int || arguments[1].Integer < 0)
            throw new InvalidOperationException("the faction rank is not a non-negative integer");
        Write(actor, faction, player, (int)arguments[1].Integer);
        return PapyrusValue.None;
    }

    private PapyrusValue Remove(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (actor, faction, player) = Faction(receiver, arguments, 1);
        if (player) leave(faction);
        else actors!.Leave(actor, faction);
        return PapyrusValue.None;
    }

    private void Write(string actor, string faction, bool player, int rank)
    {
        if (player) setRank(faction, rank);
        else actors!.SetRank(actor, faction, rank);
    }
}
