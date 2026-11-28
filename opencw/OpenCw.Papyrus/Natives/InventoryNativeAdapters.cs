using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

// Requires: keyed by the bare reference and item form keys; a reference with no inventory row answers 0 and refuses every write. A null item means every item.
public interface IReferenceInventoryAccess
{
    bool TryAdd(string refKey, string itemKey, int count);

    int Count(string refKey, string? itemKey);

    // Constraint: moves at most count of the item (all of everything when itemKey is null), into toRefKey or out of the game when null; returns the number moved, 0 and no change when either side has no inventory.
    int Move(string refKey, string? toRefKey, string? itemKey, int count);
}

public sealed class InventoryNativeAdapters(IScriptHost host, EvidenceGate gate, IReferenceInventoryAccess access)
{
    public const string SystemKey = "system.refinventory";
    public static readonly GameplayNativeFamily AddFamily = Family("AddItem");
    public static readonly GameplayNativeFamily RemoveFamily = Family("RemoveItem");
    public static readonly GameplayNativeFamily RemoveAllFamily = Family("RemoveAllItems");
    public static readonly GameplayNativeFamily CountFamily = Family("GetItemCount");

    public int Unapplied { get; private set; }

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the reference's Garden inventory",
        NativeTargetKind.EngineObject, null,
        new NativeBinding.Native(AddFamily, Add), new NativeBinding.Native(RemoveFamily, Remove),
        new NativeBinding.Native(RemoveAllFamily, RemoveAll), new NativeBinding.Native(CountFamily, Count));

    private static GameplayNativeFamily Family(string name) => NativeBinding.Family("refinventory", SystemKey, "ObjectReference", name);

    // Constraint: a form arrives as a bare form key or a Form owner; None and any other shape give null, which the natives read as no item.
    private static string? FormKey(PapyrusValue value)
    {
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference reference) return null;
        var key = reference.Identity;
        if (reference.Owner is { } owner)
        {
            if (owner.Kind != PapyrusOwnerKind.Form) return null;
            key = owner.BaseFormKey;
        }
        return string.IsNullOrWhiteSpace(key) || !key.Contains(':') || key.Contains('#') ? null : key;
    }

    private static string? RefKey(PapyrusValue value)
    {
        try { return EnableNativeAdapters.RefKey(value); }
        catch (InvalidOperationException) { return null; }
    }

    // Constraint: null means the argument was left out or is None; a value of the wrong kind throws.
    private static PapyrusValue? Argument(IReadOnlyList<PapyrusValue> arguments, int at, int most, PapyrusValueKind kind)
    {
        if (arguments.Count > most) throw new InvalidOperationException("the native has an extra argument");
        if (at >= arguments.Count) return null;
        var value = arguments[at];
        if (kind == PapyrusValueKind.Object && (value.Kind == PapyrusValueKind.None || value is { Kind: PapyrusValueKind.Object, Reference: null })) return null;
        if (value.Kind != kind) throw new InvalidOperationException("the native has an argument of the wrong type");
        return value;
    }

    private static int CountArgument(IReadOnlyList<PapyrusValue> arguments, int most) =>
        Argument(arguments, 1, most, PapyrusValueKind.Int) is { } count ? (int)Math.Clamp(count.Integer, int.MinValue, int.MaxValue) : 1;

    private PapyrusValue Add(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var item = Argument(arguments, 0, 3, PapyrusValueKind.Object) is { } form ? FormKey(form) : null;
        var count = CountArgument(arguments, 3);
        Argument(arguments, 2, 3, PapyrusValueKind.Bool);
        if (item is null || count <= 0 || RefKey(receiver) is not { } refKey || !access.TryAdd(refKey, item, count)) Unapplied++;
        return PapyrusValue.None;
    }

    private PapyrusValue Remove(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var item = Argument(arguments, 0, 4, PapyrusValueKind.Object) is { } form ? FormKey(form) : null;
        var count = CountArgument(arguments, 4);
        Argument(arguments, 2, 4, PapyrusValueKind.Bool);
        var other = Argument(arguments, 3, 4, PapyrusValueKind.Object);
        if (item is null || count <= 0 || RefKey(receiver) is not { } refKey || !Moved(refKey, other, item, count)) Unapplied++;
        return PapyrusValue.None;
    }

    private PapyrusValue RemoveAll(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var other = Argument(arguments, 0, 2, PapyrusValueKind.Object);
        Argument(arguments, 1, 2, PapyrusValueKind.Bool);
        if (RefKey(receiver) is not { } refKey || !Moved(refKey, other, null, int.MaxValue)) Unapplied++;
        return PapyrusValue.None;
    }

    // Constraint: a named destination that is not a reference refuses the move, so items are never discarded by a bad akOtherContainer.
    private bool Moved(string refKey, PapyrusValue? other, string? item, int count)
    {
        string? to = null;
        if (other is { } target && (to = RefKey(target)) is null) return false;
        return access.Move(refKey, to, item, count) > 0;
    }

    private PapyrusValue Count(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var form = Argument(arguments, 0, 1, PapyrusValueKind.Object);
        var item = form is { } value ? FormKey(value) : null;
        if ((form is not null && item is null) || RefKey(receiver) is not { } refKey) return PapyrusValue.FromInt(0);
        return PapyrusValue.FromInt(access.Count(refKey, item));
    }
}
