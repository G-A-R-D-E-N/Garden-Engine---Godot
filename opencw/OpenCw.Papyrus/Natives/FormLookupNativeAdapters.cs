using System;
using System.Collections.Generic;
using System.Globalization;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class FormLookupNativeAdapters(IScriptHost host, EvidenceGate gate, Func<string, (string Key, string? Signature)?> resolveForm, Func<string, double?> globalValue, Action<string, double>? setGlobal = null)
{
    public static readonly GameplayNativeFamily FromFileFamily = NativeBinding.Family("form", CoreNativeAdapters.SystemKey, "Game", "GetFormFromFile");
    public static readonly GameplayNativeFamily GlobalValueFamily = NativeBinding.Family("form", CoreNativeAdapters.SystemKey, "GlobalVariable", "GetValue");
    public static readonly GameplayNativeFamily GlobalSetFamily = NativeBinding.Family("form", CoreNativeAdapters.SystemKey, "GlobalVariable", "SetValue");

    public AdapterReport Bind()
    {
        var statics = NativeBinding.Bind(host, gate, CoreNativeAdapters.SystemKey, "answered from the loaded records",
            NativeTargetKind.Static, null, new NativeBinding.Native(FromFileFamily, FormFromFile));
        var engine = NativeBinding.Bind(host, gate, CoreNativeAdapters.SystemKey, "answered from the loaded records",
            NativeTargetKind.EngineObject, null, setGlobal is null
                ? [new NativeBinding.Native(GlobalValueFamily, GlobalValue)]
                : [new NativeBinding.Native(GlobalValueFamily, GlobalValue), new NativeBinding.Native(GlobalSetFamily, SetGlobalValue)]);
        return new AdapterReport([.. statics.Natives, .. engine.Natives], statics.Bound + engine.Bound, statics.Unbound + engine.Unbound);
    }

    // Invariant: a form missing from the load order answers None, as the game does, never a made-up reference.
    private PapyrusValue FormFromFile(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 2 || arguments[0].Kind != PapyrusValueKind.Int || arguments[1].Kind != PapyrusValueKind.String)
            throw new InvalidOperationException("Game.GetFormFromFile takes a form id and a plugin name");
        var id = (uint)arguments[0].Integer & 0xFFFFFFu;
        var key = $"{id.ToString("X6", CultureInfo.InvariantCulture)}:{arguments[1].Text}";
        return resolveForm(key) is { } found
            ? PapyrusValue.FromObject(new EngineObjectReference(found.Key) { ScriptType = found.Signature is { } signature ? FormScriptTypes.For(signature) : null })
            : PapyrusValue.None;
    }

    // Requires: the value is written for the receiver's global form key and read back by GlobalVariable.GetValue; a non-number argument or a non-form receiver throws.
    private PapyrusValue SetGlobalValue(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (receiver.Reference is not EngineObjectReference form) throw new InvalidOperationException("GlobalVariable.SetValue needs a global variable form");
        if (arguments.Count != 1 || arguments[0].Kind is not (PapyrusValueKind.Float or PapyrusValueKind.Int)) throw new InvalidOperationException("GlobalVariable.SetValue takes one number");
        setGlobal!(form.Identity, arguments[0].Kind == PapyrusValueKind.Int ? arguments[0].Integer : arguments[0].Real);
        return PapyrusValue.None;
    }

    private PapyrusValue GlobalValue(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (receiver.Reference is not EngineObjectReference form) throw new InvalidOperationException("GlobalVariable.GetValue needs a global variable form");
        return globalValue(form.Identity) is { } value
            ? PapyrusValue.FromFloat(value)
            : throw new InvalidOperationException($"{form.Identity} is not a global variable in the load order");
    }
}
