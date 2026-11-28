using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services;
using Radicle.Interop;
using CombatReaction = Radicle.Interop.Native.CombatReaction;

namespace OpenCommonwealth;

internal sealed class RadicleRelationSource(Func<ActorIdentity, IReadOnlyCollection<FormKey>?> factionsOf,
    Func<IReadOnlyList<FormKey>, IReadOnlyList<FormKey>, CombatReaction> reaction) : IRelationSource
{
    private readonly ConcurrentDictionary<(string, string), ActorRelation> _decided = new();
    private int _session = GameLoader.SessionId;

    public static readonly RadicleRelationSource Live = new(LiveFactions, LiveReaction);

    public ActorRelation Between(ActorIdentity observer, ActorIdentity target)
    {
        GameLoader.EnvLifetime.EnterReadLock();
        try
        {
            var session = GameLoader.SessionId;
            if (session != _session) { _decided.Clear(); _session = session; }
            var pair = (observer.NpcKey, target.NpcKey);
            if (_decided.TryGetValue(pair, out var known)) return known;
            if (factionsOf(observer) is not { } own || factionsOf(target) is not { } other) return ActorRelation.Unknown;
            var relation = Map(reaction(own.ToList(), other.ToList()));
            if (relation != ActorRelation.Unknown) _decided[pair] = relation;
            return relation;
        }
        catch (Exception e) when (e is RadicleException or InvalidOperationException or ObjectDisposedException) { return ActorRelation.Unknown; }
        finally { GameLoader.EnvLifetime.ExitReadLock(); }
    }

    public static ActorRelation Map(CombatReaction reaction) => reaction switch
    {
        CombatReaction.Neutral => ActorRelation.Neutral,
        CombatReaction.Enemy => ActorRelation.Enemy,
        CombatReaction.Ally => ActorRelation.Ally,
        CombatReaction.Friendly => ActorRelation.Friend,
        _ => ActorRelation.Unknown,
    };

    private static IReadOnlyCollection<FormKey>? LiveFactions(ActorIdentity actor)
    {
        if (!GameLoader.HasLoadOrder) return null;
        FormKey npc;
        if (actor.Kind == ActorKind.Player)
        {
            var env = GameLoader.Records.Environment;
            if (PlayerOwnership.PlayerBaseKey(env) is not { } player) return null;
            npc = player;
        }
        else if (!GameLoader.TryParseKey(actor.NpcKey, out npc)) return null;
        return RadicleNpcRecords.For(GameLoader.Records).FactsOf(npc).Factions?.Keys.ToList();
    }

    private static CombatReaction LiveReaction(IReadOnlyList<FormKey> actor, IReadOnlyList<FormKey> target) =>
        GameLoader.Records.Environment.FactionReaction(actor, target).Reaction;
}
