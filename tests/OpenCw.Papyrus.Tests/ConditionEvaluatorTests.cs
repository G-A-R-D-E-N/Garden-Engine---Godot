using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ConditionEvaluatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReviewOrGroupingWithoutEvidenceNeverReturnsAnAndAnswer(double parameter)
    {
        var evaluator = Evaluator(Gate());
        var report = evaluator.Evaluate(new[]
        {
            new ConditionInput("GetQuestVariable", "==", 1, true, parameter),
            Condition("GetQuestVariable", "==", 1, 1),
        });
        Assert.Null(report.Value);
        Assert.Equal(1, report.Unsupported);
        Assert.Contains("OR", report.Conditions[0].Reason, StringComparison.Ordinal);
    }

    private static readonly (string Key, string Status)[] Rows =
    {
        (ConditionEvaluator.OperatorsKey, "ENGINE_POLICY"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetQuestVariable", "PROVEN"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetStageDone", "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static ConditionEvaluator Evaluator(EvidenceGate gate)
    {
        var evaluator = new ConditionEvaluator(gate);
        evaluator.Register("GetQuestVariable", condition => condition.Parameter);
        return evaluator;
    }

    private static ConditionInput Condition(string function, string comparison, double value, double parameter = 0) =>
        new(function, comparison, value, false, parameter);

    [Fact]
    public void AProvenFunctionIsEvaluated()
    {
        var evaluator = Evaluator(Gate());

        var report = evaluator.Evaluate(new[] { Condition("GetQuestVariable", ">=", 10, parameter: 12) });

        Assert.True(report.Value);
        Assert.Equal(1, report.Evaluated);
        Assert.Equal(0, report.Unsupported);
        Assert.Equal(1, evaluator.Evaluated);
    }

    [Fact]
    public void EveryComparisonOperatorIsApplied()
    {
        var evaluator = Evaluator(Gate());

        Assert.True(evaluator.Evaluate(new[] { Condition("GetQuestVariable", "==", 5, 5) }).Value);
        Assert.True(evaluator.Evaluate(new[] { Condition("GetQuestVariable", "!=", 5, 6) }).Value);
        Assert.True(evaluator.Evaluate(new[] { Condition("GetQuestVariable", ">", 5, 6) }).Value);
        Assert.True(evaluator.Evaluate(new[] { Condition("GetQuestVariable", "<=", 5, 5) }).Value);
        Assert.False(evaluator.Evaluate(new[] { Condition("GetQuestVariable", "<", 5, 5) }).Value);
    }

    [Fact]
    public void AnUnsupportedFunctionNeverDefaultsTrueOrFalse()
    {
        var evaluator = Evaluator(Gate());

        var report = evaluator.Evaluate(new[] { Condition("GetStageDone", "==", 1, 1) });

        Assert.Null(report.Value);
        Assert.True(report.Indeterminate);
        Assert.Equal(new[] { "GetStageDone" }, report.UnsupportedFunctions);
        Assert.Equal(1, report.Unsupported);
        Assert.Equal(1, evaluator.Unsupported);
    }

    [Fact]
    public void OneUnsupportedMemberMakesTheWholeListIndeterminate()
    {
        var evaluator = Evaluator(Gate());

        var report = evaluator.Evaluate(new[]
        {
            Condition("GetQuestVariable", "==", 5, 5),
            Condition("GetStageDone", "==", 1, 1),
        });

        Assert.Null(report.Value);
        Assert.Equal(1, report.Evaluated);
        Assert.Equal(1, report.Unsupported);
        Assert.Equal(2, report.Conditions.Count);
    }

    [Fact]
    public void AnUnprovenFunctionKeyIsNotRegistered()
    {
        var evaluator = Evaluator(Gate());

        Assert.False(evaluator.Register("GetStageDone", _ => 1));
        Assert.Null(evaluator.Evaluate(new[] { Condition("GetStageDone", "==", 1, 1) }).Value);
    }

    [Fact]
    public void AnUnprovenOperatorPolicyDecidesNothing()
    {
        var evaluator = Evaluator(Gate((ConditionEvaluator.OperatorsKey, "UNPROVEN")));

        var report = evaluator.Evaluate(new[] { Condition("GetQuestVariable", "==", 5, 5) });

        Assert.Null(report.Value);
        Assert.Equal(1, report.Unsupported);
    }

    [Fact]
    public void EveryFunctionIsCountedByNameAndTheReportIsDeterministic()
    {
        var evaluator = Evaluator(Gate());
        var conditions = new List<ConditionInput>
        {
            Condition("GetStageDone", "==", 1),
            Condition("GetQuestVariable", "==", 5, 5),
            Condition("GetStageDone", ">", 2),
        };

        var first = evaluator.Evaluate(conditions);
        var second = evaluator.Evaluate(conditions);

        Assert.Null(first.Value);
        Assert.Equal(new[] { "GetStageDone" }, first.UnsupportedFunctions);
        Assert.Equal(first.Conditions.Count, first.Evaluated + first.Unsupported);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
    }

    [Fact]
    public void AnEmptyListDecidesNothing()
    {
        var evaluator = Evaluator(Gate());

        var report = evaluator.Evaluate(Array.Empty<ConditionInput>());

        Assert.Null(report.Value);
        Assert.Empty(report.Conditions);
    }
}
