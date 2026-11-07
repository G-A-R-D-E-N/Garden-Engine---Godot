using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestStageDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewFailedFragmentStopsSubsequentDispatch(bool runEarlier)
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, host, instance, seen) = Setup(gate, "Earlier", "Fail", "Later");
        host.Overrides!.Register("QuestScript", "Fail", 0, _ =>
        {
            seen.Add("Fail");
            return OverrideResult.Refused("synthetic fragment failure");
        });
        var fragments = new List<StageFragment>();
        if (runEarlier)
        {
            fragments.Add(Owned(10, 0, "Earlier"));
            fragments.Add(Alias(10, 1, "EarlierAlias"));
        }
        fragments.Add(Owned(10, 2, "Fail"));
        fragments.Add(Owned(10, 3, "Later"));
        fragments.Add(Alias(10, 4, "LaterAlias"));

        var report = dispatch.Dispatch(Quest(fragments.ToArray()), 10, instance);

        Assert.Equal(runEarlier ? new[] { "Earlier", "Fail" } : new[] { "Fail" }, seen);
        Assert.False(report.Applied);
        Assert.Equal(runEarlier ? 1 : 0, report.Ran);
        Assert.Equal(report.Ran, dispatch.Ran);
        Assert.Equal(runEarlier ? 1 : 0, report.Deferred);
        Assert.Equal(report.Deferred, dispatch.Deferred);
        Assert.Equal(1, dispatch.RefusedStages);
        Assert.Equal(runEarlier ? 3 : 1, report.Results.Count);
        Assert.Equal(FragmentOutcome.Refused, report.Results[^1].Outcome);
        Assert.Equal("synthetic fragment failure", report.Results[^1].Reason);
    }

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (QuestStageDispatch.TimingKey, "UNPROVEN"),
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

    private static (QuestStageDispatch Dispatch, ScriptHost Host, ScriptInstance Instance, List<string> Seen) Setup(
        EvidenceGate gate, params string[] functions)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", functions
            .Select(name => new PapyrusFunction(name, new[] { PapyrusInstruction.Return() }))
            .ToList(), Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in functions)
        {
            host.Overrides.Register("QuestScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }
        var instance = host.Attach("QuestScript");
        host.Initialize(instance);
        return (new QuestStageDispatch(host, gate), host, instance, seen);
    }

    private static StageDispatchInput Quest(params StageFragment[] fragments) =>
        new("000800:Quests.esp", "QuestScript", fragments);

    private static StageFragment Owned(int stage, int order, string function) =>
        new(stage, order, "QuestScript", function, true);

    private static StageFragment Alias(int stage, int order, string function) =>
        new(stage, order, "AliasScript", function, false);

    [Fact]
    public void AnUnprovenTimingRefusesTheStageAndRunsNothing()
    {
        var gate = Gate();
        var (dispatch, _, instance, seen) = Setup(gate, "FragmentA");
        var report = dispatch.Dispatch(Quest(Owned(10, 0, "FragmentA")), 10, instance);

        Assert.False(report.Applied);
        Assert.Equal(0, report.Ran);
        Assert.Empty(seen);
        Assert.Equal(1, dispatch.RefusedStages);
        Assert.Contains("unproven", report.Results[0].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvenTimingRunsQuestOwnedFragmentsInOrder()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, instance, seen) = Setup(gate, "FragmentB", "FragmentA");
        var report = dispatch.Dispatch(Quest(Owned(10, 1, "FragmentB"), Owned(10, 0, "FragmentA")), 10, instance);

        Assert.True(report.Applied);
        Assert.Equal(2, report.Ran);
        Assert.Equal(new[] { "FragmentA", "FragmentB" }, seen);
        Assert.Equal(2, dispatch.Ran);
    }

    [Fact]
    public void AliasOwnedFragmentsAreDeferredAndNeverRun()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, instance, seen) = Setup(gate, "FragmentA");
        var report = dispatch.Dispatch(Quest(Owned(10, 0, "FragmentA"), Alias(10, 1, "AliasFragment")), 10, instance);

        Assert.Equal(1, report.Ran);
        Assert.Equal(1, report.Deferred);
        Assert.Equal(new[] { "FragmentA" }, seen);
        Assert.Equal(FragmentOutcome.Deferred, report.Results[1].Outcome);
        Assert.Contains("alias", report.Results[1].Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("AliasFragment", seen);
    }

    [Fact]
    public void AMissingFunctionRefusesTheStageWhole()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, instance, seen) = Setup(gate, "FragmentA");
        var report = dispatch.Dispatch(Quest(Owned(10, 0, "FragmentA"), Owned(10, 1, "NotThere")), 10, instance);

        Assert.False(report.Applied);
        Assert.Equal(0, report.Ran);
        Assert.Empty(seen);
        Assert.All(report.Results, result => Assert.Equal(FragmentOutcome.Refused, result.Outcome));
        Assert.Contains("refused whole", report.Results[0].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheChangedStageIsDispatched()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, instance, seen) = Setup(gate, "FragmentA", "FragmentOther");
        var report = dispatch.Dispatch(
            Quest(Owned(10, 0, "FragmentA"), Owned(20, 0, "FragmentOther")), 10, instance);

        Assert.Equal(new[] { "FragmentA" }, seen);
        Assert.Single(report.Results);
        Assert.Equal(1, dispatch.Ran);
    }

    [Fact]
    public void AQuestWithoutAScriptInstanceIsRefused()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, _, seen) = Setup(gate, "FragmentA");
        var report = dispatch.Dispatch(Quest(Owned(10, 0, "FragmentA")), 10, null);

        Assert.False(report.Applied);
        Assert.Empty(seen);
        Assert.Contains("no script instance", report.Results[0].Reason!, StringComparison.Ordinal);
        Assert.Equal(1, dispatch.RefusedStages);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryFragment()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var (dispatch, _, instance, _) = Setup(gate, "FragmentA");
        var quest = Quest(Alias(10, 2, "AliasFragment"), Owned(10, 1, "FragmentA"));

        var report = dispatch.Dispatch(quest, 10, instance);
        var again = dispatch.Dispatch(quest, 10, instance);

        Assert.Equal(report.Results.Count, report.Ran + report.Deferred);
        Assert.Equal(report.ToReport(), again.ToReport());
        Assert.Equal(report.Digest(), again.Digest());
        Assert.Equal(64, report.Digest().Length);
        Assert.Equal(1, report.Ran);
        Assert.Equal(1, report.Deferred);
    }
}
