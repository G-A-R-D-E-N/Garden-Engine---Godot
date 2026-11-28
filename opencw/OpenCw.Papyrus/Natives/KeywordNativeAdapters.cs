using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class KeywordOverlay
{
    private readonly Dictionary<string, Dictionary<string, bool>> _changes = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string form, string keyword, bool present)
    {
        if (!_changes.TryGetValue(form, out var forForm)) _changes[form] = forForm = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        forForm[keyword] = present;
    }

    public bool? Changed(string form, string keyword) =>
        _changes.TryGetValue(form, out var forForm) && forForm.TryGetValue(keyword, out var present) ? present : null;

    public void Clear() => _changes.Clear();
}

public sealed class KeywordNativeAdapters(IScriptHost host, EvidenceGate gate, KeywordOverlay overlay, Func<string, IReadOnlyCollection<string>?> recordKeywords)
{
    public const string SystemKey = "system.keywords";
    public static readonly GameplayNativeFamily AddFamily = NativeBinding.Family("keyword", SystemKey, "ObjectReference", "AddKeyword");
    public static readonly GameplayNativeFamily RemoveFamily = NativeBinding.Family("keyword", SystemKey, "ObjectReference", "RemoveKeyword");
    public static readonly GameplayNativeFamily HasFamily = NativeBinding.Family("keyword", SystemKey, "ObjectReference", "HasKeyword");
    public static readonly GameplayNativeFamily FormHasFamily = NativeBinding.Family("keyword", SystemKey, "Form", "HasKeyword");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "record keywords with the runtime changes laid over them",
        NativeTargetKind.EngineObject, null,
        new NativeBinding.Native(AddFamily, (receiver, arguments) => Change(receiver, arguments, true)),
        new NativeBinding.Native(RemoveFamily, (receiver, arguments) => Change(receiver, arguments, false)),
        new NativeBinding.Native(HasFamily, Has), new NativeBinding.Native(FormHasFamily, Has));

    private static (string Form, string Keyword) Names(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 1 || arguments[0].Reference is not EngineObjectReference keyword)
            throw new InvalidOperationException("a keyword native takes a form and one keyword");
        return (EnableNativeAdapters.ObjectKey(receiver), keyword.Identity);
    }

    private PapyrusValue Change(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, bool present)
    {
        var (form, keyword) = Names(receiver, arguments);
        overlay.Set(form, keyword, present);
        return PapyrusValue.None;
    }

    // Invariant: a runtime change wins over the record; a form the records do not hold has only its runtime changes.
    private PapyrusValue Has(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var (form, keyword) = Names(receiver, arguments);
        return PapyrusValue.FromBool(overlay.Changed(form, keyword) ?? recordKeywords(form)?.Contains(keyword, StringComparer.OrdinalIgnoreCase) ?? false);
    }
}
