using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Events;

// Requires: Enqueue may run inside a native callback; Drain must run outside one, because the host refuses to run an event inside a native.
public sealed class DeferredActivations(PapyrusEventBridge events)
{
    public const int Capacity = 256;

    private readonly Queue<(string Target, PapyrusValue Activator)> _pending = new();

    public int Pending => _pending.Count;

    public int Dropped { get; private set; }

    public int Unrouted { get; private set; }

    public bool Enqueue(string targetKey, PapyrusValue activator)
    {
        if (_pending.Count >= Capacity)
        {
            Dropped++;
            return false;
        }
        _pending.Enqueue((targetKey, activator));
        return true;
    }

    // Invariant: only the requests queued before the call are sent, so a script that activates again from OnActivate waits for the next drain.
    public int Drain()
    {
        var sent = 0;
        for (var left = _pending.Count; left > 0; left--)
        {
            var (target, activator) = _pending.Dequeue();
            if (!events.TryFindReference(target, out var owner))
            {
                Unrouted++;
                continue;
            }
            events.Send(new EngineEvent("OnActivate", new[] { activator }, owner));
            sent++;
        }
        return sent;
    }

    public void Clear() => _pending.Clear();
}
