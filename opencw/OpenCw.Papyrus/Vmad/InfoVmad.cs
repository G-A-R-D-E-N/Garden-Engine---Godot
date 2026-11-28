using System;
using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Papyrus.Vmad;

public sealed record InfoFragmentRow(bool End, string Script, string Function);

public sealed record InfoVmadParse(string? Script, IReadOnlyList<VmadValueShape> Properties, IReadOnlyList<InfoFragmentRow> Rows, string? Failure)
{
    public static InfoVmadParse Failed(string reason) => new(null, System.Array.Empty<VmadValueShape>(), System.Array.Empty<InfoFragmentRow>(), reason);
}

public static class InfoVmad
{
    private const byte ObjectType = 1, StringType = 2, IntType = 3, FloatType = 4, BoolType = 5, VariableType = 6, StructType = 7, StructArrayType = 17, VariableArrayType = 16;

    private static readonly string[] ElementTypes = { "Object", "String", "Int", "Float", "Bool" };

    // Requires: resolve maps a raw FormID in the INFO's master space to a record key (null for no object); the result comes from DialogueInfoResult of the same INFO.
    public static InfoVmadParse Of(DialogueInfoResult result, Func<uint, string?> resolve)
    {
        if (result.FragmentState == Radicle.Interop.Native.InfoFragmentState.Unreadable)
            return InfoVmadParse.Failed(result.FragmentFailure ?? "the INFO fragment section is unreadable");
        if (result.FragmentState != Radicle.Interop.Native.InfoFragmentState.Present)
            return new InfoVmadParse(null, System.Array.Empty<VmadValueShape>(), System.Array.Empty<InfoFragmentRow>(), null);
        if (result.FragmentScript is not { } fragment)
            return InfoVmadParse.Failed("the INFO reports a fragment section but Radicle returned no fragment script");
        if (fragment.Stop is { } stop)
            return InfoVmadParse.Failed($"the INFO fragment script {fragment.Name} stopped at property {stop.Property} (type {stop.PropertyType})");
        var properties = new List<VmadValueShape>();
        foreach (var property in fragment.Properties)
        {
            if (Property(property, fragment.Unsupported, resolve, out var shape) is { } failure) return InfoVmadParse.Failed(failure);
            properties.Add(shape);
        }
        var rows = result.Fragments.Select(row => new InfoFragmentRow(row.Kind == Radicle.Interop.InfoFragmentKind.OnEnd, row.Script, row.Function)).ToList();
        return new InfoVmadParse(fragment.Name, properties, rows, null);
    }

    // Invariant: a property the engine cannot carry becomes a shape whose Type names the refusal, so only a binding of that property is refused; only an undecodable type fails the whole parse.
    private static string? Property(VmadPropertyInfo property, bool scriptUnsupported, Func<uint, string?> resolve, out VmadValueShape shape)
    {
        var name = property.Name;
        var rows = property.Elements;
        var declared = (int)property.DeclaredElementCount;
        shape = null!;
        switch (property.Type)
        {
            case ObjectType or StringType or IntType or FloatType or BoolType:
                shape = Scalar(name, property.Type, property.Value, resolve);
                return null;
            case VariableType:
                shape = new(name, "unsupported property type 6 (Variable)");
                return null;
            case VariableArrayType:
                shape = new(name, "unsupported property type 16 (Array of Variable)");
                return null;
            case StructType:
            {
                var members = Members(rows, resolve, 1);
                shape = members is null || members.Count != declared
                    ? new(name, "unsupported struct property: its members were not all decoded")
                    : new(name, "Struct", ElementCount: members.Count, Members: members);
                return null;
            }
            case StructArrayType:
                shape = StructArray(name, rows, declared, scriptUnsupported, resolve);
                return null;
            case >= 11 and <= 15:
            {
                var type = ElementTypes[property.Type - 11];
                shape = rows.Count != declared
                    ? new(name, "unsupported array property: its elements were not all decoded")
                    : new(name, "Array", ElementCount: rows.Count, ElementType: type,
                        Elements: rows.Select(row => Scalar("", (byte)(property.Type - 10), row.Value, resolve)).ToList());
                return null;
            }
            default:
                return $"unknown property type {property.Type} on {name}";
        }
    }

