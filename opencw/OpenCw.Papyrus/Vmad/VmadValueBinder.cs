using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
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
    string? Object = null);

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
        or BoundKind.Object;
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

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
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
        || (shape.Object?.Contains('\u0000') ?? false);

    // Requires: PapyrusOwnerKind declares its members in the same order as Radicle's Native.PapyrusOwnerKind.
    private static Radicle.Interop.VmadOwner ToOwner(PapyrusOwnerId owner) =>
        new((Radicle.Interop.Native.PapyrusOwnerKind)owner.Kind, owner.BaseFormKey, owner.Discriminator);

    private static Radicle.Interop.VmadShape ToShape(VmadValueShape shape) => new(shape.Name, shape.Type,
        shape.HasAlias, shape.ElementCount, shape.Text, shape.Real, shape.Integer, shape.Boolean, shape.Object);

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
                PapyrusValue.FromObject(new EngineObjectReference(value.ObjectReference ?? "")), null),
            _ => new BoundProperty(owner, property.Name, BoundKind.Unsupported, PapyrusValue.None,
                $"{property.Kind}: Radicle bound a value Garden cannot carry"),
        };
    }
}
