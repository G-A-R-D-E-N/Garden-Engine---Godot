using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ReferencePositionNativeAdapters(IScriptHost host, EvidenceGate gate, Func<string, (double X, double Y, double Z)?> position)
{
    public const string SystemKey = "system.reference_position";
    public static readonly GameplayNativeFamily XFamily = NativeBinding.Family("position", SystemKey, "ObjectReference", "GetPositionX");
    public static readonly GameplayNativeFamily YFamily = NativeBinding.Family("position", SystemKey, "ObjectReference", "GetPositionY");
    public static readonly GameplayNativeFamily ZFamily = NativeBinding.Family("position", SystemKey, "ObjectReference", "GetPositionZ");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "answered from the placed node of the reference",
        NativeTargetKind.EngineObject, null,
        new NativeBinding.Native(XFamily, (receiver, _) => Axis(receiver, p => p.X)),
        new NativeBinding.Native(YFamily, (receiver, _) => Axis(receiver, p => p.Y)),
        new NativeBinding.Native(ZFamily, (receiver, _) => Axis(receiver, p => p.Z)));

    // Invariant: a reference with no placed node stops the script instead of answering the origin.
    private PapyrusValue Axis(PapyrusValue receiver, Func<(double X, double Y, double Z), double> axis)
    {
        var key = EnableNativeAdapters.ObjectKey(receiver);
        return position(key) is { } found
            ? PapyrusValue.FromFloat(axis(found))
            : throw new InvalidOperationException($"{key} has no placed node, so it has no position");
    }
}
