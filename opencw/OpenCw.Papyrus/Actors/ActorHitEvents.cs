using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public sealed class ActorHitEvents(HitRegistrations registrations, IScriptHost host, EvidenceGate gate)
{
    public const string Hit = "OnHit";

    public int Registered => registrations.Count;

    // Requires: outside a native callback; returns how many registered scripts ran OnHit.
    public int Struck(PapyrusOwnerId victim, PapyrusOwnerId? aggressor)
    {
        if (registrations.Count == 0 || !gate.Gate(PapyrusEventBridge.KeyPrefix + Hit).Allowed) return 0;
        var blow = new HitBlow(victim.Key, aggressor?.Key);
        var arguments = new PapyrusValue[]
        {
            PapyrusValue.FromObject(new EngineObjectReference(victim)),
            aggressor is { } who ? PapyrusValue.FromObject(new EngineObjectReference(who)) : PapyrusValue.None,
            PapyrusValue.None,
            PapyrusValue.None,
            PapyrusValue.FromBool(blow.Power),
            PapyrusValue.FromBool(blow.Sneak),
            PapyrusValue.FromBool(blow.Bash),
            PapyrusValue.FromBool(blow.Blocked),
            PapyrusValue.FromString(string.Empty),
        };
        var delivered = 0;
        foreach (var instance in registrations.Matching(blow))
        {
            if (instance.Detached || !host.Instances.Contains(instance) || !instance.DefinesFunction(Hit)) continue;
            var result = host.RunEvent(instance, Hit, arguments);
            if (result.Completed || result.Reason == VmStopReason.Waiting) delivered++;
        }
        return delivered;
    }
}
