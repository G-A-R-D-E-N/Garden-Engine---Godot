using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: returns false when the actor's crime faction cannot be decided; a true answer with a null faction means the actor reports to nobody.
public delegate bool CrimeFactionReader(string refKey, out string? faction);

// Requires: write is the host's override store (actor reference key, faction form key or null for none); without it SetCrimeFaction stays unbound.
public sealed class CrimeFactionNativeAdapters(IScriptHost host, EvidenceGate gate, CrimeFactionReader read, Action<string, string?>? write = null)
{
    public const string SystemKey = "system.crime";
    public static readonly GameplayNativeFamily GetFamily =
        new("crime", "Garden.Character", SystemKey, new NativeId("Actor", "GetCrimeFaction"));
    public static readonly GameplayNativeFamily SetFamily =
        new("crime", "Garden.Character", SystemKey, new NativeId("Actor", "SetCrimeFaction"));

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        outcomes.Add(BindOne(GetFamily, Get, "bound to the NPC_ crime faction"));
        if (write is not null) outcomes.Add(BindOne(SetFamily, Set, "bound to the Garden crime faction override table"));
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private AdapterOutcome BindOne(GameplayNativeFamily family, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> handler, string bound) =>
        host.TryRegisterNative(family.Native, handler, NativeTargetKind.EngineObject)
            ? NativeBinding.Published(host, family, bound)
            : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved");

    // Requires: an undecidable receiver throws instead of answering None, because None would claim the actor reports to nobody.
    private PapyrusValue Get(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 0) throw new InvalidOperationException("the native has an extra argument");
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (!read(refKey, out var faction)) throw new InvalidOperationException("Garden cannot read the receiver's crime faction");
        return faction is null ? PapyrusValue.None : PapyrusValue.FromObject(new EngineObjectReference(faction));
    }

    // Requires: None as the argument is stored as "reports to nobody" (vanilla scripts call SetCrimeFaction(None)); any other argument must be a form, and a receiver that is not a reference throws.
    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 1) throw new InvalidOperationException("the native needs exactly one faction argument");
        var refKey = EnableNativeAdapters.RefKey(receiver);
        var none = arguments[0].Kind == PapyrusValueKind.None || arguments[0].Kind == PapyrusValueKind.Object && arguments[0].Reference is null;
        write!(refKey, none ? null : FactionNativeAdapters.FactionKey(arguments[0]));
        return PapyrusValue.None;
    }
}
