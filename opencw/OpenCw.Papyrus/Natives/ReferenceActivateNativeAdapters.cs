using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ReferenceActivateNativeAdapters(IScriptHost host, EvidenceGate gate, DeferredActivations queue)
{
    public const string SystemKey = "system.refactivate";
    public static readonly GameplayNativeFamily ActivateFamily =
        NativeBinding.Family("refactivate", SystemKey, "ObjectReference", "Activate");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the deferred OnActivate queue",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(ActivateFamily, Activate));

    // Requires: always answers False because the request is queued, never run inside the native; the drain sends OnActivate unless defaultOnly, then runs default processing.
    private PapyrusValue Activate(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count is < 1 or > 2 || (arguments[0].Kind != PapyrusValueKind.Object && arguments[0].Kind != PapyrusValueKind.None)
            || (arguments.Count == 2 && arguments[1].Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("Activate takes an activator reference and an optional bool");
        var defaultOnly = arguments.Count == 2 && arguments[1].Flag;
        queue.Enqueue(refKey, arguments[0], defaultOnly);
        return PapyrusValue.FromBool(false);
    }
}
