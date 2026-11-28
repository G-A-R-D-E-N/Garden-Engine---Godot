using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ReferenceAnimationEventNativeAdapters(IScriptHost host, EvidenceGate gate, ReferenceAnimations animations)
{
    public const string ReceiverScript = "ObjectReference";
    public static readonly GameplayNativeFamily RegisterFamily =
        NativeBinding.Family("refanimation", ReferenceAnimationNativeAdapters.SystemKey, "ScriptObject", "RegisterForAnimationEvent");
    public static readonly GameplayNativeFamily UnregisterFamily =
        NativeBinding.Family("refanimation", ReferenceAnimationNativeAdapters.SystemKey, "ScriptObject", "UnregisterForAnimationEvent");

    // Requires: the natives are declared on ScriptObject and the receiver must be a script that extends ObjectReference.
    public AdapterReport Bind() => NativeBinding.Bind(host, gate, ReferenceAnimationNativeAdapters.SystemKey, "bound to the per-script animation event registrations",
        NativeTargetKind.ScriptInstance, ReceiverScript, new NativeBinding.Native(RegisterFamily, Register), new NativeBinding.Native(UnregisterFamily, Unregister));

    private PapyrusValue Register(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (instance, sourceKey, eventName) = Parse(receiver, arguments);
        return PapyrusValue.FromBool(animations.Register(instance, arguments[0], sourceKey, eventName));
    }

    private PapyrusValue Unregister(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (instance, sourceKey, eventName) = Parse(receiver, arguments);
        animations.Unregister(instance, sourceKey, eventName);
        return PapyrusValue.None;
    }

    private static (ScriptInstance Instance, string SourceKey, string EventName) Parse(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 2 || arguments[0].Kind != PapyrusValueKind.Object || arguments[1].Kind != PapyrusValueKind.String)
            throw new InvalidOperationException("the animation event natives take a sender reference and an event name");
        var instance = receiver.Reference as ScriptInstance ?? throw new InvalidOperationException("the receiver is not an attached script");
        // Requires: an owned script instance sender is read as its owner reference.
        var sender = arguments[0].Reference is ScriptInstance { Owner: { } owner } ? PapyrusValue.FromObject(new EngineObjectReference(owner)) : arguments[0];
        return (instance, EnableNativeAdapters.RefKey(sender), arguments[1].Text ?? "");
    }
}
