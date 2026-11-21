using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: keyed by the bare reference form key; false means the reference has no open state here (not a loaded openable), which GetOpenState answers as None and SetOpen leaves unchanged.
public interface IReferenceOpenAccess
{
    bool TrySetOpen(string refKey, bool open);
    bool TryGetOpen(string refKey, out bool open);
}

public sealed class ReferenceOpenNativeAdapters(IScriptHost host, EvidenceGate gate, IReferenceOpenAccess access, ReferenceAnimations? animations = null)
{
    public const string SystemKey = "system.refopen";
    public const int StateNone = 0;
    public const int StateOpen = 1;
    public const int StateOpening = 2;
    public const int StateClosed = 3;
    public const int StateClosing = 4;
    public static readonly GameplayNativeFamily SetFamily =
        NativeBinding.Family("refopen", SystemKey, "ObjectReference", "SetOpen");
    public static readonly GameplayNativeFamily GetFamily =
        NativeBinding.Family("refopen", SystemKey, "ObjectReference", "GetOpenState");

    public int Unapplied { get; private set; }

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the door open state",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(SetFamily, Set), new NativeBinding.Native(GetFamily, Get));

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count > 1 || (arguments.Count == 1 && arguments[0].Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("SetOpen takes at most one bool argument");
        if (!access.TrySetOpen(refKey, arguments.Count == 0 || arguments[0].Flag)) Unapplied++;
        return PapyrusValue.None;
    }

    private PapyrusValue Get(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        var animated = animations?.OpenState(refKey);
        if (animated is StateOpening or StateClosing) return PapyrusValue.FromInt(animated.Value);
        if (!access.TryGetOpen(refKey, out var open)) return PapyrusValue.FromInt(animated ?? StateNone);
        return PapyrusValue.FromInt(open ? StateOpen : StateClosed);
    }
}
