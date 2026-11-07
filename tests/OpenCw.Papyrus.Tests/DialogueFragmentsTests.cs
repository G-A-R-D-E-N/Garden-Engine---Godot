using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Dialogue;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class DialogueFragmentsTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (ConditionEvaluator.OperatorsKey, "ENGINE_POLICY"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetQuestVariable", "PROVEN"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetStageDone", "UNPROVEN"),
        (DialogueFragments.TimingKey, "UNPROVEN"),
        (DialogueFragments.DispatchKey, "ENGINE_POLICY"),
        (DialogueFragments.NativesKey, "UNPROVEN"),
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

    private static (DialogueFragments Fragments, ScriptInstance Instance, List<string> Seen) Setup(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("DialogueScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("Fragment_Begin", new[] { PapyrusInstruction.Return() }, ParameterCount: 1),
            new PapyrusFunction("Fragment_End", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in new[] { "Fragment_Begin", "Fragment_End" })
        {
            host.Overrides.Register("DialogueScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }
        var evaluator = new ConditionEvaluator(gate);
        evaluator.Register("GetQuestVariable", condition => condition.Parameter);
        var instance = host.Attach("DialogueScript");
        host.Initialize(instance);
        return (new DialogueFragments(host, evaluator, gate), instance, seen);
    }

    private static DialogueLine Line(
        string begin = "Fragment_Begin",
        string? end = null,
        IReadOnlyList<PapyrusValue>? arguments = null,
        IReadOnlyList<ConditionInput>? conditions = null) =>
        new("Topic", "Info", "DialogueScript", begin, end,
            arguments ?? new[] { PapyrusValue.FromInt(7) },
            conditions ?? Array.Empty<ConditionInput>());

    [Fact]
    public void AProvenLineRunsItsBeginFragmentWithTheDecodedArguments()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"), (DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);

        var outcome = fragments.Play(Line(), instance);

        Assert.Equal(DialogueState.Played, outcome.State);
        Assert.Equal(new[] { "Fragment_Begin" }, seen);
        Assert.Equal(7, Assert.Single(fragments.SeenArguments).Integer);
    }

    [Fact]
    public void AnUnprovenTimingRunsNothing()
    {
        var gate = Gate((DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);

        var outcome = fragments.Play(Line(), instance);

        Assert.Equal(DialogueState.TimingUnproven, outcome.State);
        Assert.Empty(seen);
        Assert.Equal(1, fragments.Blocked);
    }

    [Fact]
    public void AnIndeterminateConditionBlocksTheLine()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"), (DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);
        var conditions = new[] { new ConditionInput("GetStageDone", "==", 1, false, 1) };

        var outcome = fragments.Play(Line(conditions: conditions), instance);

        Assert.Equal(DialogueState.ConditionsIndeterminate, outcome.State);
        Assert.Contains("GetStageDone", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(seen);
    }

    [Fact]
    public void ADecidedConditionPlaysTheLine()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"), (DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);
        var conditions = new[] { new ConditionInput("GetQuestVariable", ">=", 10, false, 12) };

        var outcome = fragments.Play(Line(conditions: conditions), instance);

        Assert.Equal(DialogueState.Played, outcome.State);
        Assert.Equal(new[] { "Fragment_Begin" }, seen);
    }

    [Fact]
    public void AnEndFragmentRunsAfterTheBeginFragment()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"), (DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);

        var outcome = fragments.Play(Line(end: "Fragment_End"), instance);

        Assert.Equal(DialogueState.Played, outcome.State);
        Assert.Equal(2, outcome.FragmentsRun);
        Assert.Equal(new[] { "Fragment_Begin", "Fragment_End" }, seen);
    }

    [Fact]
    public void UnprovenDialogueNativesBlockTheLineAndAreNamed()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"));
        var (fragments, instance, seen) = Setup(gate);

        var outcome = fragments.Play(Line(), instance);

        Assert.Equal(DialogueState.NativesBlocked, outcome.State);
        Assert.Empty(seen);
        Assert.Contains(fragments.Blockers(), blocker => blocker.Key == DialogueFragments.NativesKey);
        Assert.Single(fragments.Blockers());
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryLine()
    {
        var gate = Gate((DialogueFragments.TimingKey, "PROVEN"), (DialogueFragments.NativesKey, "PROVEN"));
        var (fragments, instance, _) = Setup(gate);
        var lines = new List<DialogueOutcome>
        {
            fragments.Play(Line(), instance),
            fragments.Play(Line(begin: "NotThere"), instance),
        };

        var first = fragments.Report(lines);
        var second = fragments.Report(lines);

        Assert.Equal(first.Lines.Count, first.Played + first.Blocked);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(1, first.Played);
        Assert.Equal(1, first.Blocked);
        Assert.Equal(64, first.Digest().Length);
    }
}
