using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: answers the target reference key of the link the reference holds for the keyword (null keyword is the link without a keyword), or null when there is none; a reader may throw or answer null for an unreadable reference.
public delegate string? LinkedRefReader(string refKey, string? keywordKey);

public sealed class ReferenceLinkNativeAdapters(IScriptHost host, EvidenceGate gate, LinkedRefReader read)
{
    public const string SystemKey = "system.reflinks";
    public static readonly GameplayNativeFamily GetFamily =
        NativeBinding.Family("reflinks", SystemKey, "ObjectReference", "GetLinkedRef");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the placed reference's linked reference list",
        NativeTargetKind.EngineObject, null, new NativeBinding.Native(GetFamily, Get));

    private PapyrusValue Get(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var refKey = EnableNativeAdapters.RefKey(receiver);
        if (arguments.Count > 1) throw new InvalidOperationException("GetLinkedRef takes at most one keyword argument");
        var keyword = arguments.Count == 0 ? null : KeywordKey(arguments[0]);
        return read(refKey, keyword) is { } target ? PapyrusValue.FromObject(new EngineObjectReference(target)) : PapyrusValue.None;
    }

    // Requires: a keyword is a form, so it arrives as a bare form key (script property) or as a Form owner; None is the keyword-less link and any other shape is refused.
    private static string? KeywordKey(PapyrusValue value)
    {
        if (value.Kind == PapyrusValueKind.None || (value.Kind == PapyrusValueKind.Object && value.Reference is null)) return null;
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the keyword is not a form");
        var key = reference.Identity;
        if (reference.Owner is { } owner)
        {
            if (owner.Kind != PapyrusOwnerKind.Form) throw new InvalidOperationException($"the keyword owner kind {owner.Kind} is unsupported");
            key = owner.BaseFormKey;
        }
        if (string.IsNullOrWhiteSpace(key) || !key.Contains(':') || key.Contains('#'))
            throw new InvalidOperationException("the keyword identity is not a form key");
        return key;
    }
}
