using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ActorPackageNativeAdapters(IScriptHost host, EvidenceGate gate, Func<string, bool> reevaluate, Action<string> log)
{
    public const string SystemKey = "system.actor_package";
    public static readonly GameplayNativeFamily EvaluateFamily = NativeBinding.Family("package", SystemKey, "Actor", "EvaluatePackage");

    public int Reevaluated { get; private set; }

    public int Unanswered { get; private set; }

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "forwarded to the AI package runner of the actor",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(EvaluateFamily, Evaluate));

    private PapyrusValue Evaluate(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count > 1) throw new InvalidOperationException("Actor.EvaluatePackage takes at most one flag");
        var key = Key(receiver);
        if (reevaluate(key)) Reevaluated++;
        else
        {
            Unanswered++;
            log($"Actor.EvaluatePackage on {key} reached no package runner");
        }
        return PapyrusValue.None;
    }

    private static string Key(PapyrusValue receiver) =>
        receiver.Reference is ScriptInstance { Owner: { Kind: PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference } owner }
            ? owner.Discriminator
            : EnableNativeAdapters.RefKey(receiver);
}
