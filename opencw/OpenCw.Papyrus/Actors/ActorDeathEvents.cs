using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public sealed class ActorDeathEvents
{
    public const string Dying = "OnDying";
    public const string Death = "OnDeath";

    private readonly ActorEventFamilies _families;

    public ActorDeathEvents(ActorEventFamilies families) => _families = families;

    public IReadOnlyList<ActorEventReport> Died(PapyrusOwnerId dead, PapyrusOwnerId? killer)
    {
        var arguments = new[] { killer is { } who ? PapyrusValue.FromObject(new EngineObjectReference(who)) : PapyrusValue.None };
        return new[]
        {
            _families.Deliver(dead, new ActorEvent(Dying, ActorEventPath.DirectOwner, null, arguments)),
            _families.Deliver(dead, new ActorEvent(Death, ActorEventPath.DirectOwner, null, arguments)),
        };
    }
}
