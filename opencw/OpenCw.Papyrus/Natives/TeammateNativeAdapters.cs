using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class TeammateNativeAdapters(IScriptHost host, EvidenceGate gate, Action<string, bool> setTeammate, Func<string, bool> isTeammate,
                                     Action<string, bool>? giveXp = null)
{
    public const string SystemKey = "system.teammate";
    public static readonly GameplayNativeFamily SetFamily =
        new("teammate", "Garden.Interaction", SystemKey, new NativeId("Actor", "SetPlayerTeammate"));
    public static readonly GameplayNativeFamily IsFamily =
        new("teammate", "Garden.Interaction", SystemKey, new NativeId("Actor", "IsPlayerTeammate"));

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        foreach (var (family, handler) in new (GameplayNativeFamily, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue>)[] { (SetFamily, Set), (IsFamily, Is) })
        {
            outcomes.Add(host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
                ? new AdapterOutcome(family, AdapterState.Bound, "bound to the receiver's Garden reference")
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count > 3 || arguments.Any(argument => argument.Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("SetPlayerTeammate takes up to three bool arguments");
        var on = arguments.Count < 1 || arguments[0].Flag;
        setTeammate(refKey, on);
        giveXp?.Invoke(refKey, on && arguments.Count > 2 && arguments[2].Flag);
        return PapyrusValue.None;
    }

    private PapyrusValue Is(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        string refKey;
        try { refKey = EnableNativeAdapters.RefKey(receiver); }
        catch (InvalidOperationException) { return PapyrusValue.FromBool(false); }
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        return PapyrusValue.FromBool(isTeammate(refKey));
    }
}
