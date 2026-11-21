using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record HostScalarProperty(string Name, string Type, object Value);

public sealed record HostScalarDeclaration(string Name, string Type, object Default);

public sealed partial class RadicleScriptHost
{
    private readonly Dictionary<string, List<(string Name, string Type, string AutoVariable, object Default)>> _scalarProperties = new(StringComparer.OrdinalIgnoreCase);

    // Constraint: only auto properties with a backing variable are recorded; Radicle has no property setter, so a write goes to that variable.
    private void RecordScalarProperties(Radicle.Interop.PexFile pex, Radicle.Interop.PexObjectCode code, string script)
    {
        var found = new List<(string, string, string, object)>();
        foreach (var property in code.Properties)
        {
            if (property.Name < 0 || property.Type < 0 || property.AutoVarName < 0) continue;
            var count = pex.StringTable.Count;
            if (property.Name >= count || property.Type >= count || property.AutoVarName >= count) continue;
            var type = Encoding.Latin1.GetString(pex.StringTable[property.Type]).ToLowerInvariant() switch
            {
                "int" => "Int",
                "float" => "Float",
                "bool" => "Bool",
                "string" => "String",
                _ => null,
            };
            var variable = Encoding.Latin1.GetString(pex.StringTable[property.AutoVarName]);
            if (type is null || variable.Length == 0) continue;
            found.Add((Encoding.Latin1.GetString(pex.StringTable[property.Name]), type, variable, InitialValue(pex, code, property.AutoVarName, type)));
        }
        _scalarProperties[script] = found;
    }

    // Invariant: a declared initial value of the backing variable wins; a missing or mistyped one falls back to the type default.
    private static object InitialValue(Radicle.Interop.PexFile pex, Radicle.Interop.PexObjectCode code, int variable, string type)
    {
        var declared = code.Variables.FirstOrDefault(candidate => candidate.Name == variable)?.DefaultValue;
        var count = pex.StringTable.Count;
        return (type, declared) switch
        {
            ("Int", { Type: Radicle.Interop.PexValueType.Integer } value) => value.Integer,
            ("Float", { Type: Radicle.Interop.PexValueType.Float } value) when float.IsFinite(value.Real) => value.Real,
            ("Bool", { Type: Radicle.Interop.PexValueType.Bool } value) => value.Boolean,
            ("String", { Type: Radicle.Interop.PexValueType.String } value) when value.String >= 0 && value.String < count => Encoding.Latin1.GetString(pex.StringTable[value.String]),
            ("Int", _) => 0,
            ("Float", _) => 0f,
            ("Bool", _) => false,
            _ => string.Empty,
        };
    }

    private IEnumerable<(string Name, string Type, string AutoVariable, object Default)> DeclaredScalars(string script)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrEmpty(script) && visited.Add(script))
        {
            if (_scalarProperties.TryGetValue(script, out var own))
                foreach (var property in own)
                    if (seen.Add(property.Name)) yield return property;
            if (!_parents.TryGetValue(script, out var parent)) yield break;
            script = parent;
        }
    }

    public IReadOnlyList<HostScalarProperty> ReadScalarProperties(ScriptInstance instance)
    {
        var handle = _table.HandleOf(instance);
        var read = new List<HostScalarProperty>();
        foreach (var (name, type, _, _) in DeclaredScalars(instance.ScriptName))
            if (_runtime.TryGetProperty(handle, name, out var raw) && Normalize(type, raw) is { } value)
                read.Add(new HostScalarProperty(name, type, value));
        return read;
    }

    public IReadOnlyList<HostScalarDeclaration> DeclaredScalarProperties(ScriptInstance instance) =>
        DeclaredScalars(instance.ScriptName).Select(property => new HostScalarDeclaration(property.Name, property.Type, property.Default)).ToArray();

    public bool TryReadScalarProperty(ScriptInstance instance, string name, out object value)
    {
        value = null!;
        var declared = DeclaredScalars(instance.ScriptName)
            .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
        if (declared.Name is null || !_runtime.TryGetProperty(_table.HandleOf(instance), name, out var raw)) return false;
        return Normalize(declared.Type, raw) is { } read && (value = read) is not null;
    }

    // Requires: value is the CLR type Normalize gives for the declared property type.
    public void WriteScalarProperty(ScriptInstance instance, string name, object value)
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("a property cannot be written inside a native callback");
        var variable = DeclaredScalars(instance.ScriptName)
            .First(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).AutoVariable;
        _runtime.SetVariable(_table.HandleOf(instance), variable, value);
    }

    private static object? Normalize(string type, object? raw) => (type, raw) switch
    {
        ("Int", int number) => number,
        ("Int", long number) when number is >= int.MinValue and <= int.MaxValue => (int)number,
        ("Float", float real) when float.IsFinite(real) => real,
        ("Float", double real) when double.IsFinite(real) && float.IsFinite((float)real) => (float)real,
        ("Bool", bool flag) => flag,
        ("String", string text) => text,
        _ => null,
    };
}