    // Constraint: a struct with no members has no row, so an empty struct is rebuilt from its missing StructIndex; with no rows at all and Unsupported set, the structs are lost rather than empty.
    private static VmadValueShape StructArray(string name, IReadOnlyList<VmadElementInfo> rows, int declared, bool scriptUnsupported, Func<uint, string?> resolve)
    {
        var refused = new VmadValueShape(name, "unsupported struct array property: its structs were not all decoded");
        if (declared > 0 && scriptUnsupported && rows.Count == 0) return refused;
        var byIndex = rows.GroupBy(row => row.StructIndex).ToDictionary(group => group.Key, group => group.ToList());
        if (byIndex.Keys.Any(index => index < 0 || index >= declared)) return refused;
        var items = new List<VmadValueShape>();
        for (var index = 0; index < declared; ++index)
        {
            var members = Members(byIndex.TryGetValue(index, out var found) ? found : new List<VmadElementInfo>(), resolve, 2);
            if (members is null) return refused;
            items.Add(new("", "Struct", ElementCount: members.Count, Members: members));
        }
        return new(name, "Array", ElementCount: items.Count, ElementType: "Struct", Elements: items);
    }

    private static List<VmadValueShape>? Members(IReadOnlyList<VmadElementInfo> rows, Func<uint, string?> resolve, int depth)
    {
        var members = new List<VmadValueShape>();
        foreach (var row in rows)
        {
            if (Value(row, resolve, depth) is not { } shape) return null;
            members.Add(shape);
        }
        return members;
    }

    // Requires: depth is this row's shape depth (property 0) and Children is the nested value tree Radicle reports for a struct, scalar array or struct array member; an undecodable value or a container whose items would pass VmadBinder.MaxDepth refuses the whole member.
    private static VmadValueShape? Value(VmadElementInfo row, Func<uint, string?> resolve, int depth)
    {
        var name = row.MemberName ?? "";
        if (row.Type is StructType or StructArrayType or (>= 11 and <= 15) && depth + (row.Type == StructArrayType ? 2 : 1) > VmadBinder.MaxDepth) return null;
        switch (row.Type)
        {
            case >= ObjectType and <= BoolType:
                return Scalar(name, row.Type, row.Value, resolve);
            case StructType:
                return Members(row.Children ?? [], resolve, depth + 1) is { } members
                    ? new(name, "Struct", ElementCount: members.Count, Members: members)
                    : null;
            case >= 11 and <= 15:
                return row.Children is { } items
                    ? new(name, "Array", ElementCount: items.Count, ElementType: ElementTypes[row.Type - 11],
                        Elements: items.Select(item => Scalar("", (byte)(row.Type - 10), item.Value, resolve)).ToList())
                    : null;
            case StructArrayType:
            {
                if (row.Children is not { } structs) return null;
                var elements = new List<VmadValueShape>();
                foreach (var item in structs)
                {
                    if (Members(item.Children ?? [], resolve, depth + 2) is not { } fields) return null;
                    elements.Add(new("", "Struct", ElementCount: fields.Count, Members: fields));
                }
                return new(name, "Array", ElementCount: elements.Count, ElementType: "Struct", Elements: elements);
            }
            default:
                return null;
        }
    }

    private static VmadValueShape Scalar(string name, byte type, VmadPropertyValue value, Func<uint, string?> resolve) => type switch
    {
        ObjectType => new(name, "Object", HasAlias: value.Alias != -1, Object: value.ObjectFormId == 0 ? null : resolve(value.ObjectFormId)),
        StringType => new(name, "String", Text: value.Text ?? ""),
        IntType => new(name, "Int", Integer: value.Integer),
        FloatType => new(name, "Float", Real: value.Real),
        _ => new(name, "Bool", Boolean: value.Boolean),
    };
}
