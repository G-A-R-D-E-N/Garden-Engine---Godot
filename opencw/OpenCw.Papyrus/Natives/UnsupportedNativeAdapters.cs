using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class UnsupportedNativeAdapters(IScriptHost host, EvidenceGate gate, Action<string> log)
{
    public const string SystemKey = "system.unsupported_natives";

    public static readonly IReadOnlyList<(string Script, string Function, string Missing, NativeTargetKind Kind)> Table = new (string, string, string, NativeTargetKind)[]
    {
        ("Actor", "AddSpell", "magic and spell system", NativeTargetKind.EngineObject),
        ("Actor", "GetAllCombatTargets", "combat target tracker", NativeTargetKind.EngineObject),
        ("Actor", "IsDoingFavor", "companion favor system", NativeTargetKind.EngineObject),
        ("Actor", "SetDoingFavor", "companion favor system", NativeTargetKind.EngineObject),
        ("ObjectReference", "SayCustom", "dialogue voice system", NativeTargetKind.EngineObject),
        ("ObjectReference", "ClearFromOldLocations", "location reference type system", NativeTargetKind.EngineObject),
        ("ObjectReference", "SetLocRefType", "location reference type system", NativeTargetKind.EngineObject),
        ("ObjectReference", "IsWithinBuildableArea", "workshop system", NativeTargetKind.EngineObject),
    };

    public AdapterReport Bind() => Bind(Table);

    public AdapterReport Bind(IReadOnlyList<(string Script, string Function, string Missing, NativeTargetKind Kind)> natives)
    {
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        var outcomes = natives.Select(entry =>
        {
            var family = new GameplayNativeFamily("unsupported", "Garden.Missing", SystemKey, new NativeId(entry.Script, entry.Function));
            var message = $"{entry.Script}.{entry.Function} is not supported: Garden has no {entry.Missing}";
            PapyrusValue Refuse(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
            {
                log(message);
                throw new InvalidOperationException(message);
            }
            return host.TryRegisterNative(family.Native, Refuse, entry.Kind)
                ? new AdapterOutcome(family, AdapterState.Unsupported, message)
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved");
        }).ToList();
        return new AdapterReport(outcomes, 0, outcomes.Count);
    }
}
