using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Vmad;

namespace OpenCommonwealth.Services.Papyrus.Quest;

internal static class QuestStartupBinding
{
    internal static string? Prepare(QuestStartupInput input,
        out Dictionary<string, Dictionary<string, PapyrusValue>> scripts)
    {
        scripts = new(StringComparer.OrdinalIgnoreCase);
        foreach (var script in input.Scripts)
            if (string.IsNullOrWhiteSpace(script)
                || !scripts.TryAdd(script, new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase)))
                return "duplicate or empty quest script attachments are unsupported";
        if (input.Metadata is null) return null;
        foreach (var group in input.Metadata.ScriptProperties.GroupBy(property => property.Script, StringComparer.OrdinalIgnoreCase))
        {
            if (!scripts.TryGetValue(group.Key, out var values))
                return $"quest properties name an unattached script {group.Key}";
            if (BindProperties(input.Quest, group, values) is { } failure) return failure;
        }
        return null;
    }

    // Invariant: the value names the alias and never claims it is filled; an array binds only when every element is alias-bound.
    private static PapyrusValue? AliasValue(string quest, QuestStartupProperty property)
    {
        static PapyrusValue Handle(string quest, QuestStartupValue value, short alias) =>
            PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(value.ObjectKey ?? quest, alias)));
        var value = property.Value;
        if (property.Type == 1 && value.Alias is { } single && single != -1) return Handle(quest, value, single);
        if (property.Type == 11 && value.Elements is { Count: > 0 } elements && elements.All(element => element.Alias is { } alias && alias != -1))
            return PapyrusValue.FromArray(elements.Select(element => Handle(quest, element, element.Alias!.Value)).ToList());
        return null;
    }

    internal static string? PrepareFragmentScript(QuestStartupInput input, out Dictionary<string, PapyrusValue> values)
    {
        values = new(StringComparer.OrdinalIgnoreCase);
        if (input.Metadata is null) return null;
        return BindProperties(input.Quest, input.Metadata.FragmentProperties, values);
    }

    // Invariant: an alias-typed property binds to its quest alias owner for quest and fragment scripts alike.
    private static string? BindProperties(string quest, IEnumerable<QuestStartupProperty> properties, Dictionary<string, PapyrusValue> values)
    {
        var aliasValues = new List<(string Name, PapyrusValue Value)>();
        var shapes = new List<VmadValueShape>();
        foreach (var property in properties)
        {
            if (AliasValue(quest, property) is { } alias) aliasValues.Add((property.Name, alias));
            else shapes.Add(Shape(property));
        }
        var bound = VmadValueBinder.Bind(PapyrusOwnerId.ForForm(quest), shapes);
        if (bound.Unsupported != 0) return string.Join("; ", bound.UnsupportedShapes);
        foreach (var (name, value) in bound.Properties.Select(property => (property.Name, property.Value)).Concat(aliasValues))
            if (string.IsNullOrWhiteSpace(name) || !values.TryAdd(name, value))
                return "duplicate or empty property names are unsupported";
        return null;
    }

    // Constraint: Radicle's lifecycle property carries no array elements and an object only as a bare key, so only scalar properties go through it.
    internal static bool TryLifecycleProperties(QuestStartupInput input, out List<Radicle.Interop.QuestLifecycleProperty> properties)
    {
        properties = new();
        if (input.Metadata is null) return false;
        foreach (var property in input.Metadata.ScriptProperties)
        {
            var value = property.Value;
            var converted = property.Type switch
            {
                2 when value.Text is not null => new Radicle.Interop.QuestLifecycleProperty(property.Script, property.Name, "String", Text: value.Text),
                3 when value.Integer is { } integer => new Radicle.Interop.QuestLifecycleProperty(property.Script, property.Name, "Int", Integer: integer),
                4 when value.RealBits is { } bits => new Radicle.Interop.QuestLifecycleProperty(property.Script, property.Name, "Float", Real: BitConverter.Int32BitsToSingle(bits)),
                5 when value.Boolean is { } flag => new Radicle.Interop.QuestLifecycleProperty(property.Script, property.Name, "Bool", Boolean: flag),
                _ => null,
            };
            if (converted is null) return false;
            properties.Add(converted);
        }
        return true;
    }

    private static VmadValueShape Shape(QuestStartupProperty property)
    {
        var value = property.Value;
        return property.Type switch
        {
            1 => new(property.Name, "Object", HasAlias: value.Alias != -1, Object: value.ObjectKey),
            2 when value.Text is not null => new(property.Name, "String", Text: value.Text),
            3 => new(property.Name, "Int", Integer: value.Integer),
            4 => new(property.Name, "Float", Real: value.RealBits is { } bits ? BitConverter.Int32BitsToSingle(bits) : null),
            5 => new(property.Name, "Bool", Boolean: value.Boolean),
            >= 11 and <= 15 when value.Elements is { } elements => new(property.Name, "Array", ElementCount: elements.Count,
                ElementType: ElementTypes[property.Type - 11], Elements: elements.Select(element => Element(property.Type, element)).ToList()),
            7 when value.Structs is { Count: 1 } structs => new(property.Name, "Struct", ElementCount: structs[0].Members.Count,
                Members: structs[0].Members.Select(Member).ToList()),
            17 when value.Structs is { } structs => new(property.Name, "Array", ElementCount: structs.Count, ElementType: "Struct",
                Elements: structs.Select(item => new VmadValueShape("", "Struct", ElementCount: item.Members.Count,
                    Members: item.Members.Select(Member).ToList())).ToList()),
            7 or 17 => new(property.Name, $"unsupported quest property type {property.Type}: Radicle did not decode its members"),
            >= 11 and <= 15 => new(property.Name, $"unsupported quest property type {property.Type}: Radicle did not decode its elements"),
            _ => new(property.Name, $"unsupported quest property type {property.Type}"),
        };
    }

    private static VmadValueShape Member(QuestStartupMember member) => member.Type switch
    {
        1 => new(member.Name, "Object", HasAlias: member.Value.Alias is { } alias && alias != -1, Object: member.Value.ObjectKey),
        2 => new(member.Name, "String", Text: member.Value.Text),
        3 => new(member.Name, "Int", Integer: member.Value.Integer),
        4 => new(member.Name, "Float", Real: member.Value.RealBits is { } bits ? BitConverter.Int32BitsToSingle(bits) : null),
        5 => new(member.Name, "Bool", Boolean: member.Value.Boolean),
        _ => new(member.Name, $"unsupported struct member type {member.Type}"),
    };

    private static readonly string[] ElementTypes = { "Object", "String", "Int", "Float", "Bool" };

    private static VmadValueShape Element(byte arrayType, QuestStartupValue value) => (arrayType - 11) switch
    {
        0 => new("", "Object", HasAlias: value.Alias is { } alias && alias != -1, Object: value.ObjectKey),
        1 => new("", "String", Text: value.Text),
        2 => new("", "Int", Integer: value.Integer),
        3 => new("", "Float", Real: value.RealBits is { } bits ? BitConverter.Int32BitsToSingle(bits) : null),
        _ => new("", "Bool", Boolean: value.Boolean),
    };
}
