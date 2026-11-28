using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Radicle.Interop;
using Radicle.Interop.Native;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using RadicleRecord = Radicle.Interop.Record;

namespace OpenCommonwealth.Services;

public static class BaseScripts
{
    private const byte ObjectType = 1, StringType = 2, IntType = 3, FloatType = 4, BoolType = 5, StructType = 7, StructArrayType = 17;
    private static readonly string[] ElementTypes = { "Object", "String", "Int", "Float", "Bool" };

    public sealed record Result(List<ScriptEntry>? Scripts, bool Incomplete);

    // Requires: the caller holds the environment's read lifetime; a record with no VMAD answers no scripts, and any undecodable read answers Incomplete so the owner is refused instead of attached with a hole.
    public static Result Read(RadicleEnvironment env, RadicleRecord record)
    {
        try
        {
            if (!env.TryScripts(record, out var scripts)) return new Result(null, false);
            return Map(scripts, id => env.ResolveFormId(record, id) is { } key ? key.ToString() : null);
        }
        catch (Exception error) when (error is RadicleException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            return new Result(null, true);
        }
    }

    // Invariant: a read cut short at a named property stays a per-property refusal on that script; only an unsupported read with no usable stop makes the owner Incomplete.
    public static Result Map(IReadOnlyList<VmadScriptInfo> scripts, Func<uint, string?> resolve)
    {
        var entries = scripts.Select((script, index) => new ScriptEntry(script.Name, Properties(script, index, resolve))).ToList();
        return new Result(entries.Count == 0 ? null : entries,
            scripts.Any(script => script.Unsupported && (script.Stop is null || script.Stop.ScriptIndex >= scripts.Count)));
    }

    private static List<ScriptProperty> Properties(VmadScriptInfo script, int index, Func<uint, string?> resolve)
    {
        var properties = script.Properties.Select(property => Property(property, resolve)).ToList();
        if (script.Stop is { } stop && stop.ScriptIndex == index)
        {
            var lost = stop.DeclaredScripts - index - 1;
            properties.Add(Refused(stop.Property.Length > 0 ? stop.Property : "(unnamed property)",
                $"Radicle stopped reading at this property (type {stop.PropertyType}); it and any later property of the script were not read" + (lost > 0 ? $"; {lost} later script(s) of the owner were not read" : "")));
        }
        return properties;
    }

    private static ScriptProperty Property(VmadPropertyInfo row, Func<uint, string?> resolve)
    {
        var name = row.Name;
        switch (row.Type)
        {
            case ObjectType: return Scalar(name, "Object", row.Value, resolve);
            case StringType: return Scalar(name, "String", row.Value, resolve);
            case IntType: return Scalar(name, "Int", row.Value, resolve);
            case FloatType: return Scalar(name, "Float", row.Value, resolve);
            case BoolType: return Scalar(name, "Bool", row.Value, resolve);
        }
        var rows = row.Elements;
        if (row.Type == StructType)
            return rows.Count == 0 ? Refused(name, "struct has no decoded members")
                : new ScriptProperty(name, "Struct", null, Members: rows.Select(member => Member(member, resolve, 1)).ToList());
        if (row.Type == StructArrayType)
        {
            var structs = rows.GroupBy(member => member.StructIndex).OrderBy(group => group.Key)
                .Select(group => (ScriptProperty)new ScriptProperty("", "Struct", null, Members: group.Select(member => Member(member, resolve, 2)).ToList())).ToList();
            return structs.Count != (int)row.DeclaredElementCount
                ? Refused(name, "struct array has structs with no decoded members")
                : new ScriptProperty(name, "Array", null, ElementType: "Struct", Elements: structs);
        }
        if (row.Type is >= 11 and <= 15)
            return rows.Count != (int)row.DeclaredElementCount
                ? Refused(name, "array elements were not all decoded")
                : new ScriptProperty(name, "Array", null, ElementType: ElementTypes[row.Type - 11],
                    Elements: rows.Select(element => Scalar("", ElementTypes[row.Type - 11], element.Value, resolve)).ToList());
        return Refused(name, $"script property type {row.Type} is not reproducible");
    }

    // Requires: depth is this member's shape depth (property 0), Children is the nested value tree Radicle reports for a struct, scalar array or struct array member, and a container whose items would pass VmadBinder.MaxDepth is refused by name.
    private static ScriptProperty Member(VmadElementInfo member, Func<uint, string?> resolve, int depth)
    {
        var name = member.MemberName ?? "";
        if (member.Type is StructType or StructArrayType or (>= 11 and <= 15) && depth + (member.Type == StructArrayType ? 2 : 1) > VmadBinder.MaxDepth)
            return Refused(name, $"nesting is deeper than {VmadBinder.MaxDepth} levels");
        switch (member.Type)
        {
            case ObjectType: return Scalar(name, "Object", member.Value, resolve);
            case StringType: return Scalar(name, "String", member.Value, resolve);
            case IntType: return Scalar(name, "Int", member.Value, resolve);
            case FloatType: return Scalar(name, "Float", member.Value, resolve);
            case BoolType: return Scalar(name, "Bool", member.Value, resolve);
            case StructType:
                return member.Children is { Count: > 0 } members
                    ? new ScriptProperty(name, "Struct", null, Members: members.Select(child => Member(child, resolve, depth + 1)).ToList())
                    : Refused(name, "struct member has no decoded members");
            case >= 11 and <= 15:
                return member.Children is { } items
                    ? new ScriptProperty(name, "Array", null, ElementType: ElementTypes[member.Type - 11],
                        Elements: items.Select(item => Scalar("", ElementTypes[member.Type - 11], item.Value, resolve)).ToList())
                    : Refused(name, "array member elements were not decoded");
            case StructArrayType:
                return member.Children is { } structs
                    ? new ScriptProperty(name, "Array", null, ElementType: "Struct",
                        Elements: structs.Select(item => item.Children is { Count: > 0 } fields
                            ? new ScriptProperty("", "Struct", null, Members: fields.Select(child => Member(child, resolve, depth + 2)).ToList())
                            : Refused("", "struct member has no decoded members")).ToList())
                    : Refused(name, "struct array member structs were not decoded");
            default: return Refused(name, $"struct member type {member.Type} is not reproducible");
        }
    }

    private static ScriptProperty Scalar(string name, string type, VmadPropertyValue value, Func<uint, string?> resolve)
    {
        switch (type)
        {
            case "Object":
                if (value.Alias != -1 || value.Kind != VmadValueKind.Object) return Refused(name, "alias-bound object properties are unproven", type);
                if (value.ObjectFormId == 0) return new ScriptProperty(name, type, null, None: true);
                return resolve(value.ObjectFormId) is { } key
                    ? new ScriptProperty(name, type, key)
                    : Refused(name, "the object form id does not resolve", type);
            case "String": return new ScriptProperty(name, type, value.Text ?? "");
            case "Int": return new ScriptProperty(name, type, value.Integer.ToString(CultureInfo.InvariantCulture));
            case "Float": return new ScriptProperty(name, type, value.Real.ToString("R", CultureInfo.InvariantCulture));
            default: return new ScriptProperty(name, type, value.Boolean ? "true" : "false");
        }
    }

    private static ScriptProperty Refused(string name, string reason, string type = "Unsupported") => new(name, type, null, reason);
}
