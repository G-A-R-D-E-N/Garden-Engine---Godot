using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Conditions;

/// <summary>One decoded CTDA condition, as Radicle hands it over.</summary>
public sealed record ConditionInput(string Function, string Comparison, double Value, bool Or, double Parameter);

public enum ConditionState
{
    /// <summary>A proven function decided the condition.</summary>
    Evaluated,

    /// <summary>The function has no proven implementation, so nothing was decided.</summary>
    Unsupported,
}

public sealed record ConditionOutcome(string Function, ConditionState State, bool? Value, string Reason);

public sealed record ConditionReport(
    bool? Value,
    IReadOnlyList<ConditionOutcome> Conditions,
    int Evaluated,
    int Unsupported,
    IReadOnlyList<string> UnsupportedFunctions)
{
    public bool Indeterminate => Value is null;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"value={Value?.ToString() ?? "indeterminate"} conditions={Conditions.Count} evaluated={Evaluated} unsupported={Unsupported}\n");
        foreach (var condition in Conditions)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {condition.Function} {condition.State.ToString().ToLowerInvariant()} value={condition.Value?.ToString()} {condition.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Evaluates a CTDA condition list, one function at a time from evidence.
/// </summary>
/// <remarks>
/// A function is only evaluated when its own evidence key allows it and an implementation is
/// registered; every other function is counted by its exact name and decides nothing. An unsupported
/// condition never defaults to true or false: if any member of the list is undecided the whole list is
/// indeterminate, so a caller cannot read a wrong answer as compatibility. The comparison operators
/// themselves are Garden arithmetic on the decoded values and are their own policy row.
/// </remarks>
public sealed class ConditionEvaluator
{
    public const string OperatorsKey = "condition.comparison_operators";
    public const string FunctionKeyPrefix = "condition.function.";

    private readonly EvidenceGate _gate;
    private readonly Dictionary<string, Func<ConditionInput, double>> _functions = new(StringComparer.Ordinal);

    public ConditionEvaluator(EvidenceGate gate) => _gate = gate;

    public int Evaluated { get; private set; }

    public int Unsupported { get; private set; }

    /// <summary>Registers a function implementation, but only when its own key allows it.</summary>
    public bool Register(string function, Func<ConditionInput, double> body)
    {
        if (!_gate.Gate(FunctionKeyPrefix + function).Allowed)
        {
            return false;
        }
        _functions[function] = body;
        return true;
    }

    public ConditionReport Evaluate(IEnumerable<ConditionInput> conditions)
    {
        var list = conditions.ToList();
        var outcomes = new List<ConditionOutcome>(list.Count);
        var unsupported = new List<string>();
        var evaluated = 0;
        var refused = 0;
        var decided = new List<bool>();

        if (list.Count == 0)
        {
            return new ConditionReport(null, outcomes, 0, 0, unsupported);
        }

        foreach (var condition in list)
        {
            if (condition.Or || !_functions.TryGetValue(condition.Function, out var body)
                || !_gate.Gate(OperatorsKey).Allowed)
            {
                refused++;
                Unsupported++;
                if (!unsupported.Contains(condition.Function, StringComparer.Ordinal))
                {
                    unsupported.Add(condition.Function);
                }
                outcomes.Add(new ConditionOutcome(condition.Function, ConditionState.Unsupported, null,
                    condition.Or ? "OR grouping has no verified implementation, so it decides nothing"
                        : "the function has no proven implementation, so it decides nothing"));
                continue;
            }

            var left = body(condition);
            var value = Compare(left, condition.Comparison, condition.Value);
            evaluated++;
            Evaluated++;
            decided.Add(value);
            outcomes.Add(new ConditionOutcome(condition.Function, ConditionState.Evaluated, value, null));
        }

        if (refused > 0)
        {
            return new ConditionReport(null, outcomes, evaluated, refused, unsupported);
        }

        var combined = decided[0];
        foreach (var value in decided.Skip(1))
        {
            combined = combined && value;
        }
        return new ConditionReport(combined, outcomes, evaluated, refused, unsupported);
    }

    private static bool Compare(double left, string comparison, double right) => comparison switch
    {
        "==" => left == right,
        "!=" => left != right,
        ">" => left > right,
        ">=" => left >= right,
        "<" => left < right,
        "<=" => left <= right,
        _ => throw new InvalidOperationException($"the comparison {comparison} is not recognised"),
    };
}
