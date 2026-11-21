using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class HitEventNativeAdapters(IScriptHost host, EvidenceGate gate, HitRegistrations registrations)
{
    public const string SystemKey = PapyrusEventBridge.KeyPrefix + ActorHitEvents.Hit;
    public static readonly GameplayNativeFamily RegisterFamily = NativeBinding.Family("hitevent", SystemKey, "ScriptObject", "RegisterForHitEvent");
    public static readonly GameplayNativeFamily UnregisterFamily = NativeBinding.Family("hitevent", SystemKey, "ScriptObject", "UnregisterForHitEvent");
    public static readonly GameplayNativeFamily UnregisterAllFamily = NativeBinding.Family("hitevent", SystemKey, "ScriptObject", "UnregisterForAllHitEvents");

    // Requires: the natives are declared on ScriptObject, so any attached script can register.
    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the per-script hit event registrations",
        NativeTargetKind.ScriptInstance, null,
        new NativeBinding.Native(RegisterFamily, Register), new NativeBinding.Native(UnregisterFamily, Unregister), new NativeBinding.Native(UnregisterAllFamily, UnregisterAll));

    private PapyrusValue Register(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        registrations.Register(Instance(receiver), Filter(arguments));
        return PapyrusValue.None;
    }

    private PapyrusValue Unregister(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        registrations.Unregister(Instance(receiver), Filter(arguments));
        return PapyrusValue.None;
    }

    private PapyrusValue UnregisterAll(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 1 || arguments[0].Kind is not (PapyrusValueKind.None or PapyrusValueKind.Object))
            throw new InvalidOperationException("UnregisterForAllHitEvents takes one target reference");
        registrations.UnregisterAll(Instance(receiver), KeyOf(arguments[0]));
        return PapyrusValue.None;
    }

    private static ScriptInstance Instance(PapyrusValue receiver) =>
        receiver.Reference as ScriptInstance ?? throw new InvalidOperationException("the receiver is not an attached script");

    private static HitFilter Filter(IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 9 || arguments[0].Kind != PapyrusValueKind.Object || arguments[8].Kind != PapyrusValueKind.Bool
            || arguments[1].Kind is not (PapyrusValueKind.None or PapyrusValueKind.Object)
            || arguments[2].Kind is not (PapyrusValueKind.None or PapyrusValueKind.Object)
            || arguments[3].Kind is not (PapyrusValueKind.None or PapyrusValueKind.Object)
            || arguments.Skip(4).Take(4).Any(value => value.Kind != PapyrusValueKind.Int))
            throw new InvalidOperationException("the hit event natives take a target, aggressor, source and projectile filter, four int filters and a match flag");
        return new HitFilter(KeyOf(arguments[0])!, KeyOf(arguments[1]), KeyOf(arguments[2]), KeyOf(arguments[3]),
            (int)arguments[4].Integer, (int)arguments[5].Integer, (int)arguments[6].Integer, (int)arguments[7].Integer, arguments[8].Flag);
    }

    // Requires: an owned script instance is read as its owner reference; None is no filter.
    private static string? KeyOf(PapyrusValue value) => value.Kind != PapyrusValueKind.Object ? null : value.Reference switch
    {
        ScriptInstance { Owner: { } owner } => owner.Key,
        EngineObjectReference reference => reference.Identity,
        _ => throw new InvalidOperationException("the hit filter is not a reference"),
    };
}
