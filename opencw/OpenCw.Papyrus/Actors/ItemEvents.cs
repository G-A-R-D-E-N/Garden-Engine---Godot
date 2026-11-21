using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public sealed class ItemEvents
{
    public const string Added = "OnItemAdded";
    public const string Removed = "OnItemRemoved";

    private readonly ActorEventFamilies _families;

    public ItemEvents(ActorEventFamilies families) => _families = families;

    public IReadOnlyList<ActorEventReport> Changed(PapyrusOwnerId container, IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after)
    {
        var reports = new List<ActorEventReport>();
        foreach (var id in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal))
        {
            var delta = (after.TryGetValue(id, out var now) ? now : 0) - (before.TryGetValue(id, out var was) ? was : 0);
            if (delta == 0) continue;
            var arguments = new[]
            {
                PapyrusValue.FromObject(new EngineObjectReference(id)), PapyrusValue.FromInt(Math.Abs(delta)), PapyrusValue.None, PapyrusValue.None,
            };
            reports.Add(_families.Deliver(container, new ActorEvent(delta > 0 ? Added : Removed, ActorEventPath.DirectOwner, null, arguments)));
        }
        return reports;
    }
}
