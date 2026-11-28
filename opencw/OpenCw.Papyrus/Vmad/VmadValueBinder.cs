using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Vmad;

public sealed record VmadValueShape(
    string Name,
    string Type,
    bool HasAlias = false,
    int? ElementCount = null,
    string? Text = null,
    double? Real = null,
    long? Integer = null,
    bool? Boolean = null,
    string? Object = null,
    string? ElementType = null,
    IReadOnlyList<VmadValueShape>? Elements = null,
    IReadOnlyList<VmadValueShape>? Members = null);

public enum BoundKind
{
    Int,
    Float,
    Bool,
    String,
    Object,
    AliasBound,
    Array,
    Struct,
    Variable,
    Unknown,
    Unsupported,
}

public sealed record BoundProperty(
    PapyrusOwnerId Owner,
    string Name,
    BoundKind Kind,
    PapyrusValue Value,
    string? Reason)
{
    public bool IsBound => Kind is BoundKind.Int or BoundKind.Float or BoundKind.Bool or BoundKind.String
        or BoundKind.Object or BoundKind.Array or BoundKind.Struct;
}

public sealed record VmadBindReport(
    IReadOnlyList<BoundProperty> Properties,
    int Bound,
    int Unsupported,
    int SilentlyDefaulted,
    IReadOnlyList<string> UnsupportedShapes)
{
    public bool Clean => SilentlyDefaulted == 0;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"properties={Properties.Count} bound={Bound} unsupported={Unsupported} silently-defaulted={SilentlyDefaulted}\n");
        foreach (var property in Properties)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {property.Owner.Key} {property.Name} {property.Kind.ToString().ToLowerInvariant()} {property.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Sha256Hex.OfText(ToReport());
}

public static class VmadValueBinder
{
    public static VmadBindReport Bind(PapyrusOwnerId owner, IEnumerable<VmadValueShape> shapes)
    {
        var all = shapes.ToList();
        var refused = all.Where(HasNul).ToList();
        var report = Radicle.Interop.VmadBinder.Bind(ToOwner(owner), all.Where(shape => !HasNul(shape)).Select(ToShape));
        var properties = report.Properties.Select(property => ToBound(owner, property))
            .Concat(refused.Select(shape => new BoundProperty(owner, shape.Name, BoundKind.Unsupported,
                PapyrusValue.None, "the shape carries an embedded NUL")))
            .ToList();
        if (refused.Count > 0) properties = properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
        return new VmadBindReport(properties, properties.Count(property => property.IsBound),
            properties.Count(property => !property.IsBound), report.SilentlyDefaulted,
            report.UnsupportedShapes.Concat(refused.Select(shape => $"{owner.Key} {shape.Name}: the shape carries an embedded NUL")).ToList());
    }

    private static bool HasNul(VmadValueShape shape) =>
        shape.Name.Contains('\u0000') || shape.Type.Contains('\u0000') || (shape.Text?.Contains('\u0000') ?? false)
        || (shape.Elements?.Any(HasNul) ?? false) || (shape.Members?.Any(HasNul) ?? false) || (shape.Object?.Contains('\u0000') ?? false);

    // Requires: PapyrusOwnerKind declares its members in the same order as Radicle's Native.PapyrusOwnerKind.
    private static Radicle.Interop.VmadOwner ToOwner(PapyrusOwnerId owner) =>
        new((Radicle.Interop.Native.PapyrusOwnerKind)owner.Kind, owner.BaseFormKey, owner.Discriminator);

    private static Radicle.Interop.VmadShape ToShape(VmadValueShape shape) => new(shape.Name, shape.Type,
        shape.HasAlias, shape.ElementCount, shape.Text, shape.Real, shape.Integer, shape.Boolean, shape.Object,
        shape.ElementType, shape.Elements?.Select(ToShape).ToList(), shape.Members?.Select(ToShape).ToList());

    // Constraint: Radicle binds an object with no reference as a null ObjectReference, which the VM holds as None.
    private static PapyrusValue ObjectValue(string? reference) =>
        reference is null ? PapyrusValue.None : PapyrusValue.FromObject(new EngineObjectReference(reference));

    private static PapyrusValue? Element(Radicle.Interop.VmadValue value) => value.Kind switch
    {
        Radicle.Interop.Native.PapyrusValueKind.Int => PapyrusValue.FromInt(value.Integer),
        Radicle.Interop.Native.PapyrusValueKind.Float => PapyrusValue.FromFloat(value.Real),
        Radicle.Interop.Native.PapyrusValueKind.Bool => PapyrusValue.FromBool(value.Boolean),
        Radicle.Interop.Native.PapyrusValueKind.String => PapyrusValue.FromString(value.Text ?? ""),
        Radicle.Interop.Native.PapyrusValueKind.Object => ObjectValue(value.ObjectReference),
        Radicle.Interop.Native.PapyrusValueKind.Array when value.Elements is { } items
            && items.Select(Element).ToList() is var list && list.All(item => item is not null) =>
            PapyrusValue.FromArray(list.Select(item => item!.Value).ToList()),
        Radicle.Interop.Native.PapyrusValueKind.Struct => Member(value),
        _ => null,
    };

    // Requires: a bound struct has members; an empty map means the member guide was lost, so nothing is carried.
    private static PapyrusValue? Member(Radicle.Interop.VmadValue value)
    {
        if (value.Kind != Radicle.Interop.Native.PapyrusValueKind.Struct || value.Members is not { Count: > 0 } members) return null;
        var carried = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, member) in members)
        {
            if (Element(member) is not { } converted) return null;
            carried[name] = converted;
        }
        return PapyrusValue.FromStruct(carried);
    }

    private static BoundProperty ToBound(PapyrusOwnerId owner, Radicle.Interop.VmadBoundProperty property)
    {
        if (property.Value is not { } value)
        {
            return new BoundProperty(owner, property.Name, BoundKind.Unsupported, PapyrusValue.None, property.Reason);
        }
        return property.Kind switch
        {
            Radicle.Interop.VmadBoundKind.Int => new BoundProperty(owner, property.Name, BoundKind.Int,
                PapyrusValue.FromInt(value.Integer), null),
            Radicle.Interop.VmadBoundKind.Float => new BoundProperty(owner, property.Name, BoundKind.Float,
                PapyrusValue.FromFloat(value.Real), null),
            Radicle.Interop.VmadBoundKind.Bool => new BoundProperty(owner, property.Name, BoundKind.Bool,
                PapyrusValue.FromBool(value.Boolean), null),
            Radicle.Interop.VmadBoundKind.String => new BoundProperty(owner, property.Name, BoundKind.String,
                PapyrusValue.FromString(value.Text ?? ""), null),
            Radicle.Interop.VmadBoundKind.Object => new BoundProperty(owner, property.Name, BoundKind.Object,
                ObjectValue(value.ObjectReference), null),
            Radicle.Interop.VmadBoundKind.Array when Element(value) is { } array => new BoundProperty(owner, property.Name,
                BoundKind.Array, array, null),
            Radicle.Interop.VmadBoundKind.Struct when Member(value) is { } structure => new BoundProperty(owner, property.Name,
                BoundKind.Struct, structure, null),
            _ => new BoundProperty(owner, property.Name, BoundKind.Unsupported, PapyrusValue.None,
                $"{property.Kind}: Radicle bound a value Garden cannot carry"),
        };
    }
}
