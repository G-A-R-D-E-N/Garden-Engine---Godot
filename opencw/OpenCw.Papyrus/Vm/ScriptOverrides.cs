using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public enum OverrideOutcome
{
    /// <summary>No override is registered for this script and function.</summary>
    NotRegistered,

    /// <summary>The override declined, so bytecode runs.</summary>
    Declined,

    /// <summary>The override handled the call and its value is the result.</summary>
    Handled,

    /// <summary>The call could not be handed to an override at all.</summary>
    Refused,
}

public sealed record OverrideResult(OverrideOutcome Outcome, PapyrusValue Value, string? Reason)
{
    public static OverrideResult NotRegistered => new(OverrideOutcome.NotRegistered, PapyrusValue.None, null);

    public static OverrideResult Handled(PapyrusValue value) => new(OverrideOutcome.Handled, value, null);

    public static OverrideResult Declined(string? reason = null) =>
        new(OverrideOutcome.Declined, PapyrusValue.None, reason);

    public static OverrideResult Refused(string reason) => new(OverrideOutcome.Refused, PapyrusValue.None, reason);
}

/// <summary>What an override is given: the attached instance, the function and its arguments.</summary>
public sealed record OverrideContext(ScriptInstance Instance, string Function, IReadOnlyList<PapyrusValue> Arguments);

public delegate OverrideResult OverrideHandler(OverrideContext context);

/// <summary>Marks a method as a synchronous override for one script function.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ScriptOverrideAttribute : Attribute
{
    public ScriptOverrideAttribute(string script, string function)
    {
        Script = script;
        Function = function;
    }

    public string Script { get; }

    public string Function { get; }
}

/// <summary>
/// Opt-in per-script overrides for bytecode functions and events.
/// </summary>
/// <remarks>
/// An override is registered for one script and one function, so it can only replace a bytecode
/// function or event of a real attached <see cref="ScriptInstance"/>. It never intercepts a native
/// call, because the host asks for an override only at a bytecode entry point, and it never receives
/// a bare reference, because the context carries the instance itself. A call whose argument count
/// does not match the declared parameter count, or a function the script does not define, is refused
/// before the override runs. A declined override falls through to bytecode, and every handled or
/// declined call is counted.
/// </remarks>
public sealed class ScriptOverrideRegistry
{
    public const string Key = "vm.overrides.synchronous";
    public const string ReflectionKey = "vm.overrides.reflection_registration";
    public const int AnyParameterCount = -1;

    private readonly EvidenceGate _gate;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ScriptOverrideRegistry(EvidenceGate gate) => _gate = gate;

    internal event Action<string, string, int>? Registered;

    internal IEnumerable<(string Script, string Function, int ParamCount)> Entries =>
        _entries.Values
            .Select(entry => (Script: entry.Script, Function: entry.Function, ParamCount: entry.ParameterCount))
            .ToArray();

    public int Count => _entries.Count;

    public int HandledCount { get; private set; }

    public int DeclinedCount { get; private set; }

    public int RefusedCount { get; private set; }

    public void Register(string script, string function, int parameterCount, OverrideHandler handler)
    {
        var decision = _gate.Gate(Key);
        if (!decision.Allowed)
        {
            throw new InvalidOperationException($"{decision.Key}: {decision.Reason}");
        }
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(function))
        {
            throw new ArgumentException("an override needs a script and a function");
        }
        _entries[Name(script, function)] = new Entry(script, function, parameterCount, handler);
        Registered?.Invoke(script, function, parameterCount);
    }

    /// <summary>
    /// Registers every method of <paramref name="target"/> marked with
    /// <see cref="ScriptOverrideAttribute"/>. A method whose signature is not an override is
    /// rejected rather than silently skipped.
    /// </summary>
    public void RegisterFromReflection(object target)
    {
        var decision = _gate.Gate(ReflectionKey);
        if (!decision.Allowed)
        {
            throw new InvalidOperationException($"{decision.Key}: {decision.Reason}");
        }

        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = method.GetCustomAttribute<ScriptOverrideAttribute>();
            if (attribute is null)
            {
                continue;
            }
            var parameters = method.GetParameters();
            if (method.ReturnType != typeof(OverrideResult) || parameters.Length != 1
                || parameters[0].ParameterType != typeof(OverrideContext))
            {
                throw new InvalidOperationException(
                    $"the override {method.Name} must take an OverrideContext and return an OverrideResult");
            }
            var handler = (OverrideHandler)Delegate.CreateDelegate(typeof(OverrideHandler), target, method);
            Register(attribute.Script, attribute.Function, AnyParameterCount, handler);
        }
    }

    /// <summary>Looks up an override for an attached instance and a bytecode function.</summary>
    public OverrideResult TryInvoke(ScriptInstance instance, string function,
        IReadOnlyList<PapyrusValue> arguments)
    {
        if (!_entries.TryGetValue(Name(instance.ScriptName, function), out var entry))
        {
            return OverrideResult.NotRegistered;
        }

        if (!instance.DefinesFunction(function))
        {
            RefusedCount++;
            return OverrideResult.Refused(
                $"the script {instance.ScriptName} does not define {function}, so an override must not run");
        }
        var declared = instance.DeclaredParameterCount(function);
        if ((declared is { } declaredCount && declaredCount != arguments.Count)
            || (entry.ParameterCount != AnyParameterCount && entry.ParameterCount != arguments.Count))
        {
            RefusedCount++;
            return OverrideResult.Refused(
                $"the override for {function} requires {declared?.ToString() ?? "an unknown number of"} declared arguments and a compatible registered count; received {arguments.Count}");
        }

        var result = entry.Handler(new OverrideContext(instance, function, arguments));
        switch (result.Outcome)
        {
            case OverrideOutcome.Handled:
                HandledCount++;
                break;
            case OverrideOutcome.Declined:
                DeclinedCount++;
                break;
            default:
                RefusedCount++;
                break;
        }
        return result;
    }

    private static string Name(string script, string function) => $"{script}.{function}";

    private sealed record Entry(string Script, string Function, int ParameterCount, OverrideHandler Handler);
}
