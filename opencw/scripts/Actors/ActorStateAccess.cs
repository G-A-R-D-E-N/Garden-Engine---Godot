using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Natives;

namespace OpenCommonwealth;

// Requires: main thread; find returns the actor's ActorState or null, all lists every live actor state (the on-mount answer scans mounts for this rider).
public sealed class ActorStateAccess(Func<string, ActorState?> find, Func<IEnumerable<ActorState>> all) : IActorStateAccess
{
    public bool TrySet(string refKey, ActorStateFlag flag, bool value)
    {
        if (find(refKey) is not { } state) return false;
        if (flag == ActorStateFlag.CanSpeakWhenDown) state.CanSpeakWhenDown = value;
        else if (flag == ActorStateFlag.IgnoreFriendlyHits) state.IgnoreFriendlyHits = value;
        else if (flag == ActorStateFlag.NotShowOnStealthMeter) state.NotShowOnStealthMeter = value;
        else state.NoBleedoutRecovery = value;
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
        if (find(refKey) is not { } state) return false;
        value = query switch
        {
            ActorStateQuery.BleedingOut => state.IsEssentialDown,
            ActorStateQuery.BeingRidden => state.Rider is not null,
            _ => state.NoBleedoutRecovery,
        };
        return true;
    }
}
