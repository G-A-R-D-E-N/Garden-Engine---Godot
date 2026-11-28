using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth;

public enum ActorKind
{
    Npc,
    Player,
}

public sealed record ActorIdentity(ActorKind Kind, string RefKey, string BaseKey, PapyrusOwnerId? Owner, Node3D Node)
{
    // Invariant: BaseKey is the placed base (the leveled list for a leveled marker) and owns saves; NpcKey is the NPC_ the actor resolved to and answers record reads about the actor.
    public string NpcKey { get; init; } = BaseKey;
}

public static class ActorRegistry
{
    public const string PlayerKey = "player";

    private static readonly Dictionary<string, ActorIdentity> Actors = new();
    private static readonly ConditionalWeakTable<Node3D, StrongBox<ActorIdentity>> Bound = new();

    public static ActorIdentity? Player { get; private set; }

    public static ActorIdentity? Register(Node3D node, ActorKind kind)
    {
        ActorIdentity identity;
        if (PapyrusRuntime.TryOwner(node, out var owner))
            identity = new ActorIdentity(kind, owner.Discriminator, owner.BaseFormKey, owner, node)
            {
                NpcKey = SpawnIdentity.KeyOf(owner.BaseFormKey, node.GetMeta(NpcPuppetSpawner.ResolvedMeta, "").AsString()),
            };
        else if (kind == ActorKind.Player)
            identity = new ActorIdentity(kind, PlayerKey, "", null, node);
        else
        {
            GD.PrintErr($"[Actor] {node.Name}: no placed reference and base form keys; not an actor.");
            return null;
        }

        if (Actors.TryGetValue(StoreKey(identity), out var previous) && previous.Node != node && GodotObject.IsInstanceValid(previous.Node))
            GD.PrintErr($"[Actor] {StoreKey(identity)} is registered twice; the newer node replaces {previous.Node.Name}.");
        if (Bound.TryGetValue(node, out var box)) box.Value = identity;
        else
        {
            Bound.Add(node, new StrongBox<ActorIdentity>(identity));
            node.TreeExiting += () => Remove(Bound.TryGetValue(node, out var current) ? current.Value : identity);
            node.TreeEntered += () => { if (Bound.TryGetValue(node, out var current)) Add(current.Value); };
        }
        Add(identity);
        return identity;
    }

    public static ActorIdentity[] Npcs() => Actors.Values.Where(identity => identity.Kind == ActorKind.Npc).ToArray();

    public static ActorIdentity? Find(string refKey)
    {
        if (!Actors.TryGetValue(refKey, out var identity) && Player?.RefKey == refKey) identity = Player;
        return identity is not null && GodotObject.IsInstanceValid(identity.Node) ? identity : null;
    }

    public static ActorIdentity? Find(PapyrusOwnerId owner)
    {
        if (Player is { Owner: { } playerOwner } player && playerOwner == owner) return GodotObject.IsInstanceValid(player.Node) ? player : null;
        return owner.Kind is PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference ? Find(owner.Discriminator) : null;
    }

    private static string StoreKey(ActorIdentity identity) => identity.Kind == ActorKind.Player ? PlayerKey : identity.RefKey;

    private static void Add(ActorIdentity identity)
    {
        Actors[StoreKey(identity)] = identity;
        if (identity.Kind == ActorKind.Player) Player = identity;
    }

    private static void Remove(ActorIdentity identity)
    {
        if (Actors.TryGetValue(StoreKey(identity), out var current) && current == identity) Actors.Remove(StoreKey(identity));
        if (Player == identity) Player = null;
    }
}
