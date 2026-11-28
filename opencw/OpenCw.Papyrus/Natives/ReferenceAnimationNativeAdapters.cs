using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public readonly record struct AnimationRequest(string RefKey, string Animation, string? EventName);

public sealed class ReferenceAnimationNativeAdapters(IScriptHost host, EvidenceGate gate, ReferenceAnimations? animations = null)
{
    public const string SystemKey = "system.refanimation";
    public const int Capacity = 64;
    public static readonly GameplayNativeFamily PlayFamily =
        NativeBinding.Family("refanimation", SystemKey, "ObjectReference", "PlayAnimation");
    public static readonly GameplayNativeFamily PlayAndWaitFamily =
        NativeBinding.Family("refanimation", SystemKey, "ObjectReference", "PlayAnimationAndWait");

    private readonly List<AnimationRequest> _requests = new();

    public IReadOnlyList<AnimationRequest> Requests => _requests;

    public int RequestCount { get; private set; }

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the animation table, or to a no-op that records the request when none is set",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(PlayFamily, Play), new NativeBinding.Native(PlayAndWaitFamily, PlayAndWait));

    private PapyrusValue Play(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) => Record(receiver, arguments, 1);

    private PapyrusValue PlayAndWait(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) => Record(receiver, arguments, 2);

    private PapyrusValue Record(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, int count)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count != count || arguments[0].Kind != PapyrusValueKind.String || (count == 2 && arguments[1].Kind != PapyrusValueKind.String))
            throw new InvalidOperationException("the animation natives take the animation name and, for the waiting form, the event name");
        RequestCount++;
        if (_requests.Count >= Capacity) _requests.RemoveAt(0);
        _requests.Add(new AnimationRequest(refKey, arguments[0].Text ?? "", count == 2 ? arguments[1].Text : null));
        if (animations is null) return PapyrusValue.FromBool(false);
        if (count == 1) return PapyrusValue.FromBool(animations.Play(refKey, arguments[0].Text ?? ""));
        // Invariant: no wait while any script is in OnInit, because the native is not told its caller and a wait there fails that script.
        var wait = !host.Instances.Any(instance => instance.Lifecycle == ScriptLifecycle.Initializing);
        var token = wait ? ReferenceAnimations.NextToken() : 0;
        var started = animations.Play(refKey, arguments[0].Text ?? "", arguments[1].Text, token);
        if (started && wait) throw new NativeWait(token);
        return PapyrusValue.FromBool(started);
    }
}
