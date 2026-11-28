using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ActorValueOverlay
{
    private readonly Dictionary<(string Reference, string Value), double> _values = new();

    public void Set(string reference, string value, double amount) => _values[(reference.ToLowerInvariant(), value.ToLowerInvariant())] = amount;

    public double? Get(string reference, string value) =>
        _values.TryGetValue((reference.ToLowerInvariant(), value.ToLowerInvariant()), out var amount) ? amount : null;

    public void Clear() => _values.Clear();
}

// Requires: returns the base a never-set actor value starts from (reference key, actor value form key) and where it came from, or null when the host cannot derive one.
public delegate double? ActorValueBaseReader(string reference, string value, out string source);

public sealed class ActorValueNativeAdapters(IScriptHost host, EvidenceGate gate, ActorValueOverlay overlay, ActorValueBaseReader? baseOf = null, Action<string>? log = null)
{
    private readonly HashSet<(string, string)> _logged = new();

    public const string SystemKey = "system.actor_values";
    public static readonly GameplayNativeFamily SetFamily = NativeBinding.Family("actor-value", SystemKey, "ObjectReference", "SetValue");
    public static readonly GameplayNativeFamily GetFamily = NativeBinding.Family("actor-value", SystemKey, "ObjectReference", "GetValue");
    public static readonly GameplayNativeFamily ModFamily = NativeBinding.Family("actor-value", SystemKey, "ObjectReference", "ModValue");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "runtime values per reference and actor value form",
        NativeTargetKind.EngineObject, null,
        new NativeBinding.Native(SetFamily, Set), new NativeBinding.Native(GetFamily, Get), new NativeBinding.Native(ModFamily, Mod));

    private static (string Reference, string Value) Names(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, int count)
    {
        if (arguments.Count != count || arguments[0].Reference is not EngineObjectReference value)
            throw new InvalidOperationException("an actor value native takes an actor value form" + (count == 2 ? " and a number" : ""));
        return (CustomEventNativeAdapters.SenderKey(receiver), value.Identity);
    }

    private static double Amount(PapyrusValue value) => value.Kind == PapyrusValueKind.Int ? value.Integer : value.Real;

    private PapyrusValue Set(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (reference, value) = Names(receiver, arguments, 2);
        overlay.Set(reference, value, Amount(arguments[1]));
        return PapyrusValue.None;
    }

    // Requires: a value that was never set starts from the host's base when it can derive one (and says so once by name); with none it stops by name instead of answering zero.
    private double Existing(string reference, string value) => overlay.Get(reference, value)
        ?? FromBase(reference, value)
        ?? throw new InvalidOperationException($"the actor value {value} was never set on {reference} and its record base is not read by this native");

    private double? FromBase(string reference, string value)
    {
        if (baseOf?.Invoke(reference, value, out var source) is not { } amount) return null;
        if (_logged.Add((reference, value))) log?.Invoke($"GetValue {value} on {reference} was never set: answering {amount} from {source}");
        return amount;
    }

    private PapyrusValue Get(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (reference, value) = Names(receiver, arguments, 1);
        return PapyrusValue.FromFloat(Existing(reference, value));
    }

    private PapyrusValue Mod(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (reference, value) = Names(receiver, arguments, 2);
        overlay.Set(reference, value, Existing(reference, value) + Amount(arguments[1]));
        return PapyrusValue.None;
    }
}
