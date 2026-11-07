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
            var bound = VmadValueBinder.Bind(PapyrusOwnerId.ForForm(input.Quest), group.Select(Shape));
            if (bound.Unsupported != 0) return string.Join("; ", bound.UnsupportedShapes);
            foreach (var property in bound.Properties)
                if (string.IsNullOrWhiteSpace(property.Name) || !values.TryAdd(property.Name, property.Value))
                    return $"duplicate or empty property names on {group.Key} are unsupported";
        }
        return null;
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
            _ => new(property.Name, $"unsupported quest property type {property.Type}"),
        };
    }
}
