using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class CustomEventRegistry
{
    private readonly Dictionary<(string Sender, string Event), List<ScriptInstance>> _receivers = new();

    public const int Capacity = 256;

    private readonly Queue<(PapyrusValue Sender, string Key, string Event, PapyrusValue Args)> _sends = new();

    public int Pending => _sends.Count;

    public int Dropped { get; private set; }

    public int NoReceivers { get; private set; }

    public int Delivered { get; private set; }

    public int Failed { get; private set; }

    public bool Send(PapyrusValue sender, string eventName, PapyrusValue args)
    {
        var key = CustomEventNativeAdapters.SenderKey(sender);
        if (_sends.Count >= Capacity)
        {
            Dropped++;
            return false;
        }
        _sends.Enqueue((sender, key, eventName, args));
        return true;
    }

    // Requires: runs outside a native callback; only the sends queued before the call are delivered, so a handler that sends again waits for the next drain.
    public int Drain(IScriptHost host)
    {
        var sent = 0;
        if (_sends.Count != 0) Prune(host.Instances);
        for (var left = _sends.Count; left > 0; left--)
        {
            var (sender, key, eventName, args) = _sends.Dequeue();
            var receivers = ReceiversOf(key, eventName);
            if (receivers.Count == 0) NoReceivers++;
            foreach (var receiver in receivers)
            {
                var result = host.RunEvent(receiver, HandlerPrefix + eventName, sender, args);
                if (result.Completed || result.Reason == VmStopReason.Waiting) Delivered++;
                else Failed++;
                sent++;
            }
        }
        return sent;
    }

    public void Clear() => _sends.Clear();

    public IReadOnlyList<(ScriptInstance Receiver, string Sender, string Event)> Receivers =>
        _receivers.SelectMany(pair => pair.Value.Where(receiver => !receiver.Detached).Select(receiver => (receiver, pair.Key.Sender, pair.Key.Event))).ToArray();

    public void ForgetAll() => _receivers.Clear();

    public const string HandlerPrefix = "::remote_";

    public void Register(ScriptInstance receiver, string sender, string eventName)
    {
        var key = (sender.ToLowerInvariant(), eventName.ToLowerInvariant());
        if (!_receivers.TryGetValue(key, out var list)) _receivers[key] = list = new List<ScriptInstance>();
        if (!list.Contains(receiver)) list.Add(receiver);
    }

    public void Unregister(ScriptInstance receiver, string sender, string eventName)
    {
        var key = (sender.ToLowerInvariant(), eventName.ToLowerInvariant());
        if (_receivers.TryGetValue(key, out var list) && list.Remove(receiver) && list.Count == 0) _receivers.Remove(key);
    }

    public IReadOnlyList<ScriptInstance> ReceiversOf(string sender, string eventName) =>
        _receivers.TryGetValue((sender.ToLowerInvariant(), eventName.ToLowerInvariant()), out var list)
            ? list.Where(receiver => !receiver.Detached).ToArray()
            : Array.Empty<ScriptInstance>();

    public void Prune(IReadOnlyCollection<ScriptInstance> live)
    {
        foreach (var key in _receivers.Keys.ToArray())
        {
            _receivers[key].RemoveAll(receiver => !live.Contains(receiver));
            if (_receivers[key].Count == 0) _receivers.Remove(key);
        }
    }
}

public sealed class CustomEventNativeAdapters(IScriptHost host, EvidenceGate gate, CustomEventRegistry registry)
{
    public const string SystemKey = "system.custom_events";
    public static readonly GameplayNativeFamily RegisterFamily = NativeBinding.Family("custom-event", SystemKey, "ScriptObject", "RegisterForCustomEvent");
    public static readonly GameplayNativeFamily UnregisterFamily = NativeBinding.Family("custom-event", SystemKey, "ScriptObject", "UnregisterForCustomEvent");
    public static readonly GameplayNativeFamily SendFamily = NativeBinding.Family("custom-event", SystemKey, "ScriptObject", "SendCustomEvent");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "registrations are kept and a sent event is queued, then delivered to each receiver outside the native",
        NativeTargetKind.ScriptInstance, null,
        new NativeBinding.Native(RegisterFamily, (receiver, arguments) => Change(receiver, arguments, registry.Register)),
        new NativeBinding.Native(UnregisterFamily, (receiver, arguments) => Change(receiver, arguments, registry.Unregister)),
        new NativeBinding.Native(SendFamily, (sender, arguments) => Send(sender, arguments)));

    private PapyrusValue Send(PapyrusValue sender, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count is < 1 or > 2 || arguments[0].Text is not { Length: > 0 } eventName)
            throw new InvalidOperationException("SendCustomEvent takes an event name and an optional argument array");
        if (!registry.Send(sender, eventName, arguments.Count == 2 ? arguments[1] : PapyrusValue.None))
            throw new InvalidOperationException($"SendCustomEvent {eventName} was refused: {CustomEventRegistry.Capacity} custom events are already queued");
        return PapyrusValue.None;
    }

    private static PapyrusValue Change(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, Action<ScriptInstance, string, string> change)
    {
        if (receiver.Reference is not ScriptInstance instance || arguments.Count != 2 || arguments[1].Text is not { Length: > 0 } eventName)
            throw new InvalidOperationException("a custom event registration takes a sender and an event name");
        change(instance, SenderKey(arguments[0]), eventName);
        return PapyrusValue.None;
    }

    // Invariant: a sender is known by its form (quest, base form) or its placed reference, so the same sender gives one key from either handle kind.
    public static string SenderKey(PapyrusValue sender) => sender.Reference switch
    {
        EngineObjectReference { Owner: { } owner } => OwnerKey(owner),
        EngineObjectReference engine => engine.Identity,
        ScriptInstance { Owner: { } owner } => OwnerKey(owner),
        _ => throw new InvalidOperationException("the custom event sender must be an object with an identity"),
    };

    private static string OwnerKey(PapyrusOwnerId owner) => owner.Kind switch
    {
        PapyrusOwnerKind.PlacedReference => owner.Discriminator,
        PapyrusOwnerKind.Form => owner.BaseFormKey,
        _ => owner.Key,
    };
}
