using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: Supports answers false for a form this placer does not spawn and never throws; Place answers the token of the new dynamic reference, or null when it could not place.
public interface IFormPlacer
{
    bool Supports(string baseFormKey);

    string? Place(string receiverRefKey, string baseFormKey, bool initiallyDisabled);
}

public sealed class PlaceAtMeNativeAdapters(IScriptHost host, EvidenceGate gate, IFormPlacer placer)
{
    public const string SystemKey = "system.placeatme";
    public const int MaxCount = 32;
    public static readonly GameplayNativeFamily PlaceFamily =
        NativeBinding.Family("placeatme", SystemKey, "ObjectReference", "PlaceAtMe");

    public int Refused { get; private set; }

    public int Shortfall { get; private set; }

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the hazard spawner; any other form is refused as an unbound native is",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(PlaceFamily, Place));

    // Constraint: an unsupported form throws, so the call is refused exactly as when the native was unbound.
    private PapyrusValue Place(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count is < 1 or > 5 || arguments[0].Kind != PapyrusValueKind.Object)
            throw new InvalidOperationException("PlaceAtMe takes a form and up to four optional arguments");
        if (arguments.Count > 1 && arguments[1].Kind != PapyrusValueKind.Int)
            throw new InvalidOperationException("the PlaceAtMe count is not an int");
        for (var i = 2; i < arguments.Count; i++)
            if (arguments[i].Kind != PapyrusValueKind.Bool) throw new InvalidOperationException("the PlaceAtMe flags are not bools");
        if (FormKey(arguments[0]) is not { } baseKey || !placer.Supports(baseKey))
        {
            Refused++;
            throw new InvalidOperationException("PlaceAtMe is bound for hazard and explosion records only");
        }
        var count = arguments.Count > 1 ? arguments[1].Integer : 1;
        if (count > MaxCount)
        {
            Refused++;
            throw new InvalidOperationException($"PlaceAtMe places at most {MaxCount} copies");
        }
        var disabled = arguments.Count > 3 && arguments[3].Flag;
        string? last = null;
        for (var i = 0; i < count; i++)
        {
            if (placer.Place(refKey, baseKey, disabled) is { } placed) last = placed;
            else Shortfall++;
        }
        return last is null ? PapyrusValue.None : PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForDynamicReference(baseKey, last)));
    }

    private static string? FormKey(PapyrusValue value)
    {
        if (value.Reference is not EngineObjectReference reference) return null;
        var key = reference.Owner is { Kind: PapyrusOwnerKind.Form } owner ? owner.BaseFormKey : reference.Owner is null ? reference.Identity : null;
        return string.IsNullOrWhiteSpace(key) || !key.Contains(':') || key.Contains('#') ? null : key;
    }
}
