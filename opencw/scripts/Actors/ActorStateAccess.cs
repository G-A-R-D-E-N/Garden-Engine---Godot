using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus.Natives;

namespace OpenCommonwealth;

// Requires: main thread; find returns the actor's ActorState or null, all lists every live actor state (the on-mount answer scans mounts for this rider); with a store every flag write is kept by actor identity, so it outlives the live state.
public sealed class ActorStateAccess(Func<string, ActorState?> find, Func<IEnumerable<ActorState>> all, ActorScriptState? store = null) : IActorStateAccess
{
    public bool TrySet(string refKey, ActorStateFlag flag, bool value)
    {
        var state = find(refKey);
        if (state is null && store is null) return false;
        if (flag == ActorStateFlag.CanSpeakWhenDown)
        {
            if (state is not null) state.CanSpeakWhenDown = value;
            store?.SetCanSpeakWhenDown(refKey, value);
        }
        else
        {
            if (state is not null) state.NoBleedoutRecovery = value;
            store?.SetNoBleedoutRecovery(refKey, value);
        }
        return true;
    }

    public bool TryGet(string refKey, ActorStateQuery query, out bool value)
    {
        value = false;
        if (query == ActorStateQuery.OnMount)
        {
            value = all().Any(mount => mount.IsMount && mount.Rider == refKey);
            return true;
        }
        if (find(refKey) is not { } state)
        {
            if (query != ActorStateQuery.NoBleedoutRecovery || store is null) return false;
            value = store.NoBleedoutRecovery(refKey);
            return true;
        }
        value = query switch
        {
            ActorStateQuery.BleedingOut => state.IsEssentialDown,
            ActorStateQuery.BeingRidden => state.Rider is not null,
            _ => state.NoBleedoutRecovery,
        };
        return true;
    }

    // Invariant: copies every stored flag onto the state, clearing as well as setting, so a respawned or reloaded actor matches the store.
    public static void Seed(ActorState state, string refKey, ActorScriptState store)
    {
        state.CanSpeakWhenDown = store.CanSpeakWhenDown(refKey);
        state.NoBleedoutRecovery = store.NoBleedoutRecovery(refKey);
    }
}
